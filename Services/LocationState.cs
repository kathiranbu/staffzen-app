namespace APM.StaffZen.Blazor.Services
{
    /// <summary>
    /// In-memory singleton that keeps location data alive across
    /// Blazor Server navigation (tab switches). The Locations page reads
    /// and writes this class so that leaving the page and coming back does NOT
    /// reset the saved or archived locations list.
    /// </summary>
    public static class LocationState
    {
        public static List<PersistedLocation> Locations         { get; set; } = new();
        public static List<PersistedLocation> ArchivedLocations { get; set; } = new();
        public static int NextId { get; set; } = 10;

        public class PersistedLocation
        {
            public int    Id        { get; set; }
            public string Name      { get; set; } = "";
            public string Address   { get; set; } = "";
            public double Lat       { get; set; }
            public double Lng       { get; set; }
            public int    RadiusM   { get; set; } = 300;
            public bool   Archived  { get; set; } = false;
            /// <summary>True when added via the "Add missing location" pin-drag flow.</summary>
            public bool   IsMissing { get; set; } = false;
        }
    }
}
