using System.Net.Http.Json;

namespace APM.StaffZen.Blazor.Services
{
    public class TimeTrackingPolicyVm
    {
        public int Id             { get; set; }
        public int OrganizationId { get; set; }

        public bool DevMobile   { get; set; } = true;
        public bool DevKiosk    { get; set; } = true;
        public bool DevWeb      { get; set; } = true;
        public bool DevDesktop  { get; set; } = false;
        public bool DevOffline  { get; set; } = true;

        public bool RequireFaceRecognition { get; set; } = false;
        public bool RequireSelfie          { get; set; } = false;

        public bool EnableLiveLocation    { get; set; } = false;
        public bool RequireLocation       { get; set; } = false;
        public bool EnableGeofencing      { get; set; } = false;

        public bool AutoClockInOnEntry    { get; set; } = false;
        public bool AutoClockOutOnExit    { get; set; } = false;
        public bool ExcludeRestDays       { get; set; } = false;
        public bool ExcludeHolidays       { get; set; } = false;
        public bool ExcludeTimeOffs       { get; set; } = false;

        public bool RequireActivity       { get; set; } = false;
        public bool RequireProject        { get; set; } = false;
        public bool AllowEditEntries      { get; set; } = true;
        public bool AllowEditLocation     { get; set; } = true;

        public bool RequireScreenshot     { get; set; } = false;
        public bool DeviceLockEnabled     { get; set; } = false;

        public bool EarlyClockIn         { get; set; } = false;
        public int  EarlyClockInMins     { get; set; } = 5;
        public bool LateClockIn          { get; set; } = false;
        public int  LateClockInMins      { get; set; } = 5;
        public bool EarlyClockOut        { get; set; } = false;
        public int  EarlyClockOutMins    { get; set; } = 5;

        public bool ClockInReminder      { get; set; } = false;
        public int  ClockInReminderMins  { get; set; } = 5;
        public bool ClockOutReminder     { get; set; } = false;
        public int  ClockOutReminderMins { get; set; } = 5;

        public bool   AutoClockOutEnabled         { get; set; } = false;
        public bool   AutoClockOutAfterDuration   { get; set; } = false;
        public int    AutoClockOutAfterHours      { get; set; } = 8;
        public int    AutoClockOutAfterMins       { get; set; } = 0;
        public bool   AutoClockOutAtTime          { get; set; } = false;
        public string AutoClockOutTime            { get; set; } = "23:00";
    }

    public class TimeTrackingPolicyService
    {
        private readonly IHttpClientFactory _factory;
        private HttpClient Client => _factory.CreateClient("API");

        public TimeTrackingPolicyService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<TimeTrackingPolicyVm> GetAsync(int orgId)
        {
            try
            {
                var result = await Client.GetFromJsonAsync<TimeTrackingPolicyVm>(
                    $"api/organizations/{orgId}/time-tracking-policy");
                return result ?? new TimeTrackingPolicyVm { OrganizationId = orgId };
            }
            catch
            {
                return new TimeTrackingPolicyVm { OrganizationId = orgId };
            }
        }

        public async Task<(bool success, string? error)> SaveAsync(int orgId, TimeTrackingPolicyVm vm)
        {
            try
            {
                vm.OrganizationId = orgId;
                var resp = await Client.PutAsJsonAsync(
                    $"api/organizations/{orgId}/time-tracking-policy", vm);
                if (resp.IsSuccessStatusCode) return (true, null);
                return (false, await resp.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
