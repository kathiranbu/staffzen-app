using APM.StaffZen.Blazor.Services;
using APM.StaffZen.Blazor.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace APM.StaffZen.Blazor.Components.Pages.Dashboard;

public partial class Timesheets
{
    // ── Tab state ─────────────────────────────────────────────────────
    private string activeTab = "timesheets"; // "timesheets" | "approvals"

    // ── Pay Period setup card state ───────────────────────────────────
    private bool   ppCardOpen       = false;
    private string ppName           = "";
    private string ppCycle          = "Monthly";
    private bool   ppCycleDropOpen  = false;
    private bool   ppSaving         = false;
    private string ppSaveError      = "";

    // Weekly / Every-two-weeks: selected day-of-week (0=Mon … 6=Sun)
    private int    ppDow            = (int)DateTime.Today.DayOfWeek == 0 ? 6 : (int)DateTime.Today.DayOfWeek - 1;
    // Weekly / Every-two-weeks: selected "From" start date
    private DateTime? ppFromDate    = null;

    // Monthly: single day (1–31)
    private int    ppMonthDay       = 1;
    // Twice-a-month: two days
    private int    ppSemiDay1       = 1;
    private int    ppSemiDay2       = 16;
    private bool   ppSemiDay1Open   = false;
    private bool   ppSemiDay2Open   = false;

    // Helper: periods-per-year text
    private string PpPeriodsLabel => ppCycle switch {
        "Weekly"          => "52 pay periods a year",
        "Every two weeks" => "26 pay periods a year",
        "Twice a month"   => "24 pay periods a year",
        "Monthly"         => "12 pay periods a year",
        _                 => ""
    };

    // Helper: candidate "From" dates for weekly / bi-weekly
    private List<DateTime> PpFromOptions
    {
        get
        {
            var list = new List<DateTime>();
            // Find the most recent past date with the correct DOW
            // DOW mapping: ppDow 0=Mon,1=Tue,...6=Sun => DayOfWeek Mon=1..Sun=0
            var targetDow = (DayOfWeek)(ppDow == 6 ? 0 : ppDow + 1);
            var today = DateTime.Today;
            // go back up to 13 days to find last occurrence
            int step = 0;
            while (step < 14 && today.AddDays(-step).DayOfWeek != targetDow) step++;
            var anchor = today.AddDays(-step);
            // offer: anchor-14, anchor, anchor+14 (only past 3 weeks and up to 14d ahead)
            for (int i = -1; i <= 1; i++)
            {
                var d = ppCycle == "Every two weeks" ? anchor.AddDays(i * 14) : anchor.AddDays(i * 7);
                list.Add(d);
            }
            return list;
        }
    }

    private void PpChangeCycle(string newCycle)
    {
        ppCycle        = newCycle;
        ppCycleDropOpen = false;
        ppFromDate      = null;
        ppSaveError     = "";
        // Reset defaults
        ppDow      = (int)DateTime.Today.DayOfWeek == 0 ? 6 : (int)DateTime.Today.DayOfWeek - 1;
        ppMonthDay  = 1;
        ppSemiDay1  = 1;
        ppSemiDay2  = 16;
    }

