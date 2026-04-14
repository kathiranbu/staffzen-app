using System.Net.Http.Json;
using System.Text.Json;

namespace APM.StaffZen.Blazor.Services
{
    // ── View models ───────────────────────────────────────────────────────

    public class CorrectionRequestVm
    {
        public int       Id                { get; set; }
        public int       EmployeeId        { get; set; }
        public int       TimeEntryId       { get; set; }
        public DateTime  AttendanceDate    { get; set; }
        public DateTime  RequestedClockOut { get; set; }
        public string?   Reason            { get; set; }
        public string    Status            { get; set; } = "";
        public DateTime? ApprovedClockOut  { get; set; }
        public string?   AdminNote         { get; set; }
        public DateTime  CreatedAt         { get; set; }
    }

    public class AdminCorrectionVm
    {
        public int       Id                { get; set; }
        public int       EmployeeId        { get; set; }
        public string    EmployeeName      { get; set; } = "";
        public string    EmployeeEmail     { get; set; } = "";
        public string?   ProfileImageUrl   { get; set; }
        public string?   DepartmentName    { get; set; }
        public int       TimeEntryId       { get; set; }
        public DateTime  AttendanceDate    { get; set; }
        public DateTime? ClockIn           { get; set; }
        public DateTime  RequestedClockOut { get; set; }
        public string?   Reason            { get; set; }
        public string    Status            { get; set; } = "";
        public string?   AdminNote         { get; set; }
        public DateTime? ReviewedAt        { get; set; }
        public DateTime  CreatedAt         { get; set; }
    }

    public class DashboardSummaryVm
    {
        public int Present  { get; set; }
        public int Late     { get; set; }
        public int Absent   { get; set; }
        public int Unmarked { get; set; }
    }

    public class DashboardRowVm
    {
        public int       EmployeeId        { get; set; }
        public string    EmployeeName      { get; set; } = "";
        public string    Email             { get; set; } = "";
        public string?   ProfileImageUrl   { get; set; }
        public string?   MemberCode        { get; set; }
        public string?   DepartmentName    { get; set; }
        public string    AttendanceStatus  { get; set; } = "Absent";
        public DateTime? ClockIn           { get; set; }
        public DateTime? ClockOut          { get; set; }
        public string?   WorkedHours       { get; set; }
        public bool      HasPendingRequest { get; set; }
    }

    public class AttendanceDashboardVm
    {
        public string           Date         { get; set; } = "";
        public int              TotalMembers { get; set; }
        public DashboardSummaryVm Summary    { get; set; } = new();
        public List<DashboardRowVm> Employees { get; set; } = new();
    }

    // ── Service ───────────────────────────────────────────────────────────

    public class AttendanceCorrectionService
    {
        private readonly IHttpClientFactory _factory;
        private HttpClient Client => _factory.CreateClient("API");

        public AttendanceCorrectionService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        // ── Employee: submit correction request ───────────────────────────
        public async Task<(bool success, string? error, int? requestId)> SubmitAsync(
            int employeeId, int timeEntryId, string requestedClockOutTime, string? reason)
        {
            try
            {
                var payload = new
                {
                    employeeId,
                    timeEntryId,
                    requestedClockOutTime,
                    reason
                };
                var resp = await Client.PostAsJsonAsync("api/attendance-corrections", payload);
                var body = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode)
                {
                    var doc = JsonDocument.Parse(body).RootElement;
                    return (true, null, doc.GetProperty("requestId").GetInt32());
                }
                var err = TryGetError(body);
                return (false, err, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }

        // ── Employee: get own correction requests ─────────────────────────
        public async Task<List<CorrectionRequestVm>> GetForEmployeeAsync(int employeeId, int? orgId = null)
        {
            try
            {
                var url = $"api/attendance-corrections/employee/{employeeId}";
                if (orgId.HasValue && orgId.Value > 0) url += $"?organizationId={orgId.Value}";
                return await Client.GetFromJsonAsync<List<CorrectionRequestVm>>(url) ?? new();
            }
            catch { return new(); }
        }

        // ── Admin: get pending correction requests ────────────────────────
        public async Task<List<AdminCorrectionVm>> GetPendingAsync(int orgId)
        {
            try
            {
                return await Client.GetFromJsonAsync<List<AdminCorrectionVm>>(
                    $"api/attendance-corrections/pending?organizationId={orgId}") ?? new();
            }
            catch { return new(); }
        }

        // ── Admin: get all correction requests (with optional status filter) ─
        public async Task<List<AdminCorrectionVm>> GetAllAsync(int orgId, string? status = null)
        {
            try
            {
                var url = $"api/attendance-corrections/all?organizationId={orgId}";
                if (!string.IsNullOrWhiteSpace(status)) url += $"&status={status}";
                return await Client.GetFromJsonAsync<List<AdminCorrectionVm>>(url) ?? new();
            }
            catch { return new(); }
        }

        // ── Admin: approve ────────────────────────────────────────────────
        /// <param name="markAsAbsent">When true the entry is closed as Absent; overrideTime is ignored.</param>
        /// <param name="forceStatus">When set ("Present" or "Late"), overrides auto-calculated status.</param>
        public async Task<(bool success, string? error)> ApproveAsync(
            int requestId, int reviewedByEmpId,
            string? overrideTime  = null,
            string? adminNote     = null,
            bool    markAsAbsent  = false,
            string? forceStatus   = null)
        {
            try
            {
                var payload = new
                {
                    reviewedByEmployeeId = reviewedByEmpId,
                    overrideClockOutTime = overrideTime,
                    adminNote,
                    markAsAbsent,
                    forceStatus
                };
                var resp = await Client.PutAsJsonAsync($"api/attendance-corrections/{requestId}/approve", payload);
                if (resp.IsSuccessStatusCode) return (true, null);
                return (false, TryGetError(await resp.Content.ReadAsStringAsync()));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // ── Admin: reject ─────────────────────────────────────────────────
        public async Task<(bool success, string? error)> RejectAsync(
            int requestId, int reviewedByEmpId, string? adminNote = null)
        {
            try
            {
                var payload = new { reviewedByEmployeeId = reviewedByEmpId, adminNote };
                var resp = await Client.PutAsJsonAsync($"api/attendance-corrections/{requestId}/reject", payload);
                if (resp.IsSuccessStatusCode) return (true, null);
                return (false, TryGetError(await resp.Content.ReadAsStringAsync()));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // ── Admin: attendance dashboard ───────────────────────────────────
        public async Task<AttendanceDashboardVm?> GetDashboardAsync(int orgId, DateTime date, string? statusFilter = null)
        {
            try
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                var url = $"api/attendance-corrections/dashboard?organizationId={orgId}&date={dateStr}";
                if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "All")
                    url += $"&statusFilter={statusFilter}";
                return await Client.GetFromJsonAsync<AttendanceDashboardVm>(url);
            }
            catch { return null; }
        }

        private static string TryGetError(string body)
        {
            try
            {
                var doc = JsonDocument.Parse(body).RootElement;
                if (doc.TryGetProperty("error", out var e)) return e.GetString() ?? body;
                if (doc.TryGetProperty("message", out var m)) return m.GetString() ?? body;
            }
            catch { }
            return body;
        }
    }
}
