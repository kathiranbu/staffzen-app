using APM.StaffZen.Blazor.Services;
namespace APM.StaffZen.Blazor.Components.Pages.Dashboard;

public partial class Timesheets
{
    // ── Navigation ───────────────────────────────────────────────────
    private async Task NavPrev()
    {
        if      (viewMode == "daily")  { dtlSelectedDate = dtlSelectedDate.AddDays(-1); await LoadDailyList(); }
        else if (viewMode == "weekly") { weekRangeFrom   = weekRangeFrom.AddDays(-7);   await LoadRangeData(); }
        else                           { monthDate        = monthDate.AddMonths(-1);     await LoadRangeData(); }
    }

    private async Task NavNext()
    {
        if      (viewMode == "daily")  { dtlSelectedDate = dtlSelectedDate.AddDays(1);  await LoadDailyList(); }
        else if (viewMode == "weekly") { weekRangeFrom   = weekRangeFrom.AddDays(7);    await LoadRangeData(); }
        else                           { monthDate        = monthDate.AddMonths(1);      await LoadRangeData(); }
    }

    private async Task SetViewMode(string mode)
    {
        viewMode         = mode;
        showViewDropdown = false;
        if (mode == "daily") await LoadDailyList();
        else                 await LoadRangeData();
    }

    // ── Calendar picker ──────────────────────────────────────────────
    private void ToggleCalPicker()
    {
        showCalPicker = !showCalPicker;
        if (showCalPicker)
        {
            if      (viewMode == "daily")  { calPickerYear = dtlSelectedDate.Year; calPickerMonth = dtlSelectedDate.Month; }
            else if (viewMode == "weekly") { calPickerYear = weekRangeFrom.Year;   calPickerMonth = weekRangeFrom.Month;   }
            else                           { calPickerYear = monthDate.Year;       calPickerMonth = monthDate.Month;       }
        }
    }

    private void CalPickerPrevMonth() { calPickerMonth--; if (calPickerMonth < 1)  { calPickerMonth = 12; calPickerYear--; } }
    private void CalPickerNextMonth() { calPickerMonth++; if (calPickerMonth > 12) { calPickerMonth = 1;  calPickerYear++; } }
    private void CalPickerPrevYear()  => calPickerYear--;
    private void CalPickerNextYear()  => calPickerYear++;

    private async Task CalPickerSelectDay(DateTime date)
    {
        showCalPicker  = false;
        calPickerYear  = date.Year;
        calPickerMonth = date.Month;

        if (viewMode == "daily")
        {
            dtlSelectedDate = date;
            await LoadDailyList();
        }
        else if (viewMode == "weekly")
        {
            int dow = (int)date.DayOfWeek;
            weekRangeFrom = date.AddDays(-(dow == 0 ? 6 : dow - 1));
            await LoadRangeData();
        }
        else
        {
            monthDate = new DateTime(date.Year, date.Month, 1);
            await LoadRangeData();
        }
    }

    private async Task CalPickerSelect(int month) => await CalPickerSelectDay(new DateTime(calPickerYear, month, 1));

    // ── Data loading ─────────────────────────────────────────────────
    private async Task LoadDailyList()
    {
        dtlLoading = true; StateHasChanged();
        dtlRows    = await AttendanceService.GetDailyTimesheetAsync(dtlSelectedDate, SessionService.ActiveOrganizationId);
        dtlLoading = false; StateHasChanged();
    }

