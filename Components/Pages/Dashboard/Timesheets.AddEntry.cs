using Microsoft.JSInterop;
using APM.StaffZen.Blazor.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;

namespace APM.StaffZen.Blazor.Components.Pages.Dashboard;

public partial class Timesheets
{
    // ── Add Time Entry panel state ───────────────────────────────────
    private bool     showAddTimeEntry = false;
    private string   addEntryTab      = "time";
    private DateTime addEntryDate     = DateTime.Today;

    private class AddEntryBlock
    {
        public string    EntryType     = "in";
        public TimeOnly? TimeValue     = null;
        public TimeOnly? BreakStart    = null;
        public TimeOnly? BreakEnd      = null;
        public string    BreakType     = "Lunch break";
        public bool      EndWorkForDay = false;
        public string    Activity      = "";
        public string    Project       = "";
        public string    Note          = "";
        public int       HourH         = 0;
        public int       HourM         = 0;
        public string    HourNote      = "";
        public string    HourActivity  = "";
        public string    HourProject   = "";
    }

    private bool IsLiveSessionActive =>
        dayEntries.Any(e => !e.IsManual && !e.IsHourEntry && e.ClockOut == null
                         && e.ClockIn.ToLocalTime().Date == DateTime.Today);

    private string? GetBlockError(int blockIndex)
    {
        var blk = addEntryBlocks[blockIndex];
        if (blk.EntryType == "break" || !blk.TimeValue.HasValue) return null;

        var isToday = addEntryDate.Date == DateTime.Today;
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        // Rule 0: duplicate time — only flag non-first blocks
        if (blockIndex > 0)
        {
            for (int i = 0; i < addEntryBlocks.Count; i++)
            {
                if (i == blockIndex) continue;
                var other = addEntryBlocks[i];
                if (other.TimeValue.HasValue && other.TimeValue.Value == blk.TimeValue.Value
                    && other.EntryType != "break" && blk.EntryType != "break")
                    return "Duplicate time — please use a different time for each entry.";
            }
        }

        // Rule 1: time in the future when date is today
        if (isToday && blk.TimeValue.Value > nowTime.Add(TimeSpan.FromMinutes(2)))
            return "Time can't be in the future.";

        // Rule 2: clock-out must be after clock-in
        if (blk.EntryType == "out")
        {
            var outTime = blk.TimeValue.Value;
            for (int i = blockIndex - 1; i >= 0; i--)
            {
                if (addEntryBlocks[i].EntryType == "in" && addEntryBlocks[i].TimeValue.HasValue)
                {
                    if (outTime <= addEntryBlocks[i].TimeValue!.Value)
                        return "Clock out time can't be before or equal to clock in time.";
                    return null;
                }
            }
            var lastOpenIn = dayEntries
                .Where(e => !e.IsHourEntry && e.IsManual && e.ClockOut == null
                         && e.ClockIn.ToLocalTime().Date == addEntryDate.Date)
                .OrderByDescending(e => e.ClockIn)
                .FirstOrDefault();
            if (lastOpenIn != null)
            {
                var existingInTime = TimeOnly.FromDateTime(lastOpenIn.ClockIn.ToLocalTime());
                if (outTime <= existingInTime)
                    return "Clock out time can't be before or equal to clock in time.";
            }
        }
        return null;
    }

    private bool HasDuplicateHourEntryError()
    {
        if (addEntryTab != "hour") return false;
        for (int i = 0; i < addEntryBlocks.Count; i++)
            for (int j = i + 1; j < addEntryBlocks.Count; j++)
                if (addEntryBlocks[i].HourH == addEntryBlocks[j].HourH &&
                    addEntryBlocks[i].HourM == addEntryBlocks[j].HourM)
                    return true;
        return false;
    }

    private bool HasClockOutBeforeClockInError() =>
        Enumerable.Range(0, addEntryBlocks.Count).Any(i => GetBlockError(i) != null)
        || HasDuplicateHourEntryError();

    private bool IsOutBlockInvalid(int blockIndex) => GetBlockError(blockIndex) != null;

