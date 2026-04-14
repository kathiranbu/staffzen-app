using APM.StaffZen.Blazor.Services;
namespace APM.StaffZen.Blazor.Components.Pages.Dashboard;

public partial class Timesheets
{
    private void OpenDuplicateModal(RangeTimesheetRow row)
    {
        duplicateTargetRow = row;
        dupFromStart       = weekRangeFrom.AddDays(-7);
        dupToStart         = null;
        dupActivePlusIdx   = -1;
        BuildDupBreakdown(row);
        showDuplicateModal = true;
    }

    private void CloseDuplicateModal()
    {
        showDuplicateModal = false;
        duplicateTargetRow = null;
        dupActivePlusIdx   = -1;
    }

    private void DupFromPrev() { dupFromStart = dupFromStart.AddDays(-7); BuildDupBreakdown(duplicateTargetRow); }
    private void DupFromNext() { dupFromStart = dupFromStart.AddDays(7);  BuildDupBreakdown(duplicateTargetRow); }
    private void DupToPrev()   { dupToStart   = (dupToStart ?? weekRangeFrom).AddDays(-7); }
    private void DupToNext()   { dupToStart   = (dupToStart ?? weekRangeFrom).AddDays(7); }

    private void AddDupTimeEntry(int dayIndex)
    {
        if (dayIndex >= 0 && dayIndex < dupDayBreakdown.Count)
        {
            dupDayBreakdown[dayIndex].TimeEntries.Add(new DupTimeEntry());
            StateHasChanged();
        }
    }

    private void BuildDupBreakdown(RangeTimesheetRow? row)
    {
        dupDayBreakdown.Clear();
        if (row == null) { dupFromHours = "0h 0m"; return; }
        int totalMins = 0;
        for (int i = 0; i < 7; i++)
        {
            var d   = dupFromStart.AddDays(i);
            var key = d.ToString("yyyy-MM-dd");
            if (row.Days.TryGetValue(key, out var de) && de != null && de.TrackedMins > 0)
            {
                totalMins += de.TrackedMins;
                dupDayBreakdown.Add(new DupDayItem { Label = d.ToString("dddd, d MMM"), WorkedHours = de.TrackedHours ?? "0h 0m", BreakHours = "0h 0m" });
            }
            else
            {
                dupDayBreakdown.Add(new DupDayItem { Label = d.ToString("dddd, d MMM"), WorkedHours = "0h 0m", BreakHours = "0h 0m" });
            }
        }
        var ts = TimeSpan.FromMinutes(totalMins);
        dupFromHours = totalMins == 0 ? "0h 0m" : $"{(int)ts.TotalHours}h {ts.Minutes}m";
    }
}
