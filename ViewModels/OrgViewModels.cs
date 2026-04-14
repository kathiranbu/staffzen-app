namespace APM.StaffZen.Blazor.ViewModels
{
    // UserOrgMembership is defined in LoginResponse.cs — not duplicated here

    public class OrgMemberSummary
    {
        public int     Id              { get; set; }
        public string  FullName        { get; set; } = string.Empty;
        public string  Email           { get; set; } = string.Empty;

        /// <summary>Maps to the API field 'OrgRole' returned by /organizations/{id}/employees</summary>
        public string  OrgRole         { get; set; } = string.Empty;

        /// <summary>Alias so existing UI code using .Role still works.</summary>
        public string  Role
        {
            get => string.IsNullOrEmpty(OrgRole) ? _role : OrgRole;
            set => _role = value;
        }
        private string _role = string.Empty;

        public bool    IsActive        { get; set; }
        public string? ProfileImageUrl { get; set; }
    }
}
