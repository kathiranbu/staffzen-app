using System.Net.Http.Json;
using System.Text.Json;
using APM.StaffZen.Blazor.Components.Pages.TimeOff;

namespace APM.StaffZen.Blazor.Services
{
    public class HolidayService
    {
        private readonly IHttpClientFactory _factory;

        public HolidayService(IHttpClientFactory factory) => _factory = factory;

        public async Task<List<HolidayCalendarPublic>> GetAllAsync()
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var resp     = await client.GetAsync("api/HolidayCalendars");
                if (!resp.IsSuccessStatusCode) return new();
                var raw = await resp.Content.ReadFromJsonAsync<List<HolidayCalendarRaw>>();
                if (raw == null) return new();

                return raw.Select(r => new HolidayCalendarPublic
                {
                    Id        = r.Id,
                    Name      = r.Name,
                    IsDefault = r.IsDefault,
                    Holidays  = ParseHolidays(r.HolidaysJson)
                }).ToList();
            }
            catch { return new(); }
        }

        public async Task<HolidayCalendarPublic?> SaveAsync(int id, string name, string country,
            bool isDefault, List<HolidayEntryPublic> holidays)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var payload = new
                {
                    Id           = id,
                    Name         = name,
                    Country      = country,
                    IsDefault    = isDefault,
                    HolidaysJson = JsonSerializer.Serialize(
                        holidays.Select(h => new { name = h.Name, date = h.Date.ToString("yyyy-MM-dd") }))
                };

                HttpResponseMessage resp;
                if (id == 0)
                    resp = await client.PostAsJsonAsync("api/HolidayCalendars", payload);
                else
                    resp = await client.PutAsJsonAsync($"api/HolidayCalendars/{id}", payload);

                if (!resp.IsSuccessStatusCode) return null;

                var raw = await resp.Content.ReadFromJsonAsync<HolidayCalendarRaw>();
                if (raw == null) return null;

                return new HolidayCalendarPublic
                {
                    Id        = raw.Id,
                    Name      = raw.Name,
                    IsDefault = raw.IsDefault,
                    Holidays  = ParseHolidays(raw.HolidaysJson)
                };
            }
            catch { return null; }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var resp   = await client.DeleteAsync($"api/HolidayCalendars/{id}");
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // Load from API and sync into HolidaysTabState so both pages share the same data
        public async Task SyncToStateAsync()
        {
            var list = await GetAllAsync();
            HolidaysTabState.SharedCalendars = list;
        }

        private static List<HolidayEntryPublic> ParseHolidays(string json)
        {
            try
            {
                var elements = JsonSerializer.Deserialize<List<JsonElement>>(json ?? "[]");
                if (elements == null) return new();
                return elements.Select(e => new HolidayEntryPublic
                {
                    Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Date = e.TryGetProperty("date", out var d) && DateTime.TryParse(d.GetString(), out var dt)
                           ? dt : DateTime.MinValue
                }).Where(h => h.Date != DateTime.MinValue).ToList();
            }
            catch { return new(); }
        }

        private class HolidayCalendarRaw
        {
            public int    Id           { get; set; }
            public string Name         { get; set; } = "";
            public string Country      { get; set; } = "";
            public bool   IsDefault    { get; set; }
            public string HolidaysJson { get; set; } = "[]";
        }
    }
}
