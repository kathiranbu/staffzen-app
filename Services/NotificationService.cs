using System.Net.Http.Json;
using System.Text.Json;

namespace APM.StaffZen.Blazor.Services
{
    /// <summary>
    /// Blazor-side service for persistent, role-aware leave notifications.
    ///
    /// Responsibilities:
    ///   • Load notifications from api/notifications?recipientId=N
    ///   • Mark a single notification as read (PATCH /{id}/read)
    ///   • Mark all as read
    ///   • Delete/dismiss one or all
    ///   • Fetch the single LeaveRequest linked to a notification (GET api/leave-requests/{id})
    ///   • Post an approve/reject review (PATCH api/leave-requests/{id}/review)
    /// </summary>
    public class NotificationService
    {
        private readonly IHttpClientFactory _factory;
        private readonly SessionService     _session;

        public NotificationService(IHttpClientFactory factory, SessionService session)
        {
            _factory = factory;
            _session = session;
        }

        private HttpClient Api => _factory.CreateClient("API");

        // ── Fetch notifications for the current user ──────────────────────────
        public async Task<List<NotificationVm>> GetMyNotificationsAsync()
        {
            try
            {
                var res = await Api.GetAsync($"api/notifications?recipientId={_session.Id}");
                if (!res.IsSuccessStatusCode) return new();

                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var arr  = JsonSerializer.Deserialize<JsonElement[]>(
                               await res.Content.ReadAsStringAsync(), opts);

                return arr?.Select(Map).ToList() ?? new();
            }
            catch { return new(); }
        }

        // ── Mark one notification read ────────────────────────────────────────
        public async Task MarkReadAsync(int notificationId)
        {
            try
            {
                await Api.PatchAsync($"api/notifications/{notificationId}/read",
                                     new StringContent(""));
            }
            catch { /* best-effort */ }
        }

        // ── Mark all read ─────────────────────────────────────────────────────
        public async Task MarkAllReadAsync()
        {
            try
            {
                await Api.PatchAsync(
                    $"api/notifications/mark-all-read?recipientId={_session.Id}",
                    new StringContent(""));
            }
            catch { /* best-effort */ }
        }

        // ── Dismiss one ───────────────────────────────────────────────────────
        public async Task DismissAsync(int notificationId)
        {
            try { await Api.DeleteAsync($"api/notifications/{notificationId}"); }
            catch { /* best-effort */ }
        }

        // ── Clear all for current user ────────────────────────────────────────
        public async Task ClearAllAsync()
        {
            try { await Api.DeleteAsync($"api/notifications?recipientId={_session.Id}"); }
            catch { /* best-effort */ }
        }

        // ── Fetch a single leave request by ID ───────────────────────────────
        public async Task<LeaveDetailVm?> GetLeaveRequestAsync(int leaveRequestId)
        {
            try
            {
                var res = await Api.GetAsync($"api/leave-requests/{leaveRequestId}");
                if (!res.IsSuccessStatusCode) return null;

                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var e    = JsonSerializer.Deserialize<JsonElement>(
                               await res.Content.ReadAsStringAsync(), opts);

                return MapLeave(e);
            }
            catch { return null; }
        }