    /// <summary>
    /// Silently pre-loads the current month's range rows and time-off map in the background
    /// after the daily view is shown, so switching to monthly/weekly is instant.
    /// Does NOT touch dtlLoading — no spinner is shown.
    /// </summary>
    private async Task PreloadMonthRangeAsync()
    {
        try
        {
            var from = monthDate;
            var to   = new DateTime(monthDate.Year, monthDate.Month, DateTime.DaysInMonth(monthDate.Year, monthDate.Month));
            rangeRows = await AttendanceService.GetRangeTimesheetAsync(from, to, SessionService.ActiveOrganizationId);

            // Build time-off map for the current month
            weekTimeOffsByEmp = new();
            try
            {
                var client = AttendanceService.GetHttpClient();
                var f2 = from.ToString("yyyy-MM-dd"); var t2 = to.ToString("yyyy-MM-dd");
                var resp = await client.GetAsync($"api/time-off-requests?start={f2}&end={t2}");
                if (resp.IsSuccessStatusCode)
                {
                    var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("status", out var st) && st.GetString() != "Approved") continue;
                        int empId = item.GetProperty("employeeId").GetInt32();
                        string polName = item.TryGetProperty("policyName", out var pn) ? pn.GetString() ?? "Time Off" : "Time Off";
                        if (!DateTime.TryParse(item.GetProperty("startDate").GetString(), out var sd)) continue;
                        DateTime? ed = item.TryGetProperty("endDate", out var edp) && edp.ValueKind != System.Text.Json.JsonValueKind.Null
                                       ? DateTime.TryParse(edp.GetString(), out var edv) ? edv : sd : sd;
                        if (!weekTimeOffsByEmp.ContainsKey(empId)) weekTimeOffsByEmp[empId] = new();
                        for (var d = sd.Date; d <= ed.Value.Date; d = d.AddDays(1))
                            weekTimeOffsByEmp[empId][d.ToString("yyyy-MM-dd")] = polName;
                    }
                }
            }
            catch { /* time-off best-effort */ }

