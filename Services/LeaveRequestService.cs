using System.Net.Http.Json;
using System.Text.Json;

namespace APM.StaffZen.Blazor.Services
{
    /// <summary>
    /// Calls api/leave-requests on behalf of the logged-in user.
    /// </summary>
    public class LeaveRequestService
    {
        private readonly IHttpClientFactory _factory;
        private readonly SessionService     _session;

        public LeaveRequestService(IHttpClientFactory factory, SessionService session)
        {
            _factory = factory;
            _session = session;
        }

        private HttpClient Api => _factory.CreateClient("API");

        // ── GET my leave requests ─────────────────────────────────────────────
        public async Task<List<LeaveRequestVm>> GetMyRequestsAsync()
        {
            try
            {
                var res = await Api.GetAsync(
                    $"api/leave-requests?employeeId={_session.Id}");

                if (!res.IsSuccessStatusCode) return new();

                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var arr  = JsonSerializer.Deserialize<JsonElement[]>(
                               await res.Content.ReadAsStringAsync(), opts);

                return arr?.Select(Map).ToList() ?? new();
            }
            catch { return new(); }
        }

        // ── POST submit a new leave request ──────────────────────────────────
        public async Task<(bool ok, string error)> SubmitAsync(
            string startDate,
            string endDate,
            string? reason,
            int?    leaveTypeId   = null,
            string? leaveTypeName = null)
        {
            try
            {
                var payload = new
                {
                    employeeId    = _session.Id,
                    leaveTypeId,
                    leaveTypeName,
                    startDate,
                    endDate,
                    reason
                };

                var res = await Api.PostAsJsonAsync("api/leave-requests", payload);
                if (res.IsSuccessStatusCode) return (true, "");

                var msg = await ExtractMessage(res);
                return (false, msg);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ── DELETE cancel / delete a request ─────────────────────────────────
        public async Task<(bool ok, string error)> DeleteAsync(int id)
        {
            try
            {
                var res = await Api.DeleteAsync($"api/leave-requests/{id}");
                if (res.IsSuccessStatusCode) return (true, "");

                return (false, await ExtractMessage(res));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static LeaveRequestVm Map(JsonElement e)
        {
            var status = e.GetProperty("status").GetString() ?? "Pending_TeamLead";

            return new LeaveRequestVm
            {
                Id           = e.GetProperty("id").GetInt32(),
                EmployeeId   = e.GetProperty("employeeId").GetInt32(),
                EmployeeName = e.TryGetProperty("employeeName", out var en)
                                   ? en.GetString() ?? "" : "",
                LeaveTypeId  = e.TryGetProperty("leaveTypeId", out var lti)
                               && lti.ValueKind != JsonValueKind.Null
                                   ? lti.GetInt32() : null,
                LeaveTypeName = e.TryGetProperty("leaveTypeName", out var ltn)
                               && ltn.ValueKind != JsonValueKind.Null
                                   ? ltn.GetString() ?? "" : "",
                StartDate    = DateTime.Parse(
                                   e.GetProperty("startDate").GetString()
                                   ?? DateTime.Today.ToString("yyyy-MM-dd")),
                EndDate      = e.TryGetProperty("endDate", out var ed)
                               && ed.ValueKind != JsonValueKind.Null
                                   ? DateTime.Parse(ed.GetString()!) : null,
                Reason       = e.TryGetProperty("reason", out var rn)
                               && rn.ValueKind != JsonValueKind.Null
                                   ? rn.GetString() : null,
                Status       = status,
                DisplayStatus = ToDisplayStatus(status),
                RejectReason = e.TryGetProperty("rejectReason", out var rr)
                               && rr.ValueKind != JsonValueKind.Null
                                   ? rr.GetString() : null,
                ReviewedBy   = e.TryGetProperty("reviewedBy", out var rb)
                               && rb.ValueKind != JsonValueKind.Null
                                   ? rb.GetString() : null,
                CreatedAt    = e.TryGetProperty("createdAt", out var ca)
                               && ca.ValueKind != JsonValueKind.Null
                                   ? DateTime.Parse(ca.GetString()!) : DateTime.MinValue
            };
        }

        /// <summary>
        /// Maps the raw API status string to the badge label shown to the user.
        ///   Pending_TeamLead  → Pending with Team Lead
        ///   Pending_HR        → Pending with HR
        ///   Pending           → Pending
        ///   Approved          → Approved
        ///   Rejected_TeamLead → Rejected by Team Lead
        ///   Rejected_HR       → Rejected by HR
        ///   Rejected          → Rejected
        /// </summary>
        public static string ToDisplayStatus(string status) => status switch
        {
            "Pending_TeamLead"  => "Pending with Team Lead",
            "Pending_HR"        => "Pending with HR",
            "Pending"           => "Pending",
            "Approved"          => "Approved",
            "Rejected_TeamLead" => "Rejected by Team Lead",
            "Rejected_HR"       => "Rejected by HR",
            "Rejected"          => "Rejected",
            _                   => "In Progress"
        };

        private static async Task<string> ExtractMessage(HttpResponseMessage res)
        {
            try
            {
                var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.TryGetProperty("message", out var m)
                    ? m.GetString() ?? "An error occurred."
                    : "An error occurred.";
            }
            catch { return "An error occurred."; }
        }
    }

    // ── View-model returned to the UI ─────────────────────────────────────────
    public class LeaveRequestVm
    {
        public int       Id            { get; set; }
        public int       EmployeeId    { get; set; }
        public string    EmployeeName  { get; set; } = "";
        public int?      LeaveTypeId   { get; set; }
        public string    LeaveTypeName { get; set; } = "";
        public DateTime  StartDate     { get; set; }
        public DateTime? EndDate       { get; set; }
        public string?   Reason        { get; set; }

        /// <summary>Raw status from the API: Pending_TeamLead | Pending_HR | Pending | Approved | Rejected | Rejected_TeamLead | Rejected_HR</summary>
        public string    Status        { get; set; } = "Pending_TeamLead";

        /// <summary>User-facing badge label: "In Progress" | "Approved" | "Rejected" etc.</summary>
        public string    DisplayStatus { get; set; } = "In Progress";

        public string?   RejectReason  { get; set; }
        public string?   ReviewedBy    { get; set; }
        public DateTime  CreatedAt     { get; set; }

        /// <summary>CSS badge modifier class based on DisplayStatus.</summary>
        public string BadgeCss => DisplayStatus switch
        {
            "Approved"               => "lr-badge--approved",
            "Rejected"               => "lr-badge--rejected",
            "Rejected by Team Lead"  => "lr-badge--rejected",
            "Rejected by HR"         => "lr-badge--rejected",
            "Pending"                => "lr-badge--pending",
            "Pending with Team Lead" => "lr-badge--pending",
            "Pending with HR"        => "lr-badge--pending",
            _                        => "lr-badge--inprogress"
        };

        public string DateRangeLabel
        {
            get
            {
                var end = EndDate ?? StartDate;
                return StartDate.Date == end.Date
                    ? StartDate.ToString("MMM dd, yyyy")
                    : $"{StartDate:MMM dd, yyyy} → {end:MMM dd, yyyy}";
            }
        }
    }
}
