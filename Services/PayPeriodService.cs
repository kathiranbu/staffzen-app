using System.Net.Http.Json;

namespace APM.StaffZen.Blazor.Services
{
    public class PayPeriodSettingDto
    {
        public int      Id             { get; set; }
        public int      OrganizationId { get; set; }
        public string   Name           { get; set; } = "";
        public string   Frequency      { get; set; } = "Weekly";
        public int      StartDow       { get; set; } = 1;
        public int      FirstDay       { get; set; } = 1;
        public int      SemiDay        { get; set; } = 16;
        public DateTime StartDate      { get; set; } = DateTime.Today;
    }

    public class PayPeriodService
    {
        private readonly IHttpClientFactory _factory;
        private HttpClient Client => _factory.CreateClient("API");

        public PayPeriodService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        /// <summary>Returns null if no pay period has been saved yet.</summary>
        public async Task<PayPeriodSettingDto?> GetAsync(int orgId)
        {
            try
            {
                var resp = await Client.GetAsync($"api/organizations/{orgId}/pay-period");
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadFromJsonAsync<PayPeriodSettingDto>();
            }
            catch
            {
                return null;
            }
        }

        public async Task<(bool success, string? error)> SaveAsync(int orgId, PayPeriodSettingDto dto)
        {
            try
            {
                dto.OrganizationId = orgId;
                var resp = await Client.PutAsJsonAsync($"api/organizations/{orgId}/pay-period", dto);
                if (resp.IsSuccessStatusCode) return (true, null);
                return (false, await resp.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