    private async Task PpSave()
    {
        ppSaveError = "";
        // Validation
        if (string.IsNullOrWhiteSpace(ppName)) { ppSaveError = "Please enter a name."; return; }
        if ((ppCycle == "Weekly" || ppCycle == "Every two weeks") && ppFromDate == null)
        { ppSaveError = "Please select a start date."; return; }
        if (ppCycle == "Twice a month" && ppSemiDay1 == ppSemiDay2)
        { ppSaveError = "The two days must be different."; return; }

        // Normalize frequency to the strings that GetPeriodStartFor/ComputePeriodEnd expect
        string normalizedFreq = ppCycle switch
        {
            "Weekly"          => "Weekly",
            "Every two weeks" => "Biweekly",
            "Twice a month"   => "Semi-monthly",
            "Monthly"         => "Monthly",
            _                 => ppCycle
        };

        var dto = new PayPeriodSettingDto
        {
            Name      = ppName.Trim(),
            Frequency = normalizedFreq,
            StartDow  = ppDow,
            // For Monthly: store start day in both FirstDay and SemiDay (ComputePeriodEnd reads SemiDay for Monthly)
            FirstDay  = ppCycle == "Twice a month" ? ppSemiDay1 : ppMonthDay,
            SemiDay   = ppCycle == "Twice a month" ? ppSemiDay2
                      : ppCycle == "Monthly"       ? ppMonthDay
                      : 0,
            StartDate = ppFromDate ?? DateTime.Today,
        };

        ppSaving = true;
        (bool ok, string? err) = await PayPeriodService.SaveAsync(SessionService.ActiveOrganizationId ?? 0, dto);
        ppSaving = false;
        if (ok)
        {
            _payPeriod    = dto;
            ppCardOpen    = false;
            // Recalculate the approvals period start based on the new pay period setting
            apvWeekStart  = GetPeriodStartFor(DateTime.Today);
            apvCalYear    = apvWeekStart.Year;
            apvCalMonth   = apvWeekStart.Month;
            apvStatusMap.Clear();
            // Reload approvals data immediately — no manual refresh needed
            await ApvLoadData();
        }
        else
        {
            ppSaveError = err ?? "Failed to save.";
        }
    }

    private void PpOpenCard()
    {
        // Pre-populate from existing setting if available
        if (_payPeriod != null)
        {
            ppName  = _payPeriod.Name;
            // Reverse-map stored frequency string back to UI cycle label
            ppCycle = _payPeriod.Frequency.ToLowerInvariant() switch
            {
                "biweekly" or "bi-weekly" or "every two weeks" => "Every two weeks",
                "semi-monthly" or "semimonthly" or "twice a month" => "Twice a month",
                "monthly" => "Monthly",
                _ => "Weekly"
            };
            ppDow      = _payPeriod.StartDow;
            ppMonthDay = _payPeriod.FirstDay > 0 ? _payPeriod.FirstDay : 1;
            ppSemiDay1 = _payPeriod.FirstDay > 0 ? _payPeriod.FirstDay : 1;
            ppSemiDay2 = _payPeriod.SemiDay  > 0 ? _payPeriod.SemiDay  : 16;
            ppFromDate = _payPeriod.StartDate;
        }
        ppSaveError     = "";
        ppCycleDropOpen = false;
        ppSemiDay1Open  = false;
        ppSemiDay2Open  = false;
        ppCardOpen      = true;
    }

    // ── Pay Period setting (loaded on init) ───────────────────────────
    private PayPeriodSettingDto? _payPeriod = null;

    // ── Approvals state ───────────────────────────────────────────────
    // apvPeriodStart / apvPeriodEnd reflect the *pay-period* boundaries
    private DateTime apvWeekStart     = GetCurrentPayWeekStart();
    private DateTime apvWeekEnd       => ComputePeriodEnd(apvWeekStart, null);
    // Keep these for the calendar picker
    private bool     apvCalPickerOpen  = false;
    private int      apvCalYear        = DateTime.Today.Year;
    private int      apvCalMonth       = DateTime.Today.Month;
    private bool     apvStatusOpen     = false;
    private bool     apvAddFilterOpen  = false;
    private HashSet<string> apvSelectedStatuses = new();
    private string   apvSearch         = "";
    private string   apvSortCol        = "";
    private bool     apvSortAsc        = true;
    // Per-employee approval status (employeeId → status string)
    private Dictionary<int, string> apvStatusMap = new();
    // Row selected in Approvals tab — passed to TimesheetApprovalDetail
    private RangeTimesheetRow? apvDetailRow = null;

    // ── Checkbox selection state (approvals past-period rows) ─────────
    private HashSet<int> apvCheckedRows = new();
    private bool         apvAllChecked  = false;

    private void ApvToggleRow(int employeeId)
    {
        if (apvCheckedRows.Contains(employeeId)) apvCheckedRows.Remove(employeeId);
        else apvCheckedRows.Add(employeeId);
        apvAllChecked = rangeRows.Count > 0 && rangeRows.All(r => apvCheckedRows.Contains(r.EmployeeId));
    }