            await InvokeAsync(StateHasChanged);
        }
        catch { /* preload is best-effort — user will get fresh data when they switch views */ }
    }

    private async Task LoadRangeData()
    {
        dtlLoading = true; StateHasChanged();
        DateTime from, to;
        if (viewMode == "weekly") { from = weekRangeFrom; to = weekRangeTo; }
        else                      { from = monthDate;     to = new DateTime(monthDate.Year, monthDate.Month, DateTime.DaysInMonth(monthDate.Year, monthDate.Month)); }
        rangeRows  = await AttendanceService.GetRangeTimesheetAsync(from, to, SessionService.ActiveOrganizationId);

        // Build time-off map for weekly view: employeeId → (dateKey "yyyy-MM-dd" → policyName)
        weekTimeOffsByEmp = new();
        try
        {
            var client   = AttendanceService.GetHttpClient();
            var f2 = from.ToString("yyyy-MM-dd"); var t2 = to.ToString("yyyy-MM-dd");
            var resp     = await client.GetAsync($"api/time-off-requests?start={f2}&end={t2}");
            if (resp.IsSuccessStatusCode)
            {
                var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("status", out var st) && st.GetString() != "Approved") continue;
                    int empId = item.GetProperty("employeeId").GetInt32();
                    string polName = item.TryGetProperty("policyName", out var pn) ? pn.GetString() ?? "Time Off" : "Time Off";
                    if (!DateTime.TryParse(item.GetProperty("startDate").GetString(), out var sd)) continue;
                    DateTime? ed = item.TryGetProperty("endDate", out var edp) && edp.ValueKind != System.Text.Json.JsonValueKind.Null
                                   ? DateTime.TryParse(edp.GetString(), out var edv) ? edv : sd : sd;
                    if (!weekTimeOffsByEmp.ContainsKey(empId)) weekTimeOffsByEmp[empId] = new();
                    for (var d = sd.Date; d <= ed.Value.Date; d = d.AddDays(1))
                        weekTimeOffsByEmp[empId][d.ToString("yyyy-MM-dd")] = polName;
                }
            }
        }
        catch { /* time-off data is best-effort */ }

        dtlLoading = false; StateHasChanged();
    }

    // ── Filter helpers ───────────────────────────────────────────────
    private void ToggleFilter(string f)
    {
        if      (f == "Payroll hours") filterPayroll = !filterPayroll;
        else if (f == "Groups")        filterGroups  = !filterGroups;
        else if (f == "Members")       filterMembers = !filterMembers;
    }

    private void CloseAllDropdowns()
    {
        showViewDropdown = showSchedulesDropdown = showAddFilter = showCalPicker = showLegend = false;
    }

    // ── Tooltip / hover ──────────────────────────────────────────────
    private void ShowTooltip(string name, DayEntry de, string date)
    {
        tooltipEntry = de; tooltipDate = date; tooltipVisible = true;
        StateHasChanged();
    }

    private void ShowTooltipWithCoords(string name, DayEntry de, string date, Microsoft.AspNetCore.Components.Web.MouseEventArgs e)
    {
        tooltipEntry = de; tooltipDate = date;
        tooltipX = e.ClientX - 120; tooltipY = e.ClientY + 18;
        tooltipVisible = true; StateHasChanged();
    }

    private void HideTooltip() { tooltipVisible = false; StateHasChanged(); }

    private void OnWeekCellHover(string cellKey) { hoveredCell = cellKey; StateHasChanged(); }

    private void OnWeekCellLeave() { hoveredCell = ""; plusTipVisible = false; tooltipVisible = false; createTipY = 0; StateHasChanged(); }

    private void OnHoursHover(string cellKey, string name, DayEntry? de, string dateKey, Microsoft.AspNetCore.Components.Web.MouseEventArgs e)
    {
        plusTipVisible = false;
        if (de != null) { tooltipX = e.ClientX - 120; tooltipY = e.ClientY + 18; ShowTooltip(name, de, dateKey); }
        StateHasChanged();
    }

    private void OnHoursLeave() { tooltipVisible = false; StateHasChanged(); }

    private void OnPlusHover(string cellKey, Microsoft.AspNetCore.Components.Web.MouseEventArgs e)
    {
        tooltipVisible = false; createTipX = e.ClientX - 72; createTipY = e.ClientY + 38;
        plusTipVisible = true; StateHasChanged();
    }

    private void OnPlusLeave() { plusTipVisible = false; createTipY = 0; StateHasChanged(); }

    // ── Approvals helpers ────────────────────────────────────────────

    // Navigate Approvals period by one pay period (not hardcoded 7 days)
    private async Task ApvNavPrev()
    {
        apvWeekStart  = PrevPeriodStart(apvWeekStart);
        apvCalYear    = apvWeekStart.Year;
        apvCalMonth   = apvWeekStart.Month;
        await ApvLoadData();
    }

    private async Task ApvNavNext()
    {
        apvWeekStart  = NextPeriodStart(apvWeekStart);
        apvCalYear    = apvWeekStart.Year;
        apvCalMonth   = apvWeekStart.Month;
        await ApvLoadData();
    }

    // Calendar picker: day clicked → jump to the pay period containing that day
    private async Task ApvCalSelectDay(DateTime date)
    {
        apvWeekStart     = GetPeriodStartFor(date);
        apvCalYear       = apvWeekStart.Year;
        apvCalMonth      = apvWeekStart.Month;
        apvCalPickerOpen = false;
        await ApvLoadData();
    }

    // Calendar picker month nav
    private void ApvCalPrevMonth()
    {
        apvCalMonth--;
        if (apvCalMonth < 1) { apvCalMonth = 12; apvCalYear--; }
    }
    private void ApvCalNextMonth()
    {
        apvCalMonth++;
        if (apvCalMonth > 12) { apvCalMonth = 1; apvCalYear++; }
    }

    // Load range data scoped to the current approvals week
    private async Task ApvLoadData()
    {
        apvCheckedRows.Clear();
        apvAllChecked = false;
        dtlLoading = true; StateHasChanged();

        if (!SessionService.CanManageActiveOrg)
        {
            // User role: build all pay periods for the selected year
            await ApvLoadUserPeriods();
        }
        else
        {
            var from = apvWeekStart;
            var to   = apvWeekEnd;
            rangeRows = await AttendanceService.GetRangeTimesheetAsync(from, to, SessionService.ActiveOrganizationId);
        }

        dtlLoading = false; StateHasChanged();
    }

    /// <summary>
    /// Builds the list of all pay periods for the currently selected year,
    /// loading the logged-in user's tracked hours for each, for the user-role approval view.
    /// </summary>
    private async Task ApvLoadUserPeriods()
    {
        userPayPeriods.Clear();

        // Enumerate every period that starts within the selected year
        var yearStart = new DateTime(apvWeekStart.Year, 1, 1);
        var yearEnd   = new DateTime(apvWeekStart.Year, 12, 31);

        // Walk periods from the beginning of the year
        var periodStart = GetPeriodStartFor(yearStart);
        // If the first period starts before the year, move to the next
        if (periodStart < yearStart)
            periodStart = NextPeriodStart(periodStart);

        while (periodStart <= yearEnd)
        {
            var periodEnd = ComputePeriodEnd(periodStart, null);

            // Load the user's own data for this period
            var rows = await AttendanceService.GetRangeTimesheetAsync(
                periodStart, periodEnd, SessionService.ActiveOrganizationId);

            var myRow = rows.FirstOrDefault(r => r.EmployeeId == SessionService.Id);

            bool isCurrent  = DateTime.Today >= periodStart.Date && DateTime.Today <= periodEnd.Date;
            bool isClosed   = periodEnd.Date < DateTime.Today;
            bool hasHours   = myRow != null && myRow.TotalMins > 0;

            string status = isCurrent && hasHours  ? "In progress"
                          : isCurrent && !hasHours ? "Open"
                          : isClosed  && hasHours  ? "Open"
                          : "-"; // future or no data

            userPayPeriods.Add(new UserPayPeriodRow
            {
                Start        = periodStart,
                End          = periodEnd,
                TotalHours   = myRow?.TotalHours   ?? "",
                RegularHours = myRow?.RegularHours ?? "",
                Status       = status
            });

            periodStart = NextPeriodStart(periodStart);
        }
    }

    // Return the approval status for an employee, driven by pay-period timing
    private string ApvGetStatus(int employeeId)
    {
        // Manually set status (e.g. after clicking Approve) takes priority
        if (apvStatusMap.TryGetValue(employeeId, out var st)) return st;

        // Determine whether the current pay period is still ongoing or already closed
        bool periodClosed = apvWeekEnd.Date < DateTime.Today;

        var row = rangeRows.FirstOrDefault(r => r.EmployeeId == employeeId);
        bool hasHours = row != null && row.TotalMins > 0;

        if (!hasHours)
            return "-";  // No data → show dash, not a misleading status

        // Ongoing period with tracked hours → In progress
        // Closed period with tracked hours → Open (ready to be approved)
        return periodClosed ? "Open" : "In progress";
    }

    // Approve a single employee timesheet for the current period
    private void ApvApprove(int employeeId)
    {
        apvStatusMap[employeeId] = "Approved";
        StateHasChanged();
    }

    // Sorting toggle (column header click)
    private void ApvSort(string col)
    {
        if (apvSortCol == col) apvSortAsc = !apvSortAsc;
        else { apvSortCol = col; apvSortAsc = true; }
    }

    // Open employee detail view from Approvals tab
    private void ApvOpenEmployee(RangeTimesheetRow row)
    {
        apvDetailRow = row;
        currentView  = "approval-detail";
        StateHasChanged();
    }

    // Back from approval detail → return to Approvals list tab
    private void BackToApprovals()
    {
        currentView = "list";
        activeTab   = "approvals";
        StateHasChanged();
    }

    // ── Holiday helper ───────────────────────────────────────────────
    private static string GetHolidayForDate(DateTime date)
    {
        var calendars = APM.StaffZen.Blazor.Components.Pages.TimeOff.HolidaysTabState.SharedCalendars;
        if (calendars == null || !calendars.Any()) return "";
        foreach (var cal in calendars)
        {
            var holiday = cal.Holidays.FirstOrDefault(h => h.Date.Date == date.Date);
            if (holiday != null) return holiday.Name;
        }
        return "";
    }

    // ── Rest-day helper (delegates to WorkScheduleState) ────────────
    private static bool IsRestDay(DateTime date)
        => APM.StaffZen.Blazor.Services.WorkScheduleState.IsRestDay(date);

    // ── Month cell CSS helper ────────────────────────────────────────
    private static string GetMonthCellCss(int mins) => mins switch
    {
        0     => "dtl-mcell--zero",
        < 120 => "dtl-mcell--0to2",
        < 240 => "dtl-mcell--2to4",
        < 360 => "dtl-mcell--4to6",
        < 480 => "dtl-mcell--6to8",
        < 600 => "dtl-mcell--8to10",
        _     => "dtl-mcell--10plus"
    };
}
