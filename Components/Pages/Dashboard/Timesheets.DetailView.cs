using Microsoft.JSInterop;
using APM.StaffZen.Blazor.Services;

namespace APM.StaffZen.Blazor.Components.Pages.Dashboard;

public partial class Timesheets
{
    // ── Computed properties ──────────────────────────────────────────
    private static bool MinutesDiffer(DateTime? a, DateTime? b)
        => a.HasValue && b.HasValue
           && a.Value.ToString("yyyy-MM-ddTHH:mm") != b.Value.ToString("yyyy-MM-ddTHH:mm");

    private int HistoryRowCount => changeHistory.Sum(h =>
    {
        if (h.IsHourEntry)  return 1;
        if (h.IsBreakEntry) return 1;
        if (h.Action == "Added") return (h.OldClockIn != null || h.OldClockOut != null) ? 1 : 0;
        int count = 0;
        count += MinutesDiffer(h.NewClockIn,  h.OldClockIn)  ? 1 : 0;
        count += MinutesDiffer(h.NewClockOut, h.OldClockOut) ? 1 : 0;
        return count;
    });

    private IEnumerable<DateTime> WeekDays =>
        Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i));

    private string WeekRangeLabel =>
        $"{weekStart:d MMM} - {weekStart.AddDays(6):d MMM}";

    private string GetInitial()
    {
        var n = (currentView == "detail" && !string.IsNullOrEmpty(selectedEmployeeName))
                ? selectedEmployeeName
                : (SessionService.FullName ?? "U");
        return string.IsNullOrEmpty(n) ? "U" : n[0].ToString().ToUpper();
    }

    /// <summary>
    /// Builds the absolute URL for a selfie stored on the API server.
    /// The API saves selfies as "/uploads/selfie_*.jpg" — we prepend the API base URL.
    /// </summary>
    /// <summary>
    /// Builds the absolute URL for a selfie stored on the API server.
    /// Extracts just the origin (scheme + host + port) from the HttpClient BaseAddress
    /// so that even if BaseAddress has a path suffix like /api/, the selfie URL is
    /// always https://host:port/uploads/file.jpg — never nested under /api/uploads/.
    /// </summary>
    private string GetSelfieUrl(string? relativeUrl)
    {
        if (string.IsNullOrEmpty(relativeUrl)) return "";
        if (relativeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return relativeUrl;
        try
        {
            var client      = HttpClientFactory.CreateClient("API");
            var baseAddress = client.BaseAddress?.ToString() ?? "";
            if (string.IsNullOrEmpty(baseAddress)) return relativeUrl;

            // Use only the origin (scheme + host + port) — strip any /api/ path prefix
            // so selfies always resolve to https://host:port/uploads/file.jpg
            if (Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri))
                baseAddress = uri.GetLeftPart(UriPartial.Authority);

            return baseAddress.TrimEnd('/') + "/" + relativeUrl.TrimStart('/');
        }
        catch
        {
            return relativeUrl;
        }
    }

    private static string GetTimeAgo(DateTime local)
    {
        var diff = DateTime.Now - local;
        if (diff.TotalSeconds < 60) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} minute{((int)diff.TotalMinutes == 1 ? "" : "s")} ago";
        if (diff.TotalHours   < 24) return $"{(int)diff.TotalHours} hour{((int)diff.TotalHours == 1 ? "" : "s")} ago";
        return $"{(int)diff.TotalDays} day{((int)diff.TotalDays == 1 ? "" : "s")} ago";
    }

    // ── Navigation to detail view ────────────────────────────────────
    private async Task OpenDetailView(DailyTimesheetRow row)
    {
        employeeId           = row.EmployeeId;
        selectedEmployeeName = row.FullName;
        int dow              = (int)dtlSelectedDate.DayOfWeek;
        weekStart            = dtlSelectedDate.AddDays(-(dow == 0 ? 6 : dow - 1));
        selectedDate         = dtlSelectedDate;
        currentView          = "detail";
        await LoadWeekAndDay();
    }

    private async Task OpenDetailViewFromRange(RangeTimesheetRow row)
    {
        employeeId           = row.EmployeeId;
        selectedEmployeeName = row.FullName;
        selectedDate         = viewMode == "weekly" ? weekRangeFrom : monthDate;
        int dow              = (int)selectedDate.DayOfWeek;
        weekStart            = selectedDate.AddDays(-(dow == 0 ? 6 : dow - 1));
        currentView          = "detail";
        await LoadWeekAndDay();
    }

    private void BackToList() { currentView = "list"; selectedEmployeeName = ""; employeeId = SessionService.Id; ClosePanel(); _ = RefreshListAfterDetailAsync(); StateHasChanged(); }

    /// <summary>
    /// Refreshes list-view data after returning from detail view so recalculated
    /// tracked hours (from RecalcDayEntries) are visible immediately without a manual page reload.
    /// </summary>
    private async Task RefreshListAfterDetailAsync()
    {
        try
        {
            if (viewMode == "daily") await LoadDailyList();
            else                     await LoadRangeData();
            if (basePage != null) await basePage.RefreshStatusAsync();
            StateHasChanged();
        }
        catch { /* best-effort */ }
    }

    // ── Day selection & data ─────────────────────────────────────────
    private async Task SelectDay(DateTime day)
    {
        if (selectedDate.Date == day.Date) return;
        selectedDate   = day;
        entriesLoading = true;
        StateHasChanged();
        // Use GetHistoryByDateAsync so that overnight sessions (clocked in on a previous
        // day, clocked out on this day) appear as carry-over entries on THIS day — and
        // sessions that started today but end tomorrow are capped at midnight so their
        // hours are correctly split between the two days.
        dayEntries        = await AttendanceService.GetHistoryByDateAsync(employeeId, day, SessionService.ActiveOrganizationId);
        dayTrackedDisplay = ComputeTracked(dayEntries);
        await RefreshDayOTBreakdown();
        // Update the duration cache for this day so the week-bar badge stays in sync.
        // Include the live session duration so the badge reflects ongoing time.
        // For today: use current IST time. For past days with an open session: use
        // end-of-that-day (midnight) so the badge shows correct hours for that day.
        var istNow = DateTime.UtcNow.AddHours(5).AddMinutes(30);
        bool isTodayCache = day.Date == istNow.Date;
        int cacheMins = 0;
        foreach (var e in dayEntries)
        {
            if (e.IsBreakEntry) continue;
            if (e.ClockOut.HasValue)
                cacheMins += (int)(e.ClockOut.Value - e.ClockIn).TotalMinutes;
            else if (!e.IsManual && !e.IsHourEntry)
            {
                // Today → use current IST time; past day → credit up to end of that day
                var effectiveNow = isTodayCache ? istNow : day.Date.AddDays(1);
                var lm = (int)(effectiveNow - e.ClockIn).TotalMinutes;
                if (lm > 0 && lm <= 1440) cacheMins += lm;
            }
        }
        weekDayDurationCache[day.Date] = TimeSpan.FromMinutes(Math.Max(0, cacheMins));
        await BuildChangeHistory();
        trackedExpanded = payrollExpanded = historyExpanded = false;
        entriesLoading  = false;
        StateHasChanged();
    }

    private async Task PrevWeek() { weekStart = weekStart.AddDays(-7); selectedDate = weekStart; await LoadWeekAndDay(); }
    private async Task NextWeek() { weekStart = weekStart.AddDays(7);  selectedDate = weekStart; await LoadWeekAndDay(); }

    private async Task LoadWeekAndDay()
    {
        if (employeeId == 0) return;
        entriesLoading = true; StateHasChanged();
        weekEntries        = await AttendanceService.GetHistoryByWeekAsync(employeeId, weekStart, SessionService.ActiveOrganizationId);
        weekTrackedDisplay = ComputeTracked(weekEntries);

        // Pre-populate the per-day duration cache for all 7 days in the current week.
        // We fetch all days in parallel so the week-bar badges reflect overnight carry-overs
        // correctly (e.g. clocked in Apr 1 evening, clocked out Apr 2 morning: Apr 1 shows
        // 6h 9m and Apr 2 shows 11h 40m, not 17h 49m on Apr 1 and 0 on Apr 2).
        weekDayDurationCache.Clear();
        var weekDays = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        // Fix: include live (ongoing) sessions so the week bar shows correct hours
        // even when the employee is currently clocked in (ClockOut == null).
        // For today: use IST current time. For past days with an open session
        // (e.g. clocked in Thu, no clock-out yet on Fri): use end-of-that-day (midnight)
        // so Thu's bar shows the full hours worked on Thu rather than 0h 0m.
        var fetchTasks = weekDays.Select(async d =>
        {
            var entries = await AttendanceService.GetHistoryByDateAsync(employeeId, d, SessionService.ActiveOrganizationId);
            if (!entries.Any()) return (day: d.Date, span: TimeSpan.Zero);
            int totalMins = 0;
            // Use IST "now" — DB stores ClockIn as IST local time (DateTimeKind.Unspecified).
            // DateTime.Now on a UTC server would be 5h30m behind, making live durations negative.
            var istNow   = DateTime.UtcNow.AddHours(5).AddMinutes(30);
            bool isToday  = d.Date == istNow.Date;
            foreach (var e in entries)
            {
                if (e.IsBreakEntry) continue;
                if (e.ClockOut.HasValue)
                {
                    totalMins += (int)(e.ClockOut.Value - e.ClockIn).TotalMinutes;
                }
                else if (!e.IsManual && !e.IsHourEntry)
                {
                    // Live / still-open session.
                    // Today  → use current IST time as effective clock-out.
                    // Past day → the session crossed midnight; credit hours up to end of that day
                    //            so the week-bar badge shows the correct amount for that day.
                    var effectiveNow = isToday ? istNow : d.Date.AddDays(1);
                    var liveMins = (int)(effectiveNow - e.ClockIn).TotalMinutes;
                    if (liveMins > 0 && liveMins <= 1440) totalMins += liveMins;
                }
            }
            return (day: d.Date, span: TimeSpan.FromMinutes(Math.Max(0, totalMins)));
        });
        var daySpans = await Task.WhenAll(fetchTasks);
        foreach (var (day, span) in daySpans)
            weekDayDurationCache[day] = span;

        // Use GetHistoryByDateAsync for the selected day so overnight sessions are handled
        // correctly: a session clocked in on a previous day but clocked out today appears
        // on today's list (as a carry-over with clockIn shown as midnight), and its hours
        // are computed only for the today-portion.
        dayEntries         = await AttendanceService.GetHistoryByDateAsync(employeeId, selectedDate, SessionService.ActiveOrganizationId);
        dayTrackedDisplay  = ComputeTracked(dayEntries);
        await RefreshDayOTBreakdown();
        await BuildChangeHistory();
        entriesLoading = false; StateHasChanged();
    }

    private async Task BuildChangeHistory()
    {
        changeHistoryError = "";
        try
        {
            var logs = await AttendanceService.GetChangeLogByDateAsync(employeeId, selectedDate);
            changeHistory = logs.Select(l => new ChangeHistoryEntry
            {
                ChangedByName   = l.ChangedByName,
                Action          = l.Action,
                OldClockIn      = l.OldClockIn,
                OldClockOut     = l.OldClockOut,
                NewClockIn      = l.NewClockIn,
                NewClockOut     = l.NewClockOut,
                ReasonForChange = l.ReasonForChange,
                ChangedAt       = l.ChangedAt
            }).ToList();
        }
        catch (Exception ex) { changeHistoryError = ex.Message; changeHistory = new(); }
    }

    // ── Panel: View / Edit flow ──────────────────────────────────────
    private void OpenView(TimeEntryRecord entry, string type)
    {
        viewEntry = entry; panelType = type; panelMode = "view";
        editClockInTime  = entry.ClockIn.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        editClockInDate  = entry.ClockIn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var refOut       = entry.ClockOut ?? DateTime.Now;
        editClockOutTime = refOut.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        editClockOutDate = refOut.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        editNote = ""; editError = ""; editLocationName = ""; reasonForChange = ""; showReasonModal = false;
        // Show location name in view panel (live entries default to Head Office; manual entries have no location)
        viewEntryLocation = entry.IsManual ? "" : "Head Office";
        StateHasChanged();
    }

    private void SwitchToEdit()
    {
        panelMode = "edit"; editError = ""; showCalendar = false; showTimePicker = false;
        var dateStr = panelType == "in" ? editClockInDate : editClockOutDate;
        calViewDate = DateTime.TryParse(dateStr, out var dt) ? dt : DateTime.Today;
        StateHasChanged();
    }

    // ── Calendar in edit panel ───────────────────────────────────────
    private void ToggleCalendar()
    {
        showTimePicker = false; showCalendar = !showCalendar;
        if (showCalendar)
        {
            var dateStr = panelType == "in" ? editClockInDate : editClockOutDate;
            calViewDate = DateTime.TryParse(dateStr, out var dt) ? dt : DateTime.Today;
        }
    }

    private void CalPrevMonth() => calViewDate = calViewDate.AddMonths(-1);
    private void CalNextMonth() => calViewDate = calViewDate.AddMonths(1);
    private void CalPrevYear()  => calViewDate = calViewDate.AddYears(-1);
    private void CalNextYear()  => calViewDate = calViewDate.AddYears(1);

    private void CalSelectDate(DateTime date)
    {
        var formatted = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (panelType == "in") editClockInDate = formatted; else editClockOutDate = formatted;
        showCalendar = false; StateHasChanged();
    }

    // ── Time picker in edit panel ────────────────────────────────────
    private void ToggleTimePicker()
    {
        showCalendar = false; showTimePicker = !showTimePicker;
        if (showTimePicker) InitTimePicker();
    }

    private async void InitTimePicker()
    {
        var timeStr = panelType == "in" ? editClockInTime : editClockOutTime;
        if (TimeSpan.TryParse(timeStr, out var ts))
        {
            int h24 = ts.Hours;
            tpAmPm = h24 >= 12 ? "PM" : "AM";
            tpHour = h24 % 12; if (tpHour == 0) tpHour = 12;
            tpMinute = ts.Minutes;
        }
        else { tpHour = 12; tpMinute = 0; tpAmPm = "AM"; }
        StateHasChanged();
        await Task.Delay(50);
        try
        {
            await JS.InvokeVoidAsync("eval",
                $"(function(){{" +
                $"var hEl=document.getElementById('tp-h-{tpHour:D2}');" +
                $"if(hEl)hEl.scrollIntoView({{block:'center'}});" +
                $"var mEl=document.getElementById('tp-m-{tpMinute:D2}');" +
                $"if(mEl)mEl.scrollIntoView({{block:'center'}});" +
                $"}})()");
        }
        catch { }
    }

    private void ApplyTimePicker()
    {
        int h24    = tpAmPm == "PM" ? (tpHour % 12) + 12 : tpHour % 12;
        var result = $"{h24:D2}:{tpMinute:D2}";
        if (panelType == "in") editClockInTime = result; else editClockOutTime = result;
        StateHasChanged();
    }

    private string GetDisplayTime()
    {
        var timeStr = panelType == "in" ? editClockInTime : editClockOutTime;
        if (TimeSpan.TryParse(timeStr, out var ts))
        {
            int h = ts.Hours % 12; if (h == 0) h = 12;
            return $"{h}:{ts.Minutes:D2} {(ts.Hours >= 12 ? "PM" : "AM")}";
        }
        return "--:-- AM";
    }

    private string GetViewTime()
    {
        if (viewEntry == null) return "";
        return panelType == "in"
            ? viewEntry.ClockIn.ToString("h:mm tt").ToLower()
            : (viewEntry.ClockOut?.ToString("h:mm tt").ToLower() ?? "—");
    }

    private string GetViewDateLabel()
    {
        if (viewEntry == null) return "";
        var dt = panelType == "in" ? viewEntry.ClockIn : (viewEntry.ClockOut ?? viewEntry.ClockIn);
        if (dt.Date == DateTime.Today)              return "Today";
        if (dt.Date == DateTime.Today.AddDays(-1))  return "Yesterday";
        return dt.ToString("d MMM yyyy");
    }

    private string GetEditDateLabel()
    {
        var dateStr = panelType == "in" ? editClockInDate : editClockOutDate;
        if (string.IsNullOrEmpty(dateStr)) return "Today";
        if (DateTime.TryParse(dateStr, out var dt))
        {
            if (dt.Date == DateTime.Today)             return "Today";
            if (dt.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
            return dt.ToString("d MMM yyyy");
        }
        return dateStr;
    }

    private void ClosePanel()
    {
        viewEntry       = null; panelMode = "none"; editError = "";
        showCalendar    = showTimePicker = showReasonModal = false;
        tepSelfieOpen   = false;  // close lightbox if panel is dismissed
        tepSelfieZoomUrl = "";
        StateHasChanged();
    }

    private void CancelReasonModal() { showReasonModal = false; }

    private void OnSaveClicked()
    {
        if (viewEntry == null) return;
        editError = ""; reasonForChange = ""; showReasonModal = true;
    }

    private async Task ConfirmSaveWithReason()
    {
        if (viewEntry == null) return;
        editSaving = true; showReasonModal = false; StateHasChanged();
        try
        {
            static DateTime ParseLocalDT(string date, string time)
            {
                var ic       = System.Globalization.CultureInfo.InvariantCulture;
                var timePart = time.Length > 5 ? time.Substring(0, 5) : time;
                if (!DateTime.TryParseExact($"{date}T{timePart}", "yyyy-MM-ddTHH:mm", ic,
                        System.Globalization.DateTimeStyles.None, out var dt))
                    throw new FormatException($"Cannot parse date='{date}' time='{time}'");
                return DateTime.SpecifyKind(dt, DateTimeKind.Local);
            }

            var newClockInLocal = panelType == "in" ? ParseLocalDT(editClockInDate, editClockInTime) : viewEntry.ClockIn;
            var newClockInStr   = newClockInLocal.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            string? newClockOutStr = null;
            if (viewEntry.ClockOut.HasValue)
            {
                var newClockOutLocal = panelType == "out" ? ParseLocalDT(editClockOutDate, editClockOutTime) : viewEntry.ClockOut.Value;
                newClockOutStr = newClockOutLocal.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            }

            var (updated, apiError) = await AttendanceService.UpdateEntryAsync(
                viewEntry.Id, employeeId, newClockInStr, newClockOutStr,
                string.IsNullOrWhiteSpace(reasonForChange) ? null : reasonForChange, panelType);

            if (updated != null)
            {
                ClosePanel();
                await LoadWeekAndDay();
                // Keep list-view data fresh in background for when user navigates back.
                _ = RefreshListAfterDetailAsync();
            }
            else { editError = $"Save failed — {apiError ?? "please try again."}"; panelMode = "edit"; }
        }
        catch (Exception ex) { editError = $"Error: {ex.Message}"; panelMode = "edit"; }
        finally { editSaving = false; StateHasChanged(); }
    }

    // ── Delete flow ──────────────────────────────────────────────────
    private void ConfirmDelete(TimeEntryRecord entry) { deleteTargetEntry = entry; deleteError = ""; showDeleteModal = true; }
    private void ConfirmDeleteFromPanel() { if (viewEntry != null) ConfirmDelete(viewEntry); }
    private void CancelDelete() { showDeleteModal = false; deleteTargetEntry = null; deleteError = ""; }

    private async Task ExecuteDelete()
    {
        if (deleteTargetEntry == null) return;
        deleteInProgress = true; deleteError = ""; StateHasChanged();
        var ok = await AttendanceService.DeleteEntryAsync(deleteTargetEntry.Id, employeeId);
        if (ok)
        {
            showDeleteModal = false; deleteTargetEntry = null;
            await LoadWeekAndDay();
            // Refresh list-view data in background so the timesheet table is
            // up-to-date when the user navigates back (recalculated hours visible immediately).
            _ = RefreshListAfterDetailAsync();
        }
        else { deleteError = "Delete failed — please try again."; }
        deleteInProgress = false; StateHasChanged();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the scheduled start TimeSpan for a given date using WorkScheduleState DaySlots.
    /// Returns null if not configured (means no clipping).
    /// </summary>
    private static TimeSpan? GetScheduledStart(DateTime date)
    {
        var schedule = WorkScheduleState.Schedules.FirstOrDefault(s => s.IsDefault && s.IsSaved)
                    ?? WorkScheduleState.Schedules.FirstOrDefault(s => s.IsSaved);
        if (schedule == null || schedule.Arrangement != "Fixed") return null;
        if (schedule.IncludeBeforeStart) return null; // checkbox checked → no clipping

        string dayKey = date.DayOfWeek switch
        {
            DayOfWeek.Monday    => "Mon",
            DayOfWeek.Tuesday   => "Tue",
            DayOfWeek.Wednesday => "Wed",
            DayOfWeek.Thursday  => "Thu",
            DayOfWeek.Friday    => "Fri",
            DayOfWeek.Saturday  => "Sat",
            DayOfWeek.Sunday    => "Sun",
            _                   => ""
        };
        if (schedule.DaySlots.TryGetValue(dayKey, out var slot)
            && TimeSpan.TryParse(slot.Start, out var ts))
            return ts;
        return null;
    }

    /// <summary>
    /// Resolves the split boundary for a given calendar date using WorkScheduleState.SplitAt.
    /// Returns (dayStart, dayEnd) as the window that "belongs" to that date.
    /// Default split = midnight → (date 00:00, date+1 00:00).
    /// </summary>
    private static (DateTime dayStart, DateTime dayEnd) GetSplitWindow(DateTime date)
    {
        var schedule = WorkScheduleState.Schedules.FirstOrDefault(s => s.IsDefault && s.IsSaved)
                    ?? WorkScheduleState.Schedules.FirstOrDefault(s => s.IsSaved);

        TimeSpan split = TimeSpan.Zero;
        if (schedule != null && !string.IsNullOrWhiteSpace(schedule.SplitAt))
            TimeSpan.TryParse(schedule.SplitAt, out split);

        var dayStart = date.Date + split;
        return (dayStart, dayStart.AddDays(1));
    }

    private TimeSpan DayDuration(DateTime day)
    {
        // Use the per-day cache populated by LoadWeekAndDay() via GetHistoryByDateAsync.
        // This correctly accounts for overnight carry-overs (sessions that started on a
        // previous day but clocked out on this day).
        if (weekDayDurationCache.TryGetValue(day.Date, out var cached))
            return cached;

        // Fallback: compute from weekEntries (no overnight awareness — used only before
        // the cache is populated, e.g. during the very first render tick).
        var (dayStart, dayEnd) = GetSplitWindow(day);
        return weekEntries
            .Where(e => e.ClockIn >= dayStart && e.ClockIn < dayEnd && e.ClockOut != null)
            .Aggregate(TimeSpan.Zero, (acc, e) =>
            {
                var effOut = (!e.IsManual && !e.IsHourEntry && e.ClockOut!.Value > dayEnd)
                    ? dayEnd : e.ClockOut!.Value;
                return acc + (effOut - e.ClockIn);
            });
    }

    /// <summary>
    /// Computes tracked hours for a list of entries (may span multiple days — e.g. full week).
    /// Groups by calendar day and applies per-day schedule rules:
    ///   - Split-time cap  (sessions crossing the day boundary are capped at dayEnd)
    ///   - IncludeBeforeStart  (Fixed schedules only — clips ClockIn to scheduledStart)
    ///   - Auto-deductions  (applied per-day after computing raw minutes)
    /// Hour entries bypass all schedule rules.
    /// Break entries are excluded entirely.
    /// </summary>
    private static string ComputeTracked(List<TimeEntryRecord> src)
    {
        if (!src.Any()) return "0h 0m";

        // Group entries by calendar date so each day is computed independently.
        // For a single-day call (dayEntries) there will be exactly one group.
        var byDay = src
            .Where(e => !e.IsBreakEntry)
            .GroupBy(e => e.ClockIn.Date)
            .OrderBy(g => g.Key);

        var schedule = WorkScheduleState.Schedules.FirstOrDefault(s => s.IsDefault && s.IsSaved)
                    ?? WorkScheduleState.Schedules.FirstOrDefault(s => s.IsSaved);

        int grandTotal = 0;

        foreach (var dayGroup in byDay)
        {
            var refDate   = dayGroup.Key;
            var (dayStart, dayEnd) = GetSplitWindow(refDate);
            TimeSpan? scheduledStart = GetScheduledStart(refDate);

            int dayMins = 0;

            foreach (var e in dayGroup)
            {
                // Hour entries — add directly, no schedule rules
                if (e.IsHourEntry)
                {
                    if (e.ClockOut.HasValue)
                        dayMins += Math.Max(0, (int)(e.ClockOut.Value - e.ClockIn).TotalMinutes);
                    continue;
                }

                // Live (open) session
                if (!e.ClockOut.HasValue)
                {
                    if (!e.IsManual)
                    {
                        DateTime liveStart = e.ClockIn < dayStart ? dayStart : e.ClockIn;
                        if (scheduledStart.HasValue)
                        {
                            var schedStart = refDate + scheduledStart.Value;
                            if (liveStart < schedStart) liveStart = schedStart;
                        }
                        // Use IST "now" — ClockIn is stored as IST local (DateTimeKind.Unspecified).
                        // DateTime.Now on a UTC server would be 5h30m behind, making live durations negative.
                        var istNow   = DateTime.UtcNow.AddHours(5).AddMinutes(30);
                        // For today: use current IST time as effective end.
                        // For a past day with no clock-out yet: credit hours up to end of
                        // that day (midnight) so we show only that day's contribution, not
                        // the full elapsed time since then (which would inflate the total).
                        bool isLiveToday = refDate.Date == istNow.Date;
                        var effectiveEnd = isLiveToday ? istNow : dayEnd;
                        var liveMins = (int)(effectiveEnd - liveStart).TotalMinutes;
                        if (liveMins < 0)     liveMins = 0;
                        if (liveMins > 1440)  liveMins = 1440;
                        dayMins += liveMins;
                    }
                    continue;
                }

                // Closed real session — cap at split boundary
                DateTime effOut = (!e.IsManual && e.ClockOut.Value > dayEnd) ? dayEnd : e.ClockOut.Value;
                DateTime effIn  = e.ClockIn;

                if (!e.IsManual && scheduledStart.HasValue)
                {
                    var schedStart = refDate + scheduledStart.Value;
                    if (effIn < schedStart) effIn = schedStart;
                }

                var mins = (int)(effOut - effIn).TotalMinutes;
                if (mins > 0) dayMins += mins;
            }

            // Apply auto-deductions per day
            if (schedule != null)
                foreach (var ded in schedule.AutoDeductions)
                {
                    int threshMins = (int)(ded.AfterHours * 60);
                    if (dayMins > threshMins)
                        dayMins = Math.Max(0, dayMins - ded.DeductMinutes);
                }

            grandTotal += dayMins;
        }

        return FmtSpan(TimeSpan.FromMinutes(grandTotal));
    }

    private static string FmtSpan(TimeSpan ts)
    {
        if (ts <= TimeSpan.Zero) return "0h 0m";
        int h = (int)ts.TotalHours, m = ts.Minutes;
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }

    /// <summary>
    /// Fetches the daily timesheet row for the current employee on selectedDate
    /// and populates dayRegularDisplay, dayOvertimeDisplay, dayDoubleOTDisplay,
    /// dayWeeklyOTDisplay, dayRestDayOTDisplay, dayPublicHolOTDisplay.
    /// This gives the sidebar accurate Regular / Overtime breakdown values.
    /// </summary>
    private async Task RefreshDayOTBreakdown()
    {
        try
        {
            var rows = await AttendanceService.GetDailyTimesheetAsync(selectedDate, SessionService.ActiveOrganizationId);
            var row  = rows.FirstOrDefault(r => r.EmployeeId == employeeId);
            if (row == null)
            {
                dayRegularDisplay      = dayTrackedDisplay;
                dayOvertimeDisplay     = null;
                dayDoubleOTDisplay     = null;
                dayWeeklyOTDisplay     = null;
                dayRestDayOTDisplay    = null;
                dayPublicHolOTDisplay  = null;
                return;
            }
            dayRegularDisplay     = string.IsNullOrWhiteSpace(row.RegularHours) || row.RegularHours == "—"
                                        ? dayTrackedDisplay : row.RegularHours;
            dayOvertimeDisplay    = row.HasDailyOT     ? (row.OvertimeHours          ?? "0h 0m") : null;
            dayDoubleOTDisplay    = row.HasDailyDblOT  ? (row.DailyDoubleOTHours     ?? "0h 0m") : null;
            dayWeeklyOTDisplay    = row.HasWeeklyOT    ? (row.WeeklyOvertimeHours    ?? "0h 0m") : null;
            dayRestDayOTDisplay   = row.HasRestDayOT   ? (row.RestDayOvertimeHours   ?? "0h 0m") : null;
            dayPublicHolOTDisplay = row.HasPublicHolOT ? (row.PublicHolOvertimeHours ?? "0h 0m") : null;
        }
        catch
        {
            dayRegularDisplay     = dayTrackedDisplay;
            dayOvertimeDisplay    = null;
            dayDoubleOTDisplay    = null;
            dayWeeklyOTDisplay    = null;
            dayRestDayOTDisplay   = null;
            dayPublicHolOTDisplay = null;
        }
    }
}
