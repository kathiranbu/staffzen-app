namespace APM.StaffZen.Blazor.Components.Pages.TimeOff
{
    /// <summary>
    /// Exposes the shared in-memory holiday calendars from HolidaysTab
    /// so other pages (e.g. Timesheets) can read holiday data.
    /// </summary>
    public static class HolidaysTabState
    {
        // This list is written to by HolidaysTab when calendars are created/saved.
        // It mirrors HolidaysTab._sharedCalendars via a bridge registered on save.
        public static List<HolidayCalendarPublic> SharedCalendars { get; set; } = new();
    }

    public class HolidayCalendarPublic
    {
        public int    Id        { get; set; }
        public string Name      { get; set; } = "";
        public bool   IsDefault { get; set; }
        public List<HolidayEntryPublic> Holidays { get; set; } = new();
    }

    public class HolidayEntryPublic
    {
        public string   Name { get; set; } = "";
        public DateTime Date { get; set; }
    }
}