        // ── Approve or reject a leave request (admin action from notification) ─
        public async Task<(bool ok, string error)> ReviewLeaveAsync(
            int    leaveRequestId,
            string action,          // "approve" | "direct_approve" | "reject"
            string reviewedBy,      // "TeamLead" | "HR"
            string? rejectReason = null)
        {
            try
            {
                var payload = new { action, reviewedBy, rejectReason };
                var res = await Api.PatchAsJsonAsync(
                    $"api/leave-requests/{leaveRequestId}/review", payload);

                if (res.IsSuccessStatusCode) return (true, "");

                var msg = await ExtractMessage(res);
                return (false, msg);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // ── Save a clock event notification to the DB ─────────────────────────
        public async Task SaveClockEventAsync(string type, string title, string message, int referenceId = 0)
        {
            try
            {
                var dto = new
                {
                    recipientId = _session.Id,
                    type,
                    title,
                    message,
                    referenceId
                };
                await Api.PostAsJsonAsync("api/notifications/clock-event", dto);
            }
            catch { /* best-effort — toast still shows even if DB save fails */ }
        }

        // ── Mappers ───────────────────────────────────────────────────────────
        private static NotificationVm Map(JsonElement e) => new()
        {
            Id          = e.GetProperty("id").GetInt32(),
            RecipientId = e.GetProperty("recipientId").GetInt32(),
            Type        = e.GetProperty("type").GetString() ?? "",
            Title       = e.GetProperty("title").GetString() ?? "",
            Message     = e.GetProperty("message").GetString() ?? "",
            ReferenceId = e.GetProperty("referenceId").GetInt32(),
            IsRead      = e.GetProperty("isRead").GetBoolean(),
            CreatedAt   = DateTime.Parse(e.GetProperty("createdAt").GetString()
                            ?? DateTime.UtcNow.ToString("o"))
        };

        private static LeaveDetailVm MapLeave(JsonElement e) => new()
        {
            Id           = e.GetProperty("id").GetInt32(),
            EmployeeId   = e.GetProperty("employeeId").GetInt32(),
            EmployeeName = e.TryGetProperty("employeeName", out var en)
                               ? en.GetString() ?? "" : "",
            StartDate    = DateTime.Parse(e.GetProperty("startDate").GetString()
                               ?? DateTime.Today.ToString("yyyy-MM-dd")),
            EndDate      = e.TryGetProperty("endDate", out var ed)
                           && ed.ValueKind != JsonValueKind.Null
                               ? DateTime.Parse(ed.GetString()!) : null,
            Reason       = e.TryGetProperty("reason",       out var rn)
                           && rn.ValueKind != JsonValueKind.Null
                               ? rn.GetString() : null,
            Status       = e.GetProperty("status").GetString() ?? "",
            ReviewedBy   = e.TryGetProperty("reviewedBy",   out var rb)
                           && rb.ValueKind != JsonValueKind.Null
                               ? rb.GetString() : null,
            RejectReason = e.TryGetProperty("rejectReason", out var rr)
                           && rr.ValueKind != JsonValueKind.Null
                               ? rr.GetString() : null
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

    // ── View-models ───────────────────────────────────────────────────────────

    public class NotificationVm
    {
        public int      Id          { get; set; }
        public int      RecipientId { get; set; }

        /// <summary>LeaveApplied | LeaveApproved | LeaveRejected</summary>
        public string   Type        { get; set; } = "";

        public string   Title       { get; set; } = "";
        public string   Message     { get; set; } = "";

        /// <summary>LeaveRequest.Id</summary>
        public int      ReferenceId { get; set; }

        public bool     IsRead      { get; set; }
        public DateTime CreatedAt   { get; set; }

        public string TimeAgo
        {
            get
            {
                var d = DateTime.UtcNow - CreatedAt;
                if (d.TotalMinutes < 1)  return "Just now";
                if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
                if (d.TotalHours   < 24) return $"{(int)d.TotalHours}h ago";
                return $"{(int)d.TotalDays}d ago";
            }
        }

        public string Icon => Type switch
        {
            "LeaveApplied"  => "bi-person-lines-fill",
            "LeaveApproved" => "bi-check-circle-fill",
            "LeaveRejected" => "bi-x-circle-fill",
            "ClockIn"       => "bi-box-arrow-in-right",
            "ClockOut"      => "bi-box-arrow-right",
            "BreakStart"    => "bi-cup-hot",
            "BreakEnd"      => "bi-cup-hot",
            "ShiftAlert"    => "bi-alarm",
            _               => "bi-bell-fill"
        };

        public string IconBgCss => Type switch
        {
            "LeaveApplied"  => "notif-icon--applied",
            "LeaveApproved" => "notif-icon--approved",
            "LeaveRejected" => "notif-icon--rejected",
            "ClockIn"       => "notif-icon--clockin",
            "ClockOut"      => "notif-icon--clockout",
            "BreakStart"    => "notif-icon--break",
            "BreakEnd"      => "notif-icon--break",
            "ShiftAlert"    => "notif-icon--shift",
            _               => ""
        };
    }

    public class LeaveDetailVm
    {
        public int       Id           { get; set; }
        public int       EmployeeId   { get; set; }
        public string    EmployeeName { get; set; } = "";
        public DateTime  StartDate    { get; set; }
        public DateTime? EndDate      { get; set; }
        public string?   Reason       { get; set; }
        public string    Status       { get; set; } = "";
        public string?   ReviewedBy   { get; set; }
        public string?   RejectReason { get; set; }

        public bool IsApproved => Status == "Approved";
        public bool IsRejected => Status.StartsWith("Rejected");
        public bool IsPending  => Status.StartsWith("Pending");

        public string DisplayStatus => Status switch
        {
            "Pending_TeamLead"  => "Pending with Team Lead",
            "Pending_HR"        => "Pending with HR",
            "Approved"          => "Approved",
            "Rejected_TeamLead" => "Rejected by Team Lead",
            "Rejected_HR"       => "Rejected by HR",
            _                   => Status
        };
    }
}