    private List<AddEntryBlock> addEntryBlocks = new() { new AddEntryBlock { TimeValue = TimeOnly.FromDateTime(DateTime.Now) } };
    private bool   addEntrySaving   = false;
    private string addEntrySaveError = "";

    // ── Custom picker state ──────────────────────────────────────────
    private int    amtTimePickerKey = -1;
    private int    amtSegFocus  = -1;
    private string amtSegBuf    = "";
    private int    hourSegFocus    = -1;
    private int    hourBoxFocusIdx = -1;
    private string hourSegBuf      = "";
    private int    amtDatePickerIdx = -1;
    private int    tpH  = 12;
    private int    tpM  = 0;
    private string tpAp = "am";
    private int    amtCalYear  = DateTime.Today.Year;
    private int    amtCalMonth = DateTime.Today.Month;

    private HashSet<int> collapsedBlocks = new();

    // ── Block management ─────────────────────────────────────────────
    private void ToggleCollapseBlock(int idx)
    {
        if (collapsedBlocks.Contains(idx)) collapsedBlocks.Remove(idx);
        else collapsedBlocks.Add(idx);
        StateHasChanged();
    }

    private void AddNewBlockAfter(int afterIdx)
    {
        addEntryBlocks.Insert(afterIdx + 1, new AddEntryBlock { EntryType = "in", TimeValue = addEntryBlocks[0].TimeValue });
        var shifted = collapsedBlocks.Where(i => i > afterIdx).ToList();
        foreach (var i in shifted) { collapsedBlocks.Remove(i); collapsedBlocks.Add(i + 1); }
        StateHasChanged();
    }

    private void AddNewBlock() => AddNewBlockAfter(addEntryBlocks.Count - 1);

    private void DuplicateBlock(int idx)
    {
        var src = addEntryBlocks[idx];
        addEntryBlocks.Insert(idx + 1, new AddEntryBlock
        {
            EntryType    = src.EntryType,
            TimeValue    = src.TimeValue,
            BreakStart   = src.BreakStart,
            BreakEnd     = src.BreakEnd,
            BreakType    = src.BreakType,
            Activity     = src.Activity,
            Project      = src.Project,
            Note         = src.Note,
            HourH        = src.HourH,
            HourM        = src.HourM,
            HourActivity = src.HourActivity,
            HourProject  = src.HourProject,
            HourNote     = src.HourNote
        });
        var shifted = collapsedBlocks.Where(i => i > idx).ToList();
        foreach (var i in shifted) { collapsedBlocks.Remove(i); collapsedBlocks.Add(i + 1); }
        StateHasChanged();
    }

    private void RemoveBlock(int idx)
    {
        if (addEntryBlocks.Count <= 1) return;
        addEntryBlocks.RemoveAt(idx);
        collapsedBlocks.Remove(idx);
        var shifted = collapsedBlocks.Where(i => i > idx).ToList();
        foreach (var i in shifted) { collapsedBlocks.Remove(i); collapsedBlocks.Add(i - 1); }
        StateHasChanged();
    }

    // ── Tab switching ────────────────────────────────────────────────
    private void SwitchToHourTab()
    {
        addEntryTab = "hour";
        addEntryBlocks = new() { new AddEntryBlock() };
        collapsedBlocks.Clear();
        hourSegFocus = -1; hourBoxFocusIdx = -1; hourSegBuf = "";
        StateHasChanged();
    }

    private void SwitchToTimeTab()
    {
        addEntryTab = "time";
        addEntryBlocks = new() { new AddEntryBlock { TimeValue = TimeOnly.FromDateTime(DateTime.Now) } };
        collapsedBlocks.Clear();
        amtSegFocus = -1; amtSegBuf = ""; amtTimePickerKey = -1;
        StateHasChanged();
    }

    // ── Open / Close panel ───────────────────────────────────────────
    private void OpenAddTimeEntry()
    {
        addEntryTab = "time"; addEntryDate = selectedDate;
        addEntryBlocks = new() { new AddEntryBlock { TimeValue = TimeOnly.FromDateTime(DateTime.Now) } };
        collapsedBlocks.Clear();
        showAddTimeEntry = true;
    }

