using Microsoft.JSInterop;
using APM.StaffZen.Blazor.Services;
namespace APM.StaffZen.Blazor.Components.Pages.Dashboard;

public partial class Timesheets
{
    private string ExportModalTitle => expDateRange switch
    {
        "day"    => "Daily Timesheets Data",
        "week"   => "Weekly Timesheets Data",
        "month"  => "Monthly Timesheets Data",
        "custom" => "Custom Timesheets Data",
        _        => "Timesheets Data"
    };

    private async Task OpenExportModal()
    {
        expDateRange = viewMode switch { "daily" => "day", "weekly" => "week", "monthly" => "month", _ => "week" };
        SyncExpDates();
        expCalYear  = expRangeStart.Year;
        expCalMonth = expRangeStart.Month;
        expAllGroups  = await AttendanceService.GetGroupsAsync();
        expAllMembers = await AttendanceService.GetAllMembersAsync(SessionService.ActiveOrganizationId);
        showExportModal = true;
    }

    private void CloseExportModal() { showExportModal = false; CloseAllExpDropdowns(); }

    private void SyncExpDates()
    {
        switch (expDateRange)
        {
            case "day":    expRangeStart = dtlSelectedDate; expRangeEnd = null; break;
            case "week":   expRangeStart = weekRangeFrom;   expRangeEnd = weekRangeFrom.AddDays(6); break;
            case "month":  expRangeStart = new DateTime(monthDate.Year, monthDate.Month, 1);
                           expRangeEnd   = new DateTime(monthDate.Year, monthDate.Month, DateTime.DaysInMonth(monthDate.Year, monthDate.Month)); break;
            case "custom": expRangeStart = weekRangeFrom;   expRangeEnd = weekRangeFrom.AddDays(6); break;
        }
        expCalYear = expRangeStart.Year; expCalMonth = expRangeStart.Month;
    }

    private string GetExportDateRangeLabel()
    {
        if (expDateRange == "day") return expRangeStart.ToString("MMM, dd yyyy");
        if (expRangeEnd.HasValue)  return $"{expRangeStart:MMM, dd yyyy} - {expRangeEnd.Value:MMM, dd yyyy}";
        return expRangeStart.ToString("MMM, dd yyyy");
    }

    private void ExpCalPrevMonth() { expCalMonth--; if (expCalMonth < 1)  { expCalMonth = 12; expCalYear--; } }
    private void ExpCalNextMonth() { expCalMonth++; if (expCalMonth > 12) { expCalMonth = 1;  expCalYear++; } }
    private void ExpCalPrevYear()  => expCalYear--;
    private void ExpCalNextYear()  => expCalYear++;