    private void ApvToggleAll()
    {
        if (apvAllChecked)
            foreach (var r in rangeRows) apvCheckedRows.Add(r.EmployeeId);
        else
            apvCheckedRows.Clear();
    }

    // ── Pay-period helpers ────────────────────────────────────────────

    /// <summary>
    /// Returns the start-of-period date that contains <paramref name="date"/>,
    /// based on the current pay-period setting.
    /// </summary>
    private DateTime GetPeriodStartFor(DateTime date)
    {
        if (_payPeriod == null)
        {
            // Fallback: Monday of the week
            int d = (int)date.DayOfWeek;
            return date.AddDays(-(d == 0 ? 6 : d - 1));
        }

        var freq    = _payPeriod.Frequency.ToLowerInvariant();
        var anchor  = _payPeriod.StartDate.Date; // the configured "first period start"

        switch (freq)
        {
            case "weekly":
            {
                // Align to the anchor date: find the start of the 7-day block containing date
                int totalDays = (int)(date.Date - anchor).TotalDays;
                if (totalDays < 0)
                {
                    int rem = ((-totalDays) % 7);
                    return date.AddDays(rem == 0 ? 0 : -(7 - rem)).Date;
                }
                int periodDays = (totalDays / 7) * 7;
                return anchor.AddDays(periodDays);
            }
            case "biweekly":
            case "bi-weekly":
            case "every two weeks":
            {
                // Count weeks from anchor, align to 2-week blocks
                int totalDays = (int)(date.Date - anchor).TotalDays;
                if (totalDays < 0)
                {
                    int rem = ((-totalDays) % 14);
                    return date.AddDays(rem == 0 ? 0 : -(14 - rem)).Date;
                }
                int periodDays = (totalDays / 14) * 14;
                return anchor.AddDays(periodDays);
            }
            case "semi-monthly":
            case "semimonthly":
            case "twice a month":
            {
                // Two periods: firstDay → (secondDay-1), and secondDay → last day of month
                int firstDay  = _payPeriod.FirstDay > 0 ? _payPeriod.FirstDay : 1;
                int secondDay = _payPeriod.SemiDay  > 0 ? _payPeriod.SemiDay  : 16;
                if (date.Day < secondDay)
                    return new DateTime(date.Year, date.Month, Math.Min(firstDay, DateTime.DaysInMonth(date.Year, date.Month)));
                else
                    return new DateTime(date.Year, date.Month, Math.Min(secondDay, DateTime.DaysInMonth(date.Year, date.Month)));
            }
            case "monthly":
            {
                // Period starts on configured day (SemiDay holds this for Monthly, FirstDay as fallback)
                int startDay = _payPeriod.SemiDay > 0 ? _payPeriod.SemiDay
                             : _payPeriod.FirstDay > 0 ? _payPeriod.FirstDay : 1;
                if (date.Day >= startDay)
                    return new DateTime(date.Year, date.Month, Math.Min(startDay, DateTime.DaysInMonth(date.Year, date.Month)));
                else
                {
                    var prevMonth = date.AddMonths(-1);
                    int safeDay = Math.Min(startDay, DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month));
                    return new DateTime(prevMonth.Year, prevMonth.Month, safeDay);
                }
            }
            default:
            {
                // Default: align to anchor using weekly
                int totalDays = (int)(date.Date - anchor).TotalDays;
                if (totalDays < 0)
                {
                    int rem = ((-totalDays) % 7);
                    return date.AddDays(rem == 0 ? 0 : -(7 - rem)).Date;
                }
                return anchor.AddDays((totalDays / 7) * 7);
            }
        }
    }

    /// <summary>
    /// Returns the last day of the period that starts on <paramref name="start"/>.
    /// Pass <c>null</c> for <paramref name="pp"/> to use the instance field.
    /// </summary>
    private DateTime ComputePeriodEnd(DateTime start, PayPeriodSettingDto? pp)
    {
        var setting = pp ?? _payPeriod;
        if (setting == null) return start.AddDays(6);

        var freq = setting.Frequency.ToLowerInvariant();
        switch (freq)
        {
            case "weekly":
                return start.AddDays(6);
            case "biweekly":
            case "bi-weekly":
            case "every two weeks":
                return start.AddDays(13);
            case "semi-monthly":
            case "semimonthly":
            case "twice a month":
            {
                int firstDay  = setting.FirstDay > 0 ? setting.FirstDay : 1;
                int secondDay = setting.SemiDay  > 0 ? setting.SemiDay  : 16;
                // Is this the first period (starts on firstDay)?
                if (start.Day == firstDay)
                {
                    // First period ends day before secondDay of same month
                    return new DateTime(start.Year, start.Month, Math.Min(secondDay - 1, DateTime.DaysInMonth(start.Year, start.Month)));
                }
                else
                {
                    // Second period ends last day of same month (does NOT extend into next month)
                    return new DateTime(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month));
                }
            }
            case "monthly":
            {
                // Start day is stored in SemiDay for Monthly (FirstDay as fallback)
                int startDay = setting.SemiDay > 0 ? setting.SemiDay
                             : setting.FirstDay > 0 ? setting.FirstDay : 1;
                var nextMonth = start.AddMonths(1);
                int safeStartDay = Math.Min(startDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
                var nextStart = new DateTime(nextMonth.Year, nextMonth.Month, safeStartDay);
                return nextStart.AddDays(-1);
            }
            default:
                return start.AddDays(6);
        }
    }

    /// <summary>Moves one period forward from <paramref name="start"/>.</summary>
    private DateTime NextPeriodStart(DateTime start)
    {
        var end = ComputePeriodEnd(start, _payPeriod);
        return end.AddDays(1);
    }

    /// <summary>Moves one period backward from <paramref name="start"/>.</summary>
    private DateTime PrevPeriodStart(DateTime start)
    {
        // Step one day before current start, then find the period start for that day
        return GetPeriodStartFor(start.AddDays(-1));
    }

    // Helper: get the Monday of the current week (used as static initialiser fallback)
    private static DateTime GetCurrentPayWeekStart()
    {
        var today = DateTime.Today;
        int dow   = (int)today.DayOfWeek;
        return today.AddDays(-(dow == 0 ? 6 : dow - 1));
    }

    // Day-of-week header labels for calendar, ordered by pay-period start day
    private string[] ApvCalDowHeaders()
    {
        var allDows = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        var freqLower = _payPeriod?.Frequency?.ToLowerInvariant() ?? "";
        int startDow = (freqLower == "weekly" || freqLower == "biweekly" || freqLower == "bi-weekly" || freqLower == "every two weeks")
                       ? _payPeriod!.StartDow
                       : 1; // default Monday
        var ordered = new string[7];
        for (int i = 0; i < 7; i++)
            ordered[i] = allDows[(startDow + i) % 7];
        return ordered;
    }
    // ── View state ───────────────────────────────────────────────────
    private BasePage? basePage;
    private string currentView = "list";

    // ── View mode: daily / weekly / monthly ──────────────────────────
    private string viewMode          = "daily";
    private bool   showViewDropdown  = false;
    private bool   orgView           = true;

    // ── Daily list state ─────────────────────────────────────────────
    private DateTime              dtlSelectedDate = DateTime.Today;
    private List<DailyTimesheetRow> dtlRows       = new();
    private List<AllMember>       allOrgMembers   = new();   // all org members — used when no entries for a holiday/rest day
    private bool                  dtlLoading      = false;
    private string                dtlSearchText   = "";

    // ── Weekly state ─────────────────────────────────────────────────
    private DateTime weekRangeFrom = DateTime.Today.AddDays(-((int)DateTime.Today.DayOfWeek == 0 ? 6 : (int)DateTime.Today.DayOfWeek - 1));
    private DateTime weekRangeTo   => weekRangeFrom.AddDays(6);

    // ── Monthly state ────────────────────────────────────────────────
    private DateTime monthDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    // ── Range rows (weekly + monthly) ────────────────────────────────
    private List<RangeTimesheetRow> rangeRows = new();

    // ── User-role pay-period approval rows ──────────────────────────
    private List<UserPayPeriodRow> userPayPeriods = new();

    public class UserPayPeriodRow
    {
        public DateTime Start       { get; set; }
        public DateTime End         { get; set; }
        public string   TotalHours  { get; set; } = "";
        public string   RegularHours { get; set; } = "";
        public string   Status      { get; set; } = "Open";
    }

    // Time-off map for weekly view: employeeId → (dateKey → policyName)
    private Dictionary<int, Dictionary<string,string>> weekTimeOffsByEmp = new();

    // ── Calendar picker ──────────────────────────────────────────────
    private bool showCalPicker  = false;
    private int  calPickerYear  = DateTime.Today.Year;
    private int  calPickerMonth = DateTime.Today.Month;

    // ── Legend ───────────────────────────────────────────────────────
    private bool showLegend = false;

    // ── Filter state ─────────────────────────────────────────────────
    private bool filterPayroll = true;
    private bool filterGroups  = true;
    private bool filterMembers = true;
    private bool showAddFilter = false;

    // ── Schedules dropdown ───────────────────────────────────────────
    private bool         showSchedulesDropdown = false;
    private string?      selectedSchedule      = null;
    private string       scheduleSearch        = "";
    private List<string> availableSchedules    = new() { "Default Work Schedule" };

    // ── Tooltip ──────────────────────────────────────────────────────
    private bool      tooltipVisible = false;
    private DayEntry? tooltipEntry   = null;
    private string    tooltipDate    = "";
    private double    tooltipX       = 0;
    private double    tooltipY       = 0;

    // ── Hovered cell ─────────────────────────────────────────────────
    private string hoveredCell   = "";
    private double createTipX    = 0;
    private double createTipY    = 0;
    private bool   plusTipVisible = false;

    // ── Page / detail-view state ─────────────────────────────────────
    private bool   isLoaded            = false;
    private bool   entriesLoading      = false;
    private int    employeeId;
    private string selectedEmployeeName = "";

    // ── Selfie policy ─────────────────────────────────────────────────
    // True when RequireSelfie or RequireFaceVerification is enabled on the default schedule.
    // Controls whether selfie photos are shown in the Time Entries rows.
    private bool selfiePolicyEnabled = false;

    private DateTime              weekStart;
    private DateTime              selectedDate;
    private List<TimeEntryRecord> weekEntries = new();
    private List<TimeEntryRecord> dayEntries  = new();
    private string weekTrackedDisplay = "0h 0m";
    private string dayTrackedDisplay  = "0h 0m";
    private string dayRegularDisplay  = "0h 0m";   // regular hours for selected day (after OT split)
    private string? dayOvertimeDisplay     = null;  // daily overtime hours (null = no OT rule)
    private string? dayDoubleOTDisplay     = null;  // double OT hours for selected day
    private string? dayWeeklyOTDisplay     = null;  // weekly OT hours for selected day
    private string? dayRestDayOTDisplay    = null;  // rest day OT hours for selected day
    private string? dayPublicHolOTDisplay  = null;  // public holiday OT hours for selected day

    // Cache of per-day durations keyed by date.Date, populated by GetHistoryByDateAsync.
    // Used by DayDuration() so week-bar badges correctly handle overnight carry-overs.
    private Dictionary<DateTime, TimeSpan> weekDayDurationCache = new();

    // ── Accordion state ──────────────────────────────────────────────
    private bool trackedExpanded = false;
    private bool payrollExpanded = false;
    private bool historyExpanded = false;

    // ── Change history ───────────────────────────────────────────────
    private List<ChangeHistoryEntry> changeHistory = new();
    private string changeHistoryError = "";

    // ── Panel state (view/edit entry) ────────────────────────────────
    private TimeEntryRecord? viewEntry = null;
    private string panelMode = "none";   // "none" | "view" | "edit"
    private string panelType = "in";     // "in"   | "out"
    private string viewEntryLocation = ""; // location name for the viewed entry

    // ── Selfie lightbox (View Time Entry panel avatar click) ─────────
    private bool   tepSelfieOpen    = false;
    private string tepSelfieZoomUrl = "";

    // Edit input fields
    private string editClockInTime  = "";
    private string editClockInDate  = "";
    private string editClockOutTime = "";
    private string editClockOutDate = "";
    private string editNote         = "";
    private string editLocationName = "";
    private string editError        = "";
    private bool   editSaving = false;

    // Calendar picker (edit panel)
    private bool     showCalendar = false;
    private DateTime calViewDate  = DateTime.Today;

    // Custom time picker (edit panel)
    private bool   showTimePicker = false;
    private int    tpHour   = 12;
    private int    tpMinute = 0;
    private string tpAmPm   = "AM";

    // Reason modal
    private bool   showReasonModal  = false;
    private string reasonForChange  = "";

    // ── Delete state ─────────────────────────────────────────────────
    private TimeEntryRecord? deleteTargetEntry = null;
    private bool   showDeleteModal   = false;
    private bool   deleteInProgress  = false;
    private string deleteError       = "";

    // ── Duplicate Timesheets modal state ─────────────────────────────
    private bool               showDuplicateModal = false;
    private RangeTimesheetRow? duplicateTargetRow = null;
    private DateTime           dupFromStart       = DateTime.Today.AddDays(-7);
    private DateTime?          dupToStart         = null;
    private string             dupFromHours       = "168h 0m";
    private int                dupActivePlusIdx   = -1;

    private class DupTimeEntry { public string ClockIn = "09:00"; public string ClockOut = "17:00"; }
    private class DupDayItem
    {
        public string           Label       = "";
        public string           WorkedHours = "0h 0m";
        public string           BreakHours  = "0h 0m";
        public List<DupTimeEntry> TimeEntries = new();
    }
    private List<DupDayItem> dupDayBreakdown = new();

    // ── Export Modal state ───────────────────────────────────────────
    private bool   showExportModal       = false;
    private string expFormat             = "csv";
    private bool   expIncludeTimesheets  = false;
    private bool   expIncludeTimeEntries = false;
    private bool   expIncludePerMember   = false;
    private string expPerMemberMode      = "summary";
    private bool   expPerMemberModeOpen  = false;
    private string expDateRange          = "week";
    private string expTimeFormat         = "12h";
    private string expDurationFormat     = "hm";

    // Export dropdowns
    private bool expShowPeriodDd       = false;
    private bool expShowDateCal        = false;
    private bool expPayrollOpen        = false;
    private bool expGroupsOpen         = false;
    private bool expMembersOpen        = false;
    private bool expSchedulesOpen      = false;
    private bool expAddFilterOpen      = false;
    private bool expTimeFormatOpen     = false;
    private bool expDurationFormatOpen = false;

    // Export calendar
    private int       expCalYear    = DateTime.Today.Year;
    private int       expCalMonth   = DateTime.Today.Month;
    private DateTime  expRangeStart = DateTime.Today;
    private DateTime? expRangeEnd   = null;

    // Export filters
    private bool         expExcludeRestDays   = false;
    private bool         expExcludeTimeOffs   = false;
    private string?      expSelectedHours     = null;
    private string       expGroupSearch       = "";
    private string       expMemberSearch      = "";
    private string       expScheduleSearch    = "";
    private bool         expShowArchived      = false;
    private List<int>    expSelectedGroupIds  = new();
    private List<int>    expSelectedMemberIds = new();
    private List<string> expSelectedSchedules = new();

    // Export data
    private List<GroupInfo>  expAllGroups  = new();
    private List<AllMember>  expAllMembers = new();

    // ── Data models ──────────────────────────────────────────────────
    public class ChangeHistoryEntry
    {
        public string    ChangedByName  { get; set; } = "";
        public string    Action         { get; set; } = "";
        public DateTime? OldClockIn     { get; set; }
        public DateTime? OldClockOut    { get; set; }
        public DateTime? NewClockIn     { get; set; }
        public DateTime? NewClockOut    { get; set; }
        public string?   ReasonForChange{ get; set; }
        public DateTime  ChangedAt      { get; set; }
        public bool      IsHourEntry    => Action == "AddedHour";
        public bool      IsBreakEntry   => Action == "AddedBreak";
        public string?   WorkedHours    => IsHourEntry ? ReasonForChange : null;
    }
}