    private void OpenAddTimeEntryForDate(DateTime date)
    {
        addEntryTab = "time"; addEntryDate = date;
        addEntryBlocks = new() { new AddEntryBlock { TimeValue = TimeOnly.FromDateTime(DateTime.Now) } };
        collapsedBlocks.Clear();
        showAddTimeEntry = true;
    }

    private void CloseAddTimeEntry()
    {
        showAddTimeEntry = false; addEntrySaving = false; addEntrySaveError = "";
        amtTimePickerKey = -1; amtDatePickerIdx = -1; amtSegFocus = -1; amtSegBuf = "";
        hourSegFocus = -1; hourBoxFocusIdx = -1; hourSegBuf = "";
        collapsedBlocks.Clear();
    }

    // ── Save ─────────────────────────────────────────────────────────
    private async Task SaveManualEntries()
    {
        addEntrySaving    = true;
        addEntrySaveError = "";
        StateHasChanged();

        var dateStr  = addEntryDate.ToString("yyyy-MM-dd");
        bool anyFail = false;
        var  errors  = new System.Text.StringBuilder();

        int orgId       = SessionService.ActiveOrganizationId ?? 0;
        int requesterId = SessionService.Id;
        // Admin override: allow back-dating clock-in over a live session
        bool isAdminOverride = SessionService.CanManageActiveOrg;

        foreach (var blk in addEntryBlocks)
        {
            bool ok; string failMsg = "";

            if (addEntryTab == "hour")
            {
                (ok, var errMsg) = await AttendanceService.AddManualEntryAsync(employeeId, "hour", dateStr, null, blk.HourH, blk.HourM, orgId, requesterId, isAdminOverride);
                if (!ok) failMsg = errMsg ?? "Failed to save hour entry.";
            }
            else if (blk.EntryType == "in" && blk.TimeValue.HasValue)
            {
                (ok, var errMsg) = await AttendanceService.AddManualEntryAsync(employeeId, "in", dateStr, blk.TimeValue.Value.ToString("HH:mm"), organizationId: orgId, requesterId: requesterId, isAdminOverride: isAdminOverride);
                if (!ok) failMsg = errMsg ?? $"Failed to save clock-in at {blk.TimeValue.Value.ToString("h:mm tt").ToLower()}.";
            }
            else if (blk.EntryType == "out" && blk.TimeValue.HasValue)
            {
                (ok, var errMsg) = await AttendanceService.AddManualEntryAsync(employeeId, "out", dateStr, blk.TimeValue.Value.ToString("HH:mm"), organizationId: orgId, requesterId: requesterId, isAdminOverride: isAdminOverride);
                if (!ok) failMsg = errMsg ?? $"Failed to save clock-out at {blk.TimeValue.Value.ToString("h:mm tt").ToLower()}.";
            }
            else if (blk.EntryType == "break" && blk.BreakStart.HasValue)
            {
                (ok, var errMsg) = await AttendanceService.AddManualEntryAsync(employeeId, "break", dateStr, blk.BreakStart.Value.ToString("HH:mm"), organizationId: orgId, requesterId: requesterId, isAdminOverride: isAdminOverride);
                if (!ok) { failMsg = errMsg ?? "Failed to save break entry."; }
                else if (blk.BreakEnd.HasValue)
                {
                    (var ok2, var errMsg2) = await AttendanceService.AddManualEntryAsync(employeeId, "in", dateStr, blk.BreakEnd.Value.ToString("HH:mm"), organizationId: orgId, requesterId: requesterId, isAdminOverride: isAdminOverride);
                    if (!ok2) failMsg = errMsg2 ?? "Break saved but failed to create clock-in after break.";
                }
            }
            else { continue; }

            if (!string.IsNullOrEmpty(failMsg))
            {
                anyFail = true;
                if (errors.Length > 0) errors.Append(" ");
                errors.Append(failMsg);
            }
        }

        addEntrySaving = false;

        if (anyFail)
        {
            addEntrySaveError = errors.ToString();
            StateHasChanged();
            return;
        }

        showAddTimeEntry = false;

        // Always sync the list-view date to the entry date so the reload fetches the right data
        dtlSelectedDate = addEntryDate;

        if (currentView == "detail")
        {
            // In detail view: refresh the week/day entries panel
            selectedDate = addEntryDate;
            int dow = (int)selectedDate.DayOfWeek;
            weekStart = selectedDate.AddDays(-(dow == 0 ? 6 : dow - 1));
            await LoadWeekAndDay();
        }

        // Always refresh the list view data so timesheets table updates immediately
        if (viewMode == "daily")
            await LoadDailyList();
        else
            await LoadRangeData();

        if (basePage != null) await basePage.RefreshStatusAsync();
        StateHasChanged();
    }

