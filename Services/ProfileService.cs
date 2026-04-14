using APM.StaffZen.Blazor.ViewModels;
using System.Net.Http.Json;

namespace APM.StaffZen.Blazor.Services
{
    public class ProfileService
    {
        private readonly IHttpClientFactory _factory;

        public ProfileService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<ProfileViewModel?> GetProfileAsync(int employeeId)
        {
            var client = _factory.CreateClient("API");
            var response = await client.GetAsync($"api/employees/{employeeId}/profile");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ProfileViewModel>();
            }

            return null;
        }

        public async Task<ProfileViewModel?> UpdateProfileAsync(int employeeId, UpdateProfileRequest request)
        {
            var client = _factory.CreateClient("API");
            var response = await client.PutAsJsonAsync($"api/employees/{employeeId}/profile", request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ProfileViewModel>();
            }

            return null;
        }
    }
}
