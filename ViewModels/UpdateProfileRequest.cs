namespace APM.StaffZen.Blazor.ViewModels
{
    public class UpdateProfileRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string? MobileNumber { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? ProfileImageUrl { get; set; }
    }
}