    // ── Hour segment helpers ─────────────────────────────────────────
    private string GetHourBoxClass(int bIdx) =>
        hourBoxFocusIdx == bIdx ? "amt-hour-box amt-hour-box--active" : "amt-hour-box";

    private string GetHourSegClass(int bIdx, int seg) =>
        hourSegFocus == bIdx * 10 + seg ? "amt-hour-seg amt-hour-seg--on" : "amt-hour-seg";

    private void FocusHourSeg(int bIdx, int seg)
    {
        hourSegFocus = bIdx * 10 + seg; hourBoxFocusIdx = bIdx; hourSegBuf = "";
        StateHasChanged();
    }

    private void BlurHourSeg(int bIdx, int seg)
    {
        if (hourSegFocus == bIdx * 10 + seg) { hourSegFocus = -1; hourBoxFocusIdx = -1; }
        StateHasChanged();
    }

    private void HandleHourSegKey(KeyboardEventArgs e, AddEntryBlock blk, int bIdx, int seg)
    {
        if (e.Key == "Tab" || e.Key == "Escape") { hourSegFocus = -1; hourBoxFocusIdx = -1; hourSegBuf = ""; StateHasChanged(); return; }
        if (e.Key == "ArrowRight" || e.Key == "ArrowLeft")
        {
            hourSegFocus = e.Key == "ArrowRight" ? (seg == 1 ? bIdx*10+2 : bIdx*10+1) : (seg == 2 ? bIdx*10+1 : bIdx*10+2);
            hourSegBuf = ""; StateHasChanged(); return;
        }
        if (e.Key == "ArrowUp" || e.Key == "ArrowDown")
        {
            int delta = e.Key == "ArrowUp" ? 1 : -1;
            if (seg == 1) blk.HourH = Math.Max(0, blk.HourH + delta);
            else          blk.HourM = Math.Clamp(blk.HourM + delta, 0, 59);
            hourSegBuf = ""; StateHasChanged(); return;
        }
        if (e.Key == "Backspace") { if (seg == 1) blk.HourH = 0; else blk.HourM = 0; hourSegBuf = ""; StateHasChanged(); return; }
        if (e.Key.Length == 1 && char.IsDigit(e.Key[0]))
        {
            hourSegBuf += e.Key;
            int val = int.Parse(hourSegBuf);
            if (seg == 1) { blk.HourH = Math.Max(0, val); if (hourSegBuf.Length >= 2) { hourSegBuf = ""; hourSegFocus = bIdx*10+2; } }
            else          { blk.HourM = Math.Clamp(val, 0, 59); if (hourSegBuf.Length >= 2 || val >= 6) { hourSegBuf = ""; hourSegFocus = bIdx*10+2; } }
            StateHasChanged();
        }
    }

