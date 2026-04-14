using System.Net.Http.Json;
using System.Text.Json;

namespace APM.StaffZen.Blazor.Services
{
    /// <summary>
    /// Calls the API's /api/Locations endpoints.
    /// Handles all location CRUD plus the "Add Missing Location" pin-drag flow.
    /// </summary>
    public class LocationService
    {
        private readonly IHttpClientFactory _factory;

        public LocationService(IHttpClientFactory factory) => _factory = factory;

        // ── GET all active locations ────────────────────────────────────────
        public async Task<List<LocationDto>> GetAllAsync()
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.GetAsync("api/Locations");
                if (!resp.IsSuccessStatusCode) return new();
                return await resp.Content.ReadFromJsonAsync<List<LocationDto>>() ?? new();
            }
            catch { return new(); }
        }

        // ── GET archived locations ──────────────────────────────────────────
        public async Task<List<LocationDto>> GetArchivedAsync()
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.GetAsync("api/Locations/archived");
                if (!resp.IsSuccessStatusCode) return new();
                return await resp.Content.ReadFromJsonAsync<List<LocationDto>>() ?? new();
            }
            catch { return new(); }
        }

        // ── POST normal add (address-search result) ─────────────────────────
        public async Task<LocationDto?> AddAsync(SaveLocationRequest req)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.PostAsJsonAsync("api/Locations", req);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadFromJsonAsync<LocationDto>();
            }
            catch { return null; }
        }

        // ── POST add missing location (pin-drag flow) ★ ─────────────────────
        /// <summary>
        /// Called when the user clicks "+ Add missing location", drags the pin
        /// on the map, then clicks Done and fills in the details panel.
        /// IsMissing is forced to true server-side; setting it here makes the
        /// intent clear to future readers.
        /// </summary>
        public async Task<MissingLocationResult> AddMissingAsync(SaveLocationRequest req)
        {
            try
            {
                req.IsMissing = true;   // always for this flow
                var client = _factory.CreateClient("API");
                var resp   = await client.PostAsJsonAsync("api/Locations/missing", req);
                var body   = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    // Try to surface the API error message to the UI
                    try
                    {
                        var errDoc = JsonDocument.Parse(body).RootElement;
                        var msg    = errDoc.TryGetProperty("error", out var ep) ? ep.GetString() : body;
                        return new MissingLocationResult { Success = false, Error = msg ?? "Unknown error." };
                    }
                    catch { return new MissingLocationResult { Success = false, Error = body }; }
                }

                var doc = JsonDocument.Parse(body).RootElement;
                LocationDto? location = null;
                if (doc.TryGetProperty("location", out var locEl))
                    location = JsonSerializer.Deserialize<LocationDto>(locEl.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                bool geofenceReady = doc.TryGetProperty("geofenceReady", out var gfEl) && gfEl.GetBoolean();
                return new MissingLocationResult { Success = true, Location = location, GeofenceReady = geofenceReady };
            }
            catch (Exception ex)
            {
                return new MissingLocationResult { Success = false, Error = ex.Message };
            }
        }

        // ── PUT update existing location ────────────────────────────────────
        public async Task<LocationDto?> UpdateAsync(int id, SaveLocationRequest req)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.PutAsJsonAsync($"api/Locations/{id}", req);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadFromJsonAsync<LocationDto>();
            }
            catch { return null; }
        }

        // ── PUT bulk radius update ──────────────────────────────────────────
        public async Task<bool> BulkUpdateRadiusAsync(List<int> ids, int radiusMetres)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.PutAsJsonAsync("api/Locations/bulk-radius",
                    new { LocationIds = ids, RadiusMetres = radiusMetres });
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // ── DELETE (archive) ────────────────────────────────────────────────
        public async Task<bool> ArchiveAsync(int id)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.DeleteAsync($"api/Locations/{id}");
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // ── DELETE permanent ────────────────────────────────────────────────
        public async Task<bool> DeletePermanentAsync(int id)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.DeleteAsync($"api/Locations/{id}/permanent");
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // ── POST restore ────────────────────────────────────────────────────
        public async Task<bool> RestoreAsync(int id)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.PostAsync($"api/Locations/{id}/restore", null);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // ── Live Location Tracking ───────────────────────────────────────────

        public async Task<bool> UpdateLiveLocationAsync(int employeeId, double latitude, double longitude,
                                                        float? accuracy = null, float? speed = null)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.PostAsJsonAsync("api/LiveLocation/update", new
                {
                    EmployeeId = employeeId,
                    Latitude   = latitude,
                    Longitude  = longitude,
                    Accuracy   = accuracy,
                    Speed      = speed,
                });
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<List<LiveMemberDto>> GetCurrentLocationsAsync()
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.GetAsync("api/LiveLocation/current");
                if (!resp.IsSuccessStatusCode) return new();
                var arr    = System.Text.Json.JsonDocument.Parse(
                                 await resp.Content.ReadAsStringAsync()).RootElement;
                var list   = new List<LiveMemberDto>();
                foreach (var item in arr.EnumerateArray())
                {
                    list.Add(new LiveMemberDto
                    {
                        EmployeeId  = item.GetProperty("employeeId").GetInt32(),
                        FullName    = item.GetProperty("fullName").GetString() ?? "",
                        HasLocation = item.GetProperty("hasLocation").GetBoolean(),
                        Latitude    = item.TryGetProperty("latitude",  out var lat) && lat.ValueKind  != System.Text.Json.JsonValueKind.Null ? lat.GetDouble()   : 0,
                        Longitude   = item.TryGetProperty("longitude", out var lng) && lng.ValueKind  != System.Text.Json.JsonValueKind.Null ? lng.GetDouble()   : 0,
                        RecordedAt  = item.TryGetProperty("recordedAt",out var ra)  && ra.ValueKind   != System.Text.Json.JsonValueKind.Null ? ra.GetDateTime()  : (DateTime?)null,
                    });
                }
                return list;
            }
            catch { return new(); }
        }

        public async Task<List<RoutePointDto>> GetRoutesAsync(int employeeId, DateTime date)
        {
            try
            {
                var client  = _factory.CreateClient("API");
                var dateStr = date.ToString("yyyy-MM-dd");
                var resp    = await client.GetAsync($"api/LiveLocation/routes/{employeeId}?date={dateStr}");
                if (!resp.IsSuccessStatusCode) return new();
                var arr  = System.Text.Json.JsonDocument.Parse(
                               await resp.Content.ReadAsStringAsync()).RootElement;
                var list = new List<RoutePointDto>();
                foreach (var item in arr.EnumerateArray())
                    list.Add(new RoutePointDto
                    {
                        Lat        = item.GetProperty("lat").GetDouble(),
                        Lng        = item.GetProperty("lng").GetDouble(),
                        RecordedAt = item.GetProperty("recordedAt").GetDateTime(),
                    });
                return list;
            }
            catch { return new(); }
        }

        public async Task<GeofenceCheckResult> CheckGeofenceAsync(double latitude, double longitude)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.PostAsJsonAsync("api/LiveLocation/geofence-check",
                    new { Latitude = latitude, Longitude = longitude });
                if (!resp.IsSuccessStatusCode) return new();
                var doc = System.Text.Json.JsonDocument.Parse(
                              await resp.Content.ReadAsStringAsync()).RootElement;
                return new GeofenceCheckResult
                {
                    IsInsideGeofence = doc.GetProperty("isInsideGeofence").GetBoolean(),
                };
            }
            catch { return new(); }
        }

    }

    // ── DTOs / Result types ─────────────────────────────────────────────────

    public class LocationDto
    {
        public int     Id            { get; set; }
        public string  Name          { get; set; } = "";
        public double  Latitude      { get; set; }
        public double  Longitude     { get; set; }
        public string? Street        { get; set; }
        public string? City          { get; set; }
        public string? Country       { get; set; }
        public string? PostalCode    { get; set; }
        public int     RadiusMetres  { get; set; } = 300;
        public bool    IsMissing     { get; set; }
        public bool    IsArchived    { get; set; }
        public bool    GeofenceReady { get; set; }

        // Convenience: build a display address string for the UI
        public string DisplayAddress =>
            string.Join(", ", new[] { Street, City, Country, PostalCode }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    public class SaveLocationRequest
    {
        public string  Name         { get; set; } = "";
        public double  Latitude     { get; set; }
        public double  Longitude    { get; set; }
        public string? Street       { get; set; }
        public string? City         { get; set; }
        public string? Country      { get; set; }
        public string? PostalCode   { get; set; }
        public int     RadiusMetres { get; set; } = 300;
        public bool    IsMissing    { get; set; } = false;
    }

    public class MissingLocationResult
    {
        public bool          Success       { get; set; }
        public LocationDto?  Location      { get; set; }
        public bool          GeofenceReady { get; set; }
        public string?       Error         { get; set; }
    }
    // ── Live Location DTOs ────────────────────────────────────────────────────

    /// <summary>Current location of a clocked-in employee, returned by GetCurrentLocationsAsync.</summary>
    public class LiveMemberDto
    {
        public int       EmployeeId  { get; set; }
        public string    FullName    { get; set; } = "";
        public bool      HasLocation { get; set; }
        public double    Latitude    { get; set; }
        public double    Longitude   { get; set; }
        public DateTime? RecordedAt  { get; set; }
    }

    /// <summary>A single GPS point in a Route, returned by GetRoutesAsync.</summary>
    public class RoutePointDto
    {
        public double   Lat        { get; set; }
        public double   Lng        { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    /// <summary>Result of a geofence membership check.</summary>
    public class GeofenceCheckResult
    {
        public bool IsInsideGeofence { get; set; }
    }

}