using System.Text.Json;

namespace APM.StaffZen.Blazor.Services
{
    public class NotificationSettingsService
    {
        private readonly IHttpClientFactory _factory;

        public NotificationSettingsService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<NotificationSettingsVm?> GetAsync(int employeeId)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.GetAsync($"api/employees/{employeeId}/notification-settings");
                if (!response.IsSuccessStatusCode) return null;
                var body = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<NotificationSettingsVm>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return null; }
        }

        public async Task<(bool success, string? error)> SaveAsync(int employeeId, NotificationSettingsVm vm)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.PutAsJsonAsync(
                    $"api/employees/{employeeId}/notification-settings", vm);
                if (response.IsSuccessStatusCode) return (true, null);
                var body = await response.Content.ReadAsStringAsync();
                try
                {
                    var doc = JsonDocument.Parse(body).RootElement;
                    var msg = doc.TryGetProperty("error", out var e) ? e.GetString() : body;
                    return (false, msg);
                }
                catch { return (false, body); }
            }
            catch (Exception ex) { return (false, ex.Message); }
        }
    }

    public class NotificationSettingsVm
    {
        // Channels
        public bool ReportsChannelEmail      { get; set; } = true;
        public bool ReportsChannelWhatsApp   { get; set; } = false;
        public bool ReportsChannelSms        { get; set; } = false;
        public bool ReportsChannelPush       { get; set; } = false;
        public bool RemindersChannelEmail    { get; set; } = true;
        public bool RemindersChannelWhatsApp { get; set; } = false;
        public bool RemindersChannelSms      { get; set; } = false;
        public bool RemindersChannelPush     { get; set; } = false;

        // Reports
        public bool   NotifDailyAttendance { get; set; } = false;
        public string DailyAttendanceTime  { get; set; } = "9:00 am";
        public string DailyAttendanceFreq  { get; set; } = "everyday";
        public bool   NotifWeeklyActivity  { get; set; } = true;
        public string WeeklyActivityDay    { get; set; } = "Monday";

        // Reminders
        public bool NotifClockIn    { get; set; } = false;
        public int  ClockInMinutes  { get; set; } = 5;
        public bool NotifClockOut   { get; set; } = false;
        public int  ClockOutMinutes { get; set; } = 5;
        public bool NotifEndBreak   { get; set; } = false;
        public int  EndBreakMinutes { get; set; } = 5;

        // Alerts
        public bool NotifTimeClockStarts { get; set; } = false;
        public bool NotifTimeOffRequests { get; set; } = true;

        // Subscriptions
        public bool SubProductUpdates { get; set; } = false;
        public bool SubPromotions     { get; set; } = false;
        public bool SubUsageTracking  { get; set; } = false;

        public NotificationSettingsVm Clone() => (NotificationSettingsVm)MemberwiseClone();
    }
}
