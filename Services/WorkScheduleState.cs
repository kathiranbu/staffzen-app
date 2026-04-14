namespace APM.StaffZen.Blazor.Services
{
    /// <summary>
    /// In-memory singleton that keeps work schedule UI state alive across
    /// Blazor Server navigation (tab switches).  The WorkSchedules page reads
    /// and writes this class so that leaving the page and coming back does NOT
    /// reset the schedule list or the active selection.
    /// </summary>
    public static class WorkScheduleState
    {
        // ── Persisted schedule list ───────────────────────────────────────
        public static List<PersistedSchedule> Schedules { get; set; } = new();
        public static int  NextId            { get; set; } = 1;
        public static int  ActiveScheduleId  { get; set; } = 0;

        // ── Lightweight mirror of ScheduleVm ─────────────────────────────
        // (We cannot store the private inner class directly from the razor
        //  component, so we use this equivalent DTO that the component maps
        //  to/from on enter and on save.)
        public class PersistedSchedule
        {
            public int    Id                      { get; set; }
            public string Name                    { get; set; } = "New Schedule";
            public bool   IsDefault               { get; set; }
            public string Arrangement             { get; set; } = "Fixed";
            public List<string> WorkingDays       { get; set; } = new() { "Mon","Tue","Wed","Thu","Fri" };
            public Dictionary<string, PersistedDaySlot> DaySlots { get; set; } = new();
            public bool   IncludeBeforeStart      { get; set; }
            public int    WeeklyHours             { get; set; }
            public int    WeeklyMinutes           { get; set; }
            public string SplitAt                 { get; set; } = "00:00";
            public List<PersistedBreak>     Breaks         { get; set; } = new();
            public List<PersistedDeduction> AutoDeductions { get; set; } = new();
            public bool   DailyOvertime           { get; set; }
            public bool   DailyOvertimeIsTime     { get; set; }
            public int    DailyOvertimeAfterHours { get; set; } = 8;
            public int    DailyOvertimeAfterMins  { get; set; }
            public double DailyOvertimeMultiplier { get; set; } = 1.5;
            public bool   DailyDoubleOvertime     { get; set; }
            public bool   DailyDoubleOvertimeIsTime { get; set; }
            public int    DailyDoubleOTAfterHours { get; set; } = 8;
            public int    DailyDoubleOTAfterMins  { get; set; }
            public double DailyDoubleOTMultiplier { get; set; } = 1.5;
            public bool   WeeklyOvertime          { get; set; }
            public int    WeeklyOvertimeAfterHours { get; set; } = 40;
            public int    WeeklyOvertimeAfterMins  { get; set; }
            public double WeeklyOvertimeMultiplier { get; set; } = 1.5;
            public bool   RestDayOvertime          { get; set; }
            public double RestDayOvertimeMultiplier { get; set; } = 1.5;
            public bool   PublicHolidayOvertime    { get; set; }
            public double PublicHolidayOvertimeMultiplier { get; set; } = 1.5;
            public bool   IsSaved                 { get; set; }

            // ── Verification policy ──────────────────────────────────────
            public bool   RequireFaceVerification { get; set; } = false;
            public bool   RequireSelfie           { get; set; } = false;
            public string UnusualBehavior         { get; set; } = "Blocked";
        }

        public class PersistedDaySlot
        {
            public string Start      { get; set; } = "09:00";
            public string End        { get; set; } = "17:00";
            public double DailyHours { get; set; }
        }

        public class PersistedBreak
        {
            public string Name         { get; set; } = "";
            public int    DurationHours { get; set; }
            public int    DurationMins  { get; set; }
            public bool   IsPaid        { get; set; }
            public bool   HasTimeRange  { get; set; }
            public string RangeStart    { get; set; } = "13:00";
            public string RangeEnd      { get; set; } = "14:00";
        }

        public class PersistedDeduction
        {
            public double AfterHours    { get; set; } = 5;
            public int    DeductMinutes { get; set; } = 30;
        }

        // ── Public REST-day helper (used by Timesheets) ───────────────────
        /// <summary>
        /// Returns true when <paramref name="date"/> is a rest day in the
        /// default (or only) saved schedule.
        /// </summary>
        public static bool IsRestDay(DateTime date)
        {
            var schedule = Schedules.FirstOrDefault(s => s.IsDefault && s.IsSaved)
                        ?? Schedules.FirstOrDefault(s => s.IsSaved);
            if (schedule == null) return false;

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
            return !schedule.WorkingDays.Contains(dayKey);
        }
    }
}
