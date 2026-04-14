using System.Text.Json;

namespace APM.StaffZen.Blazor.Services
{
    public class AttendanceService
    {
        private readonly IHttpClientFactory _factory;

        public AttendanceService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<AttendanceStatus> GetStatusAsync(int employeeId, int? organizationId = null)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var url      = $"api/Attendance/status/{employeeId}";
                if (organizationId.HasValue && organizationId.Value > 0)
                    url += $"?organizationId={organizationId.Value}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new AttendanceStatus();
                var doc    = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                var status = new AttendanceStatus { IsClockedIn = doc.GetProperty("isClockedIn").GetBoolean() };
                if (status.IsClockedIn)
                {
                    status.EntryId = doc.GetProperty("entryId").GetInt32();
                    status.ClockIn = doc.GetProperty("clockIn").GetDateTime();
                }
                return status;
            }
            catch { return new AttendanceStatus(); }
        }

        public async Task<List<TimeEntryRecord>> GetHistoryAsync(int employeeId, int? organizationId = null)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var url      = $"api/Attendance/history/{employeeId}";
                if (organizationId.HasValue && organizationId.Value > 0)
                    url += $"?organizationId={organizationId.Value}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new();
                return ParseEntries(await response.Content.ReadAsStringAsync());
            }
            catch { return new(); }
        }

        public async Task<List<TimeEntryRecord>> GetHistoryByDateAsync(int employeeId, DateTime date, int? organizationId = null)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var dateStr  = date.ToString("yyyy-MM-dd");
                var url      = $"api/Attendance/history/{employeeId}/bydate?date={dateStr}";
                if (organizationId.HasValue && organizationId.Value > 0)
                    url += $"&organizationId={organizationId.Value}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new();
                return ParseEntries(await response.Content.ReadAsStringAsync());
            }
            catch { return new(); }
        }

        public async Task<List<TimeEntryRecord>> GetHistoryByWeekAsync(int employeeId, DateTime weekStart, int? organizationId = null)
        {
            try
            {
                var client      = _factory.CreateClient("API");
                var weekStartStr = weekStart.ToString("yyyy-MM-dd");
                var url         = $"api/Attendance/history/{employeeId}/week?weekStart={weekStartStr}";
                if (organizationId.HasValue && organizationId.Value > 0)
                    url += $"&organizationId={organizationId.Value}";
                var response    = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new();
                return ParseEntries(await response.Content.ReadAsStringAsync());
            }
            catch { return new(); }
        }

        private static List<TimeEntryRecord> ParseEntries(string json)
        {
            var arr  = JsonDocument.Parse(json).RootElement;
            var list = new List<TimeEntryRecord>();
            foreach (var item in arr.EnumerateArray())
            {
                list.Add(new TimeEntryRecord
                {
                    Id          = item.GetProperty("id").GetInt32(),
                    ClockIn     = item.GetProperty("clockIn").GetDateTime(),
                    ClockOut    = item.TryGetProperty("clockOut", out var co) && co.ValueKind != JsonValueKind.Null
                                    ? co.GetDateTime() : null,
                    WorkedHours = item.TryGetProperty("workedHours", out var wh) && wh.ValueKind != JsonValueKind.Null
                                    ? wh.GetString() : null,
                    IsManual    = item.TryGetProperty("isManual", out var im) && im.ValueKind == JsonValueKind.True,
                    IsHourEntry  = item.TryGetProperty("isHourEntry",  out var ih) && ih.ValueKind == JsonValueKind.True,
                    IsBreakEntry = item.TryGetProperty("isBreakEntry", out var ib) && ib.ValueKind == JsonValueKind.True,
                    ClockInSelfieUrl  = item.TryGetProperty("clockInSelfieUrl",  out var cis) && cis.ValueKind == JsonValueKind.String ? cis.GetString() : null,
                    ClockOutSelfieUrl = item.TryGetProperty("clockOutSelfieUrl", out var cos) && cos.ValueKind == JsonValueKind.String ? cos.GetString() : null,
                    IsOvernightCarryOver = item.TryGetProperty("isOvernightCarryOver", out var ocr) && ocr.ValueKind == JsonValueKind.True
                });
            }
            return list;
        }

        public async Task<List<GroupInfo>> GetGroupsAsync()
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.GetAsync("api/groups");
                if (!response.IsSuccessStatusCode) return new();
                var arr  = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                var list = new List<GroupInfo>();
                foreach (var item in arr.EnumerateArray())
                {
                    list.Add(new GroupInfo
                    {
                        Id   = item.GetProperty("id").GetInt32(),
                        Name = item.GetProperty("name").GetString() ?? ""
                    });
                }
                return list;
            }
            catch { return new(); }
        }

        public async Task<List<AllMember>> GetAllMembersAsync(int? organizationId = null)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var url = organizationId.HasValue && organizationId.Value > 0
                    ? $"api/Attendance/allmembers?organizationId={organizationId.Value}"
                    : "api/Attendance/allmembers";
                var response = await client.GetAsync(url);
                var arr  = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                var list = new List<AllMember>();
                foreach (var item in arr.EnumerateArray())
                {
                    list.Add(new AllMember
                    {
                        Id          = item.GetProperty("id").GetInt32(),
                        FullName    = item.GetProperty("fullName").GetString() ?? "",
                        Email       = item.GetProperty("email").GetString() ?? "",
                        GroupId     = item.TryGetProperty("groupId", out var gid) && gid.ValueKind != JsonValueKind.Null ? gid.GetInt32() : null,
                        GroupName   = item.TryGetProperty("groupName", out var gn)  && gn.ValueKind  != JsonValueKind.Null ? gn.GetString()  : null,
                        IsClockedIn = item.GetProperty("isClockedIn").GetBoolean(),
                        ClockIn     = item.TryGetProperty("clockIn", out var ci) && ci.ValueKind != JsonValueKind.Null ? ci.GetDateTime() : null
                    });
                }
                return list;
            }
            catch { return new(); }
        }

        /// <summary>Exposes the named API client for ad-hoc calls (e.g. time-off requests in ListData).</summary>
        public HttpClient GetHttpClient() => _factory.CreateClient("API");

        public async Task<List<DailyTimesheetRow>> GetDailyTimesheetAsync(DateTime date, int? organizationId = null)
        {
            try
            {
                var client  = _factory.CreateClient("API");
                var dateStr = date.ToString("yyyy-MM-dd");
                var url = $"api/Attendance/daily?date={dateStr}";
                if (organizationId.HasValue && organizationId.Value > 0)
                    url += $"&organizationId={organizationId.Value}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new();
                var arr  = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                var list = new List<DailyTimesheetRow>();
                foreach (var item in arr.EnumerateArray())
                {
                    list.Add(new DailyTimesheetRow
                    {
                        EmployeeId         = item.GetProperty("employeeId").GetInt32(),
                        FullName           = item.GetProperty("fullName").GetString() ?? "",
                        FirstIn            = item.TryGetProperty("firstIn",  out var fi) && fi.ValueKind != JsonValueKind.Null ? fi.GetDateTime() : null,
                        LastOut            = item.TryGetProperty("lastOut",  out var lo) && lo.ValueKind != JsonValueKind.Null ? lo.GetDateTime() : null,
                        IsOngoing          = item.GetProperty("isOngoing").GetBoolean(),
                        TrackedHours       = item.GetProperty("trackedHours").GetString() ?? "—",
                        RegularHours       = item.TryGetProperty("regularHours", out var rh) && rh.ValueKind != JsonValueKind.Null
                                               ? rh.GetString() ?? "—"
                                               : item.GetProperty("trackedHours").GetString() ?? "—",
                        OvertimeHours         = item.TryGetProperty("overtimeHours",         out var ot)  && ot.ValueKind  != JsonValueKind.Null ? ot.GetString()  : null,
                        DailyDoubleOTHours    = item.TryGetProperty("dailyDoubleOTHours",    out var ddo) && ddo.ValueKind != JsonValueKind.Null ? ddo.GetString() : null,
                        WeeklyOvertimeHours   = item.TryGetProperty("weeklyOvertimeHours",   out var wot) && wot.ValueKind != JsonValueKind.Null ? wot.GetString() : null,
                        RestDayOvertimeHours  = item.TryGetProperty("restDayOvertimeHours",  out var rdo) && rdo.ValueKind != JsonValueKind.Null ? rdo.GetString() : null,
                        PublicHolOvertimeHours= item.TryGetProperty("publicHolOvertimeHours",out var pho) && pho.ValueKind != JsonValueKind.Null ? pho.GetString() : null,
                        HasDailyOT            = item.TryGetProperty("hasDailyOT",      out var hdot)  && hdot.ValueKind  == JsonValueKind.True,
                        HasDailyDblOT         = item.TryGetProperty("hasDailyDblOT",   out var hddot) && hddot.ValueKind == JsonValueKind.True,
                        HasWeeklyOT           = item.TryGetProperty("hasWeeklyOT",     out var hwot)  && hwot.ValueKind  == JsonValueKind.True,
                        HasRestDayOT          = item.TryGetProperty("hasRestDayOT",    out var hrdo)  && hrdo.ValueKind  == JsonValueKind.True,
                        HasPublicHolOT        = item.TryGetProperty("hasPublicHolOT",  out var hpho)  && hpho.ValueKind  == JsonValueKind.True,
                        IsRestDay             = item.TryGetProperty("isRestDay",       out var ird)   && ird.ValueKind   == JsonValueKind.True,
                        IsPublicHoliday       = item.TryGetProperty("isPublicHoliday", out var iph)   && iph.ValueKind   == JsonValueKind.True,
                        HasEntries            = item.TryGetProperty("hasEntries",      out var he)    && he.ValueKind    == JsonValueKind.True,
                        TimeOffLabel          = item.TryGetProperty("timeOffLabel",    out var tol)   && tol.ValueKind   != JsonValueKind.Null ? tol.GetString() : null,
                    });
                }
                return list;
            }
            catch { return new(); }
        }

        public async Task<List<RangeTimesheetRow>> GetRangeTimesheetAsync(DateTime from, DateTime to, int? organizationId = null)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var f = from.ToString("yyyy-MM-dd");
                var t = to.ToString("yyyy-MM-dd");
                var url = $"api/Attendance/range?from={f}&to={t}";
                if (organizationId.HasValue && organizationId.Value > 0)
                    url += $"&organizationId={organizationId.Value}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new();
                var arr  = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                var list = new List<RangeTimesheetRow>();
                foreach (var item in arr.EnumerateArray())
                {
                    var totalMinsVal = item.GetProperty("totalMins").GetInt32();
                    var totalHoursVal = item.TryGetProperty("totalHours", out var th) && th.ValueKind != JsonValueKind.Null ? th.GetString() ?? "" : "";
                    var regularMinsVal = item.TryGetProperty("regularMins", out var rm) && rm.ValueKind != JsonValueKind.Null ? rm.GetInt32() : totalMinsVal;
                    var regularHoursVal = item.TryGetProperty("regularHours", out var rh) && rh.ValueKind != JsonValueKind.Null ? rh.GetString() ?? "" : totalHoursVal;
                    var row = new RangeTimesheetRow
                    {
                        EmployeeId   = item.GetProperty("employeeId").GetInt32(),
                        FullName     = item.GetProperty("fullName").GetString() ?? "",
                        TotalMins    = totalMinsVal,
                        TotalHours   = totalHoursVal,
                        RegularMins  = regularMinsVal,
                        RegularHours = regularHoursVal
                    };
                    if (item.TryGetProperty("days", out var days))
                        foreach (var d in days.EnumerateArray())
                            row.Days[d.GetProperty("date").GetString()!] = new DayEntry
                            {
                                FirstIn      = d.TryGetProperty("firstIn",  out var fi) && fi.ValueKind != JsonValueKind.Null ? fi.GetDateTime() : null,
                                LastOut      = d.TryGetProperty("lastOut",  out var lo) && lo.ValueKind != JsonValueKind.Null ? lo.GetDateTime() : null,
                                IsOngoing    = d.GetProperty("isOngoing").GetBoolean(),
                                TrackedMins  = d.GetProperty("trackedMins").GetInt32(),
                                TrackedHours = d.TryGetProperty("trackedHours", out var dth) && dth.ValueKind != JsonValueKind.Null ? dth.GetString() ?? "" : ""
                            };
                    list.Add(row);
                }
                return list;
            }
            catch { return new(); }
        }

        public async Task<List<GroupMember>> GetGroupMembersAsync(int employeeId)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.GetAsync($"api/Attendance/groupmembers/{employeeId}");
                if (!response.IsSuccessStatusCode) return new();
                var arr  = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                var list = new List<GroupMember>();
                foreach (var item in arr.EnumerateArray())
                {
                    list.Add(new GroupMember
                    {
                        Id          = item.GetProperty("id").GetInt32(),
                        FullName    = item.GetProperty("fullName").GetString() ?? "",
                        Email       = item.GetProperty("email").GetString() ?? "",
                        IsClockedIn = item.GetProperty("isClockedIn").GetBoolean()
                    });
                }
                return list;
            }
            catch { return new(); }
        }

        /// <summary>
        /// Returns (ClockResult, null) on success, or (null, errorMessage) on failure.
        /// The error message comes directly from the API so the UI can show the real reason.
        /// </summary>
        public async Task<(ClockResult? Result, string? Error)> ClockInAsync(int employeeId, string? selfieBase64 = null, int? organizationId = null)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Attendance/clockin",
                    new { EmployeeId = employeeId, OrganizationId = organizationId, SelfieBase64 = selfieBase64 });
                var body     = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    try
                    {
                        var errDoc = JsonDocument.Parse(body).RootElement;
                        var msg    = errDoc.TryGetProperty("error", out var e) ? e.GetString() : null;
                        return (null, msg ?? "Clock in failed. Please try again.");
                    }
                    catch { return (null, "Clock in failed. Please try again."); }
                }
                var doc = JsonDocument.Parse(body).RootElement;
                return (new ClockResult
                {
                    EntryId          = doc.GetProperty("entryId").GetInt32(),
                    ClockIn          = doc.GetProperty("clockIn").GetDateTime(),
                    ClockInSelfieUrl = doc.TryGetProperty("clockInSelfieUrl", out var cis) && cis.ValueKind == JsonValueKind.String
                                        ? cis.GetString() : null
                }, null);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }

        public async Task<ClockResult?> ClockOutAsync(int employeeId, string? selfieBase64 = null, int? organizationId = null)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Attendance/clockout",
                    new { EmployeeId = employeeId, OrganizationId = organizationId, SelfieBase64 = selfieBase64 });
                if (!response.IsSuccessStatusCode) return null;
                var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                return new ClockResult
                {
                    EntryId           = doc.GetProperty("entryId").GetInt32(),
                    ClockIn           = doc.GetProperty("clockIn").GetDateTime(),
                    ClockOut          = doc.GetProperty("clockOut").GetDateTime(),
                    WorkedHours       = doc.TryGetProperty("workedHours", out var wh) ? wh.GetString() : null,
                    ClockInSelfieUrl  = doc.TryGetProperty("clockInSelfieUrl",  out var cis) && cis.ValueKind == JsonValueKind.String
                                         ? cis.GetString() : null,
                    ClockOutSelfieUrl = doc.TryGetProperty("clockOutSelfieUrl", out var cos) && cos.ValueKind == JsonValueKind.String
                                         ? cos.GetString() : null
                };
            }
            catch { return null; }
        }


        public async Task<(TimeEntryRecord? record, string? error)> UpdateEntryAsync(int entryId, int changedByEmpId, string newClockIn, string? newClockOut, string? reason, string changedField = "in")
        {
            try
            {
                var client = _factory.CreateClient("API");

                // Callers already send UTC ISO-8601 strings with "Z" suffix.
                // Pass straight through — no re-conversion.
                var response = await client.PutAsJsonAsync($"api/Attendance/entry/{entryId}", new
                {
                    ChangedByEmpId  = changedByEmpId,
                    NewClockIn      = newClockIn,
                    NewClockOut     = newClockOut,
                    ReasonForChange = reason,
                    ChangedField    = changedField
                });

                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Try to extract error message from JSON body
                    try
                    {
                        var errDoc = JsonDocument.Parse(body).RootElement;
                        var msg = errDoc.TryGetProperty("error", out var e) ? e.GetString() : body;
                        return (null, msg ?? $"HTTP {(int)response.StatusCode}");
                    }
                    catch { return (null, $"HTTP {(int)response.StatusCode}: {body}"); }
                }

                var doc = JsonDocument.Parse(body).RootElement;
                return (new TimeEntryRecord
                {
                    Id          = doc.GetProperty("id").GetInt32(),
                    ClockIn     = doc.GetProperty("clockIn").GetDateTime(),
                    ClockOut    = doc.TryGetProperty("clockOut", out var co) && co.ValueKind != JsonValueKind.Null
                                    ? co.GetDateTime() : null,
                    WorkedHours = doc.TryGetProperty("workedHours", out var wh) && wh.ValueKind != JsonValueKind.Null
                                    ? wh.GetString() : null
                }, null);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }

        public async Task<bool> DeleteEntryAsync(int entryId, int changedByEmpId)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.DeleteAsync($"api/Attendance/entry/{entryId}?changedByEmpId={changedByEmpId}");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<List<ChangeLogRecord>> GetChangeLogByDateAsync(int employeeId, DateTime date)
        {
            var client  = _factory.CreateClient("API");
            var dateStr = date.ToString("yyyy-MM-dd");
            var response = await client.GetAsync($"api/Attendance/changelog/{employeeId}/bydate?date={dateStr}");

            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"API {(int)response.StatusCode}: {body}");

            var arr  = JsonDocument.Parse(body).RootElement;
            var list = new List<ChangeLogRecord>();
            foreach (var item in arr.EnumerateArray())
            {
                list.Add(new ChangeLogRecord
                {
                    Id              = item.GetProperty("id").GetInt32(),
                    TimeEntryId     = item.TryGetProperty("timeEntryId", out var teid) && teid.ValueKind != JsonValueKind.Null ? teid.GetInt32() : 0,
                    ChangedByName   = item.GetProperty("changedByName").GetString() ?? "",
                    Action          = item.GetProperty("action").GetString() ?? "",
                    OldClockIn      = item.TryGetProperty("oldClockIn",  out var oci) && oci.ValueKind != JsonValueKind.Null ? oci.GetDateTime() : null,
                    OldClockOut     = item.TryGetProperty("oldClockOut", out var oco) && oco.ValueKind != JsonValueKind.Null ? oco.GetDateTime() : null,
                    NewClockIn      = item.TryGetProperty("newClockIn",  out var nci) && nci.ValueKind != JsonValueKind.Null ? nci.GetDateTime() : null,
                    NewClockOut     = item.TryGetProperty("newClockOut", out var nco) && nco.ValueKind != JsonValueKind.Null ? nco.GetDateTime() : null,
                    ReasonForChange = item.TryGetProperty("reasonForChange", out var rfr) && rfr.ValueKind != JsonValueKind.Null ? rfr.GetString() : null,
                    ChangedAt       = item.GetProperty("changedAt").GetDateTime()
                });
            }
            return list;
        }

        public async Task<(bool success, string? error)> AddManualEntryAsync(int employeeId, string entryType, string date, string? time, int hourH = 0, int hourM = 0, int? organizationId = null, int? requesterId = null, bool isAdminOverride = false)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var payload  = new
                {
                    EmployeeId      = employeeId,
                    EntryType       = entryType,
                    Date            = date,
                    Time            = time,
                    HourH           = hourH,
                    HourM           = hourM,
                    OrganizationId  = organizationId,
                    RequesterId     = requesterId,
                    IsAdminOverride = isAdminOverride
                };
                var response = await client.PostAsJsonAsync("api/Attendance/entry/manual", payload);
                if (response.IsSuccessStatusCode) return (true, null);
                var body = await response.Content.ReadAsStringAsync();
                string? msg = null;
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(body);
                    msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString()
                        : doc.RootElement.TryGetProperty("error",   out var e2) ? e2.GetString()
                        : doc.RootElement.TryGetProperty("title",   out var t) ? t.GetString()
                        : null;
                }
                catch { }
                return (false, msg ?? (body.Length < 200 ? body : null));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public async Task<byte[]?> ExportXlsxAsync(
            string from, string to,
            bool includeTimesheets, bool includeTimeEntries,
            bool includePerMemberSummary, bool includePerMemberDetailed,
            int? organizationId = null)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var url = $"api/Attendance/export/xlsx?from={from}&to={to}"
                          + $"&includeRawTimesheets={includeTimesheets.ToString().ToLower()}"
                          + $"&includeRawTimeEntries={includeTimeEntries.ToString().ToLower()}"
                          + $"&includePerMemberSummary={includePerMemberSummary.ToString().ToLower()}"
                          + $"&includePerMemberDetailed={includePerMemberDetailed.ToString().ToLower()}";
                if (organizationId.HasValue && organizationId.Value > 0)
                    url += $"&organizationId={organizationId.Value}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch { return null; }
        }
    }

    public class AttendanceStatus
    {
        public bool     IsClockedIn { get; set; }
        public int      EntryId     { get; set; }
        public DateTime ClockIn     { get; set; }
    }

    public class ClockResult
    {
        public int       EntryId          { get; set; }
        public DateTime  ClockIn          { get; set; }
        public DateTime? ClockOut         { get; set; }
        public string?   WorkedHours      { get; set; }
        public string?   ClockInSelfieUrl  { get; set; }
        public string?   ClockOutSelfieUrl { get; set; }
    }

    public class TimeEntryRecord
    {
        public int       Id               { get; set; }
        public DateTime  ClockIn          { get; set; }
        public DateTime? ClockOut         { get; set; }
        public string?   WorkedHours      { get; set; }
        public bool      IsManual         { get; set; }
        public bool      IsHourEntry      { get; set; }
        public bool      IsBreakEntry     { get; set; }
        public string?   ClockInSelfieUrl  { get; set; }
        public string?   ClockOutSelfieUrl { get; set; }
        public bool      IsOvernightCarryOver { get; set; }
    }

    public class ChangeLogRecord
    {
        public int       Id              { get; set; }
        public int       TimeEntryId     { get; set; }
        public string    ChangedByName   { get; set; } = "";
        public string    Action          { get; set; } = "";
        public DateTime? OldClockIn      { get; set; }
        public DateTime? OldClockOut     { get; set; }
        public DateTime? NewClockIn      { get; set; }
        public DateTime? NewClockOut     { get; set; }
        public string?   ReasonForChange { get; set; }
        public DateTime  ChangedAt       { get; set; }
        /// <summary>True when Action == "AddedHour"; ReasonForChange holds the worked-hours string.</summary>
        public bool      IsHourEntry     => Action == "AddedHour";
        public bool      IsBreakEntry    => Action == "AddedBreak";
        /// <summary>For hour entries, the worked-hours duration string stored in ReasonForChange.</summary>
        public string?   WorkedHours     => IsHourEntry ? ReasonForChange : null;
    }

    public class GroupMember
    {
        public int    Id          { get; set; }
        public string FullName    { get; set; } = "";
        public string Email       { get; set; } = "";
        public bool   IsClockedIn { get; set; }
    }

    public class GroupInfo
    {
        public int    Id   { get; set; }
        public string Name { get; set; } = "";
    }

    public class RangeTimesheetRow
    {
        public int    EmployeeId   { get; set; }
        public string FullName     { get; set; } = "";
        public int    TotalMins    { get; set; }
        public string TotalHours   { get; set; } = "";
        /// <summary>
        /// Schedule-aware payroll total (IncludeBeforeStart clipping + auto-deductions applied).
        /// Falls back to TotalHours if the API does not return regularHours.
        /// </summary>
        public int    RegularMins  { get; set; }
        public string RegularHours { get; set; } = "";
        public Dictionary<string, DayEntry> Days { get; set; } = new();
    }

    public class DayEntry
    {
        public DateTime? FirstIn      { get; set; }
        public DateTime? LastOut      { get; set; }
        public bool      IsOngoing    { get; set; }
        public int       TrackedMins  { get; set; }
        public string    TrackedHours { get; set; } = "";
    }

    public class DailyTimesheetRow
    {
        public int       EmployeeId            { get; set; }
        public string    FullName              { get; set; } = "";
        public DateTime? FirstIn               { get; set; }
        public DateTime? LastOut               { get; set; }
        public bool      IsOngoing             { get; set; }
        public bool      IsRestDay             { get; set; }
        public bool      IsPublicHoliday       { get; set; }
        public bool      HasEntries            { get; set; }
        /// <summary>Approved time-off policy name (e.g. "casual leave"), null if none.</summary>
        public string?   TimeOffLabel          { get; set; }
        public string    TrackedHours          { get; set; } = "—";
        public string    RegularHours          { get; set; } = "—";
        public string?   OvertimeHours         { get; set; }   // Daily OT
        public string?   DailyDoubleOTHours    { get; set; }   // Daily Double OT
        public string?   WeeklyOvertimeHours   { get; set; }   // Weekly OT
        public string?   RestDayOvertimeHours  { get; set; }   // Rest Day OT
        public string?   PublicHolOvertimeHours{ get; set; }   // Public Holiday OT
        public bool      HasDailyOT            { get; set; }
        public bool      HasDailyDblOT         { get; set; }
        public bool      HasWeeklyOT           { get; set; }
        public bool      HasRestDayOT          { get; set; }
        public bool      HasPublicHolOT        { get; set; }
    }

    public class AllMember
    {
        public int       Id          { get; set; }
        public string    FullName    { get; set; } = "";
        public string    Email       { get; set; } = "";
        public int?      GroupId     { get; set; }
        public string?   GroupName   { get; set; }
        public bool      IsClockedIn { get; set; }
        public DateTime? ClockIn     { get; set; }
    }
}