    private void ExpCalSelectDay(DateTime date)
    {
        if      (expDateRange == "day")    { expRangeStart = date; expRangeEnd = null; expShowDateCal = false; }
        else if (expDateRange == "week")   { int dow = (int)date.DayOfWeek; expRangeStart = date.AddDays(-(dow == 0 ? 6 : dow - 1)); expRangeEnd = expRangeStart.AddDays(6); expShowDateCal = false; }
        else if (expDateRange == "month")  { expRangeStart = new DateTime(date.Year, date.Month, 1); expRangeEnd = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)); expShowDateCal = false; }
        else { if (expRangeEnd.HasValue || date < expRangeStart) { expRangeStart = date; expRangeEnd = null; } else { expRangeEnd = date; expShowDateCal = false; } }
    }

    private void ToggleExpFilter(string which)
    {
        expPayrollOpen   = which == "payroll"   && !expPayrollOpen;
        expGroupsOpen    = which == "groups"    && !expGroupsOpen;
        expMembersOpen   = which == "members"   && !expMembersOpen;
        expSchedulesOpen = which == "schedules" && !expSchedulesOpen;
        expAddFilterOpen = which == "addfilter" && !expAddFilterOpen;
    }

    private void CloseAllExpDropdowns()
    {
        expShowPeriodDd = expShowDateCal = expPayrollOpen = expGroupsOpen = expMembersOpen =
        expSchedulesOpen = expAddFilterOpen = expTimeFormatOpen = expDurationFormatOpen = false;
    }

    private string GetDurationLabel() => expDurationFormat switch
    {
        "hmm"  => "h:mm", "hmms" => "h:mm:ss", "dec2" => "Decimal h.XX",
        "dec4" => "Decimal h.XXXX", "hm" => "XXh YYm", _ => "XXh YYm"
    };

    private string GetDurationPreview() => expDurationFormat switch
    {
        "hmm"  => "7:50", "hmms" => "7:50:40", "dec2" => "7.84",
        "dec4" => "7.8444", "hm" => "7h 50m", _ => "7h 50m"
    };

    private async Task ExecuteExport()
    {
        try { if (expFormat == "csv") await ExportCsv(); else await ExportXls(); }
        catch { }
        CloseExportModal();
    }

    private List<string[]> BuildExportRows()
    {
        var header = new[] { "Day", "Date", "Full Name", "Member Code", "Work Schedule",
                             "Tracked Hours", "Worked Hours", "Payroll Hours", "Regular Hours", "First In", "Last Out" };
        var rows = new List<string[]> { header };
        if (viewMode == "daily")
        {
            foreach (var r in dtlRows)
                rows.Add(new[] { expRangeStart.ToString("dddd"), expRangeStart.ToString("M/d/yyyy"), r.FullName, "", "Default Work Schedule",
                    r.TrackedHours, r.TrackedHours, r.TrackedHours, r.TrackedHours,
                    r.FirstIn.HasValue ? r.FirstIn.Value.ToString("h:mm tt") : "-",
                    r.LastOut.HasValue ? r.LastOut.Value.ToString("h:mm tt") : "-" });
        }
        else
        {
            foreach (var r in rangeRows)
                foreach (var kv in r.Days.OrderBy(x => x.Key))
                {
                    var d  = DateTime.Parse(kv.Key); var de = kv.Value;
                    rows.Add(new[] { d.ToString("dddd"), d.ToString("M/d/yyyy"), r.FullName, "", "Default Work Schedule",
                        de?.TrackedHours ?? "-", de?.TrackedHours ?? "-", de?.TrackedHours ?? "-", de?.TrackedHours ?? "-",
                        de?.FirstIn.HasValue == true  ? de.FirstIn!.Value.ToString("h:mm tt")  : "-",
                        de?.LastOut.HasValue == true  ? de.LastOut!.Value.ToString("h:mm tt") : "-" });
                }
        }
        return rows;
    }

    private async Task ExportCsv()
    {
        var rows      = BuildExportRows();
        var sb        = new System.Text.StringBuilder();
        foreach (var row in rows) sb.AppendLine(string.Join(",", row.Select(c => $"\"{c.Replace("\"", "\"\"")}\"" )));
        var dateLabel = expRangeEnd.HasValue ? $"{expRangeStart:yyyy_MM_dd}_to_{expRangeEnd.Value:yyyy_MM_dd}" : expRangeStart.ToString("yyyy_MM_dd");
        await JS.InvokeVoidAsync("eval", $@"(function(){{var csv={System.Text.Json.JsonSerializer.Serialize(sb.ToString())};var blob=new Blob([csv],{{type:'text/csv'}});var a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='Raw_Timesheet_{dateLabel}.csv';a.click();}})();");
    }

    private async Task ExportXls()
    {
        var fromStr = expRangeStart.ToString("yyyy-MM-dd");
        var toStr   = (expRangeEnd ?? expRangeStart).ToString("yyyy-MM-dd");
        var bytes   = await AttendanceService.ExportXlsxAsync(fromStr, toStr, expIncludeTimesheets, expIncludeTimeEntries,
            includePerMemberSummary: expIncludePerMember && expPerMemberMode == "summary",
            includePerMemberDetailed: expIncludePerMember && expPerMemberMode == "detailed",
            organizationId: SessionService.ActiveOrganizationId);
        if (bytes == null || bytes.Length == 0) return;
        var groupName = "ASK GROUPS";
        var dateLabel = expRangeEnd.HasValue
            ? $"Daily_Timesheet_-_{groupName}_-_{expRangeStart:yyyy_MM_dd}_to_{expRangeEnd.Value:yyyy_MM_dd}"
            : $"Daily_Timesheet_-_{groupName}_-_{expRangeStart:yyyy_MM_dd}_to_{expRangeStart:yyyy_MM_dd}";
        var base64 = Convert.ToBase64String(bytes);
        await JS.InvokeVoidAsync("eval", $@"(function(){{var b64='{base64}';var bin=atob(b64);var arr=new Uint8Array(bin.length);for(var i=0;i<bin.length;i++)arr[i]=bin.charCodeAt(i);var blob=new Blob([arr],{{type:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'}});var a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='{dateLabel}.xlsx';document.body.appendChild(a);a.click();document.body.removeChild(a);}})();");
    }
}
