using APM.StaffZen.Blazor.ViewModels;
using System.Net.Http.Json;

namespace APM.StaffZen.Blazor.Services
{
    public class OrgService
    {
        private readonly IHttpClientFactory _factory;

        public OrgService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        private HttpClient Client => _factory.CreateClient("API");

        /// <summary>Returns all orgs the employee belongs to.</summary>
        public async Task<List<UserOrgMembership>> GetMyOrgsAsync(int employeeId)
        {
            try
            {
                var result = await Client.GetFromJsonAsync<List<UserOrgMembership>>(
                    $"api/organizations/by-employee/{employeeId}");
                return result ?? new();
            }
            catch { return new(); }
        }

        /// <summary>Returns all active members of an org.</summary>
        public async Task<List<OrgMemberSummary>> GetOrgMembersAsync(int orgId)
        {
            try
            {
                var result = await Client.GetFromJsonAsync<List<OrgMemberSummary>>(
                    $"api/organizations/{orgId}/employees");
                return result ?? new();
            }
            catch { return new(); }
        }

        /// <summary>Creates a new organization. Creator becomes Admin.</summary>
        public async Task<(OrgDetailVm? org, string? error)> CreateOrgAsync(CreateOrgRequest req)
        {
            try
            {
                var resp = await Client.PostAsJsonAsync("api/organizations", req);
                if (resp.IsSuccessStatusCode)
                    return (await resp.Content.ReadFromJsonAsync<OrgDetailVm>(), null);

                var body = await resp.Content.ReadAsStringAsync();
                return (null, body);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }

        /// <summary>Adds an existing employee (by email) to an org with a role.</summary>
        public async Task<(bool success, string? error)> AddMemberAsync(int orgId, string email, string orgRole)
        {
            try
            {
                var resp = await Client.PostAsJsonAsync(
                    $"api/organizations/{orgId}/members",
                    new { email, orgRole });
                if (resp.IsSuccessStatusCode) return (true, null);
                var body = await resp.Content.ReadAsStringAsync();
                return (false, body);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        /// <summary>Updates the role of a member within an org.</summary>
        public async Task<bool> UpdateMemberRoleAsync(int orgId, int employeeId, string newRole)
        {
            try
            {
                var resp = await Client.PatchAsJsonAsync(
                    $"api/organizations/{orgId}/members/{employeeId}/role",
                    new { orgRole = newRole });
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>Soft-removes a member from an org.</summary>
        public async Task<(bool success, string? error)> RemoveMemberAsync(int orgId, int employeeId)
        {
            try
            {
                var resp = await Client.DeleteAsync($"api/organizations/{orgId}/members/{employeeId}");
                if (resp.IsSuccessStatusCode) return (true, null);
                var body = await resp.Content.ReadAsStringAsync();
                return (false, body);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        /// <summary>Updates org details (name, industry, etc.).</summary>
        public async Task<bool> UpdateOrgAsync(int orgId, object dto)
        {
            try
            {
                var resp = await Client.PutAsJsonAsync($"api/organizations/{orgId}", dto);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>Accepts a pending org invitation for the given employee.</summary>
        public async Task<(bool success, string? error)> AcceptInviteAsync(int orgId, int employeeId)
        {
            try
            {
                var resp = await Client.PostAsJsonAsync(
                    $"api/organizations/{orgId}/invitations/accept",
                    new { employeeId });
                if (resp.IsSuccessStatusCode) return (true, null);
                var body = await resp.Content.ReadAsStringAsync();
                return (false, body);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        /// <summary>Declines a pending org invitation for the given employee.</summary>
        public async Task<(bool success, string? error)> DeclineInviteAsync(int orgId, int employeeId)
        {
            try
            {
                var resp = await Client.PostAsJsonAsync(
                    $"api/organizations/{orgId}/invitations/decline",
                    new { employeeId });
                if (resp.IsSuccessStatusCode) return (true, null);
                var body = await resp.Content.ReadAsStringAsync();
                return (false, body);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }
    }

    // ── View models used only in Blazor ──────────────────────────────────

    public class CreateOrgRequest
    {
        public string  Name             { get; set; } = "";
        public int     EmployeeId       { get; set; }
        public string? Country          { get; set; }
        public string? PhoneNumber      { get; set; }
        public string? CountryCode      { get; set; }
        public string? Industry         { get; set; }
        public string? OrganizationSize { get; set; }
        public string? OwnerRole        { get; set; }
    }

    public class OrgDetailVm
    {
        public int    Id   { get; set; }
        public string Name { get; set; } = "";
        public int    EmployeeId { get; set; }
        public List<OrgMemberSummary> Employees { get; set; } = new();
    }
}
