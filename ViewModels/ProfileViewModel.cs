namespace APM.StaffZen.Blazor.ViewModels
{
    public class ProfileViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? MobileNumber { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string Role { get; set; } = string.Empty;
        public string? OrganizationName { get; set; }
    }
}
