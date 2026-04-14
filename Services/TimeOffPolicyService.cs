using System.Net.Http.Json;
using System.Text.Json;

namespace APM.StaffZen.Blazor.Services
{
    // ── View models used by the Blazor UI ────────────────────────────────────

    public class TimeOffPolicyVm
    {
        public int     Id                       { get; set; }
        public string  Name                     { get; set; } = "";
        public string  CompensationType         { get; set; } = "Paid";
        public string  Unit                     { get; set; } = "Days";
        public string  AccrualType              { get; set; } = "None";
        public double  AnnualEntitlement        { get; set; }
        public bool    ExcludePublicHolidays    { get; set; }
        public bool    ExcludeNonWorkingDays    { get; set; }
        public bool    AllowCarryForward        { get; set; }
        public double? CarryForwardLimit        { get; set; }
        public int?    CarryForwardExpiryMonths { get; set; }
        public bool    IsActive                 { get; set; }
        public DateTime CreatedAt               { get; set; }
        public string  AssigneeSummary          { get; set; } = "All Employees";
        public List<int> AssignedEmployeeIds    { get; set; } = new();
        public List<int> AssignedGroupIds       { get; set; } = new();
    }

    public class SaveTimeOffPolicyRequest
    {
        public string  Name                     { get; set; } = "";
        public string  CompensationType         { get; set; } = "Paid";
        public string  Unit                     { get; set; } = "Days";
        public string  AccrualType              { get; set; } = "None";
        public double  AnnualEntitlement        { get; set; } = 0;
        public bool    ExcludePublicHolidays    { get; set; } = false;
        public bool    ExcludeNonWorkingDays    { get; set; } = false;
        public bool    AllowCarryForward        { get; set; } = false;
        public double? CarryForwardLimit        { get; set; }
        public int?    CarryForwardExpiryMonths { get; set; }
        public List<int> AssignedEmployeeIds    { get; set; } = new();
        public List<int> AssignedGroupIds       { get; set; } = new();
    }

    // ── Service ───────────────────────────────────────────────────────────────

    public class TimeOffPolicyService
    {
        private readonly IHttpClientFactory _factory;
        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

        public TimeOffPolicyService(IHttpClientFactory factory) => _factory = factory;

        private HttpClient Api => _factory.CreateClient("API");

        public async Task<List<TimeOffPolicyVm>> GetAllAsync()
        {
            try
            {
                var res = await Api.GetAsync("api/time-off-policies");
                if (!res.IsSuccessStatusCode) return new();
                return JsonSerializer.Deserialize<List<TimeOffPolicyVm>>(
                    await res.Content.ReadAsStringAsync(), _json) ?? new();
            }
            catch { return new(); }
        }

        /// <returns>(success, errorMessage)</returns>
        public async Task<(bool Ok, string? Error)> CreateAsync(SaveTimeOffPolicyRequest req)
        {
            try
            {
                var res = await Api.PostAsJsonAsync("api/time-off-policies", req);
                return res.IsSuccessStatusCode ? (true, null) : (false, await ReadError(res));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        /// <returns>(success, errorMessage)</returns>
        public async Task<(bool Ok, string? Error)> UpdateAsync(int id, SaveTimeOffPolicyRequest req)
        {
            try
            {
                var res = await Api.PutAsJsonAsync($"api/time-off-policies/{id}", req);
                return res.IsSuccessStatusCode ? (true, null) : (false, await ReadError(res));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        /// <returns>(success, errorMessage)</returns>
        public async Task<(bool Ok, string? Error)> DeleteAsync(int id)
        {
            try
            {
                var res = await Api.DeleteAsync($"api/time-off-policies/{id}");
                return res.IsSuccessStatusCode ? (true, null) : (false, await ReadError(res));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        private static async Task<string> ReadError(HttpResponseMessage res)
        {
            try
            {
                var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.TryGetProperty("message", out var m)
                    ? m.GetString() ?? "Unknown error." : "Unknown error.";
            }
            catch { return "Unknown error."; }
        }
    }
}