    private void OnHourHInput(Microsoft.AspNetCore.Components.ChangeEventArgs e, AddEntryBlock blk)
    {
        var raw    = e.Value?.ToString() ?? "";
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length > 2) digits = digits.Substring(digits.Length - 2);
        blk.HourH = digits.Length > 0 ? Math.Max(0, int.Parse(digits)) : 0;
        StateHasChanged();
    }

    private void OnHourMInput(Microsoft.AspNetCore.Components.ChangeEventArgs e, AddEntryBlock blk)
    {
        var raw    = e.Value?.ToString() ?? "";
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length > 2) digits = digits.Substring(digits.Length - 2);
        blk.HourM = digits.Length > 0 ? Math.Clamp(int.Parse(digits), 0, 59) : 0;
        StateHasChanged();
    }

    // ── Time picker helpers ──────────────────────────────────────────
    private string FmtTime(TimeOnly? t)
    {
        if (!t.HasValue) return "";
        int h = t.Value.Hour % 12; if (h == 0) h = 12;
        return $"{h}:{t.Value.Minute:D2} {(t.Value.Hour >= 12 ? "pm" : "am")}";
    }

    private void LoadPicker(TimeOnly? t)
    {
        if (t.HasValue)
        {
            tpAp = t.Value.Hour >= 12 ? "pm" : "am";
            tpH  = t.Value.Hour % 12; if (tpH == 0) tpH = 12;
            tpM  = t.Value.Minute;
        }
        else { tpH = 12; tpM = 0; tpAp = "am"; }
        _ = JS.InvokeVoidAsync("scrollAmtPickerToSelected");
    }

    private TimeOnly PickerToTime() { int h24 = tpAp == "pm" ? (tpH % 12) + 12 : tpH % 12; return new TimeOnly(h24, tpM); }
    private void ToggleTpAp() { tpAp = tpAp == "am" ? "pm" : "am"; }
    private void ApplyMainT(AddEntryBlock blk) { blk.TimeValue = PickerToTime(); StateHasChanged(); }
    private void ApplyBrkT(AddEntryBlock blk, bool isStart)
    { if (isStart) blk.BreakStart = PickerToTime(); else blk.BreakEnd = PickerToTime(); StateHasChanged(); }

    private void ParseBreakT(AddEntryBlock blk, string? raw, bool isStart)
    {
        if (string.IsNullOrWhiteSpace(raw)) { if (isStart) blk.BreakStart = null; else blk.BreakEnd = null; return; }
        if (TimeOnly.TryParse(raw.Trim(), out var t)) { if (isStart) blk.BreakStart = t; else blk.BreakEnd = t; }
    }

    private void ToggleDatePicker(int idx)
    {
        amtDatePickerIdx = amtDatePickerIdx == idx ? -1 : idx;
        amtCalYear  = addEntryDate.Year;
        amtCalMonth = addEntryDate.Month;
        amtTimePickerKey = -1;
    }

    private void MoveCalMonth(int delta)
    {
        amtCalMonth += delta;
        if (amtCalMonth < 1)  { amtCalMonth = 12; amtCalYear--; }
        if (amtCalMonth > 12) { amtCalMonth = 1;  amtCalYear++; }
    }

    private void HandleSegKey(KeyboardEventArgs e, AddEntryBlock blk, int bIdx, int seg)
    {
        int activeSeg = (amtSegFocus / 10 == bIdx || amtSegFocus == bIdx)
            ? (amtSegFocus % 10 == 0 ? seg : amtSegFocus % 10) : seg;
        if (activeSeg == 0) activeSeg = seg;
        seg = activeSeg;

        if (e.Key == "Tab" || e.Key == "Escape") { amtSegFocus = -1; amtSegBuf = ""; return; }
        if (e.Key == "ArrowRight") { amtSegFocus = bIdx*10 + (seg < 3 ? seg+1 : 1); amtSegBuf = ""; return; }
        if (e.Key == "ArrowLeft")  { amtSegFocus = bIdx*10 + (seg > 1 ? seg-1 : 3); amtSegBuf = ""; return; }

        var t = blk.TimeValue ?? new TimeOnly(12, 0);
        int h12 = t.Hour % 12; if (h12 == 0) h12 = 12;
        bool isPm = t.Hour >= 12;

        if (seg == 3)
        {
            if      (e.Key == "a" || e.Key == "A") blk.TimeValue = new TimeOnly(isPm ? h12 % 12 : h12 % 12, t.Minute);
            else if (e.Key == "p" || e.Key == "P") blk.TimeValue = new TimeOnly((h12 % 12) + 12, t.Minute);
            else ToggleSegAp(blk);
            tpAp = blk.TimeValue!.Value.Hour >= 12 ? "pm" : "am";
            StateHasChanged(); return;
        }

        if (e.Key == "ArrowUp" || e.Key == "ArrowDown")
        {
            int delta = e.Key == "ArrowUp" ? 1 : -1;
            if (seg == 1) { h12 = ((h12 - 1 + delta + 12) % 12) + 1; blk.TimeValue = new TimeOnly(isPm ? (h12%12)+12 : h12%12, t.Minute); tpH = h12; }
            else          { int nm = (t.Minute + delta + 60) % 60; blk.TimeValue = new TimeOnly(t.Hour, nm); tpM = nm; }
            StateHasChanged(); return;
        }

        if (!char.IsDigit(e.Key[0]) || e.Key.Length != 1) return;

        if (seg == 1)
        {
            amtSegBuf += e.Key;
            int newH = int.Parse(amtSegBuf);
            if (amtSegBuf.Length == 1 && newH >= 2) { h12 = newH; amtSegBuf = ""; amtSegFocus = bIdx*10+2; }
            else if (amtSegBuf.Length == 1) { h12 = newH; }
            else { if (newH >= 1 && newH <= 12) h12 = newH; else h12 = int.Parse(e.Key) == 0 ? 12 : Math.Min(int.Parse(e.Key), 12); amtSegBuf = ""; amtSegFocus = bIdx*10+2; }
            if (h12 >= 1 && h12 <= 12) { blk.TimeValue = new TimeOnly(isPm ? (h12%12)+12 : h12%12, t.Minute); tpH = h12; }
        }
        else
        {
            amtSegBuf += e.Key;
            int newM = int.Parse(amtSegBuf);
            if (amtSegBuf.Length == 1 && newM >= 6) { blk.TimeValue = new TimeOnly(t.Hour, newM); tpM = newM; amtSegBuf = ""; amtSegFocus = bIdx*10+3; }
            else if (amtSegBuf.Length == 1) { blk.TimeValue = new TimeOnly(t.Hour, newM); tpM = newM; }
            else { int m2 = newM <= 59 ? newM : int.Parse(e.Key); blk.TimeValue = new TimeOnly(t.Hour, m2); tpM = m2; amtSegBuf = ""; amtSegFocus = bIdx*10+3; }
        }
        StateHasChanged();
    }

    private void ToggleSegAp(AddEntryBlock blk)
    {
        if (!blk.TimeValue.HasValue) { blk.TimeValue = new TimeOnly(12, 0); return; }
        var t = blk.TimeValue.Value;
        blk.TimeValue = t.Hour >= 12 ? new TimeOnly(t.Hour - 12, t.Minute) : new TimeOnly(t.Hour + 12, t.Minute);
    }

    private void ParseTimeInput(AddEntryBlock blk, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { blk.TimeValue = null; return; }
        var s       = raw.Trim().ToLowerInvariant();
        bool hasPm  = s.Contains("pm"); bool hasAm = s.Contains("am");
        var digits  = new string(s.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return;
        if (digits.Length >= 1 && digits.Length <= 4)
        {
            int h, m;
            if (digits.Length <= 2) { h = int.Parse(digits); m = blk.TimeValue?.Minute ?? 0; bool pm = hasPm ? true : hasAm ? false : (blk.TimeValue.HasValue && blk.TimeValue.Value.Hour >= 12); if (h < 1 || h > 12) return; int h24 = pm ? (h%12)+12 : h%12; blk.TimeValue = new TimeOnly(h24, m); return; }
            else if (digits.Length == 3) { h = int.Parse(digits[..1]); m = int.Parse(digits[1..]); }
            else { h = int.Parse(digits[..2]); m = int.Parse(digits[2..]); }
            if (m > 59) return;
            if (h > 12 && h <= 23 && !hasPm && !hasAm) { blk.TimeValue = new TimeOnly(h, m); return; }
            if (h < 1 || h > 12) return;
            bool isPm2 = hasPm ? true : hasAm ? false : (blk.TimeValue.HasValue && blk.TimeValue.Value.Hour >= 12);
            blk.TimeValue = new TimeOnly(isPm2 ? (h%12)+12 : h%12, m);
        }
    }

    private void ParseDateInput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var s = raw.Trim();
        if (DateTime.TryParseExact(s, "dd-MM-yyyy", null, System.Globalization.DateTimeStyles.None, out var d1)) { addEntryDate = d1; return; }
        if (DateTime.TryParse(s, out var d2)) addEntryDate = d2;
    }
}
