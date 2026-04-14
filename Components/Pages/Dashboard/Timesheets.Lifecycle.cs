using APM.StaffZen.Blazor.Services;
using Microsoft.AspNetCore.Components;

namespace APM.StaffZen.Blazor.Components.Pages.Dashboard;

public partial class Timesheets : IDisposable
{
    // ── Lifecycle ────────────────────────────────────────────────────
    protected override async Task OnInitializedAsync()
    {
        employeeId      = SessionService.Id;
        int dow         = (int)DateTime.Today.DayOfWeek;
        weekStart       = DateTime.Today.AddDays(-(dow == 0 ? 6 : dow - 1));
        selectedDate    = DateTime.Today;
        dtlSelectedDate = DateTime.Today;
        weekRangeFrom   = DateTime.Today.AddDays(-(dow == 0 ? 6 : dow - 1));
        monthDate       = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        calPickerYear   = DateTime.Today.Year;
        calPickerMonth  = DateTime.Today.Month;

        // ── Load pay period setting early so date ranges are correct ──
        try
        {
            _payPeriod = await PayPeriodService.GetAsync(SessionService.ActiveOrganizationId ?? 0);
            if (_payPeriod != null)
            {
                // Recalculate approvals start based on the loaded pay period
                apvWeekStart = GetPeriodStartFor(DateTime.Today);
                apvCalYear   = apvWeekStart.Year;
                apvCalMonth  = apvWeekStart.Month;
            }
        }
        catch { /* pay period optional */ }

        // ── Load selfie/face-verification policy from default schedule ──
        try
        {
            var schedules = await WorkScheduleService.GetAllAsync();
            var def = schedules?.FirstOrDefault(s => s.IsDefault) ?? schedules?.FirstOrDefault();
            if (def != null)
                selfiePolicyEnabled = def.RequireSelfie || def.RequireFaceVerification;
        }
        catch { /* policy defaults to false — no selfie display */ }

        // Load all org members once so we can show them on holiday/rest days that have no entries
        try { allOrgMembers = await AttendanceService.GetAllMembersAsync(SessionService.ActiveOrganizationId); } catch { allOrgMembers = new(); }
        // Load the default daily view first
        await LoadDailyList();
        isLoaded = true;
        // Pre-load current month range in background so monthly/weekly views are ready without a wait
        _ = PreloadMonthRangeAsync();
    }

    protected override void OnInitialized()
    {
        NavManager.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        if (e.Location.EndsWith("/timesheets", StringComparison.OrdinalIgnoreCase))
        {
            currentView = "list";
            InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose() { NavManager.LocationChanged -= OnLocationChanged; }

    private async Task OnClockActionFired()
    {
        // Always refresh both the list view AND the week/day detail entries.
        // This ensures selfie photos appear immediately in Time Entries after
        // clock-in/out, regardless of which view is currently active.
        if (currentView == "list")
        {
            if (viewMode == "daily") await LoadDailyList();
            else                     await LoadRangeData();
        }
        else
        {
            await LoadWeekAndDay();
        }

        // ALWAYS refresh weekEntries + dayEntries so that when the user
        // opens the Time Entries detail panel the selfie photo is already loaded.
        // This fixes the case where clock-in happens from the list view and
        // the user then clicks into Time Entries expecting to see their selfie.
        await LoadWeekAndDay();
    }
}
