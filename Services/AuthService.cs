using APM.StaffZen.Blazor.ViewModels;

namespace APM.StaffZen.Blazor.Services
{
    public class AuthService
    {
        private readonly IHttpClientFactory _factory;

        public AuthService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        // Standard email + password login — returns (response, errorMessage)
        public async Task<(LoginResponse? result, string? error)> LoginAsync(LoginRequest request)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Auth/login", request);

                if (response.IsSuccessStatusCode)
                    return (await response.Content.ReadFromJsonAsync<LoginResponse>(), null);

                var body = await response.Content.ReadAsStringAsync();
                // API returns plain string for Unauthorized
                return (null, string.IsNullOrWhiteSpace(body) ? "Login failed." : body.Trim('"'));
            }
            catch (Exception ex)
            {
                return (null, $"Could not reach server: {ex.Message}");
            }
        }

        // Forgot password — request a reset link
        // Returns (success, errorMessage)
        public async Task<(bool success, string? error)> ForgotPasswordAsync(string email, string phone)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Auth/forgot-password",
                    new { Email = email, PhoneNumber = phone });

                if (response.IsSuccessStatusCode)
                    return (true, null);

                // Read error detail from API
                var body = await response.Content.ReadAsStringAsync();
                return (false, $"Server error ({(int)response.StatusCode}): {body}");
            }
            catch (Exception ex)
            {
                return (false, $"Could not reach server: {ex.Message}");
            }
        }

        // Validate the reset token — returns the associated email or null if invalid/expired
        public async Task<string?> ValidateResetTokenAsync(string token)
        {
            var client = _factory.CreateClient("API");
            var response = await client.GetAsync(
                $"api/Auth/validate-reset-token?token={Uri.EscapeDataString(token)}");

            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync<TokenValidationResult>();
            return result?.Email;
        }

        // Reset password with token
        public async Task<bool> ResetPasswordAsync(string token, string newPassword)
        {
            var client = _factory.CreateClient("API");
            var response = await client.PostAsJsonAsync("api/Auth/reset-password",
                new { Token = token, NewPassword = newPassword });
            return response.IsSuccessStatusCode;
        }

        // Change password for a logged-in user (verifies current password first)
        public async Task<(bool success, string? error)> ChangePasswordAsync(
            string email, string currentPassword, string newPassword)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Auth/change-password",
                    new { Email = email, CurrentPassword = currentPassword, NewPassword = newPassword });

                if (response.IsSuccessStatusCode)
                    return (true, null);

                var body = await response.Content.ReadAsStringAsync();
                // Try to extract the "error" field from JSON
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(body);
                    if (json.RootElement.TryGetProperty("error", out var errProp))
                        return (false, errProp.GetString());
                }
                catch { }
                return (false, $"Error ({(int)response.StatusCode})");
            }
            catch (Exception ex)
            {
                return (false, $"Could not reach server: {ex.Message}");
            }
        }

                // Google login — email already verified by Google, no password needed.
        // Returns the employee session if email is found, null if not registered.
        public async Task<(LoginResponse? result, string? error)> GoogleLoginAsync(string email)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Auth/google-login",
                    new { Email = email });

                if (response.IsSuccessStatusCode)
                    return (await response.Content.ReadFromJsonAsync<LoginResponse>(), null);

                var body = await response.Content.ReadAsStringAsync();
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(body);
                    if (json.RootElement.TryGetProperty("error", out var errProp))
                        return (null, errProp.GetString());
                }
                catch { }
                return (null, $"Google login failed ({(int)response.StatusCode}).");
            }
            catch (Exception ex)
            {
                return (null, $"Could not reach server: {ex.Message}");
            }
        }

        // Self-registration with email + password
        // Returns (response, errorMessage)
        public async Task<(LoginResponse? result, string? error)> RegisterAsync(
            string fullName, string email, string password, string? phone)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Auth/register",
                    new { FullName = fullName, Email = email, Password = password, PhoneNumber = phone });

                if (response.IsSuccessStatusCode)
                    return (await response.Content.ReadFromJsonAsync<LoginResponse>(), null);

                var body = await response.Content.ReadAsStringAsync();
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(body);
                    if (json.RootElement.TryGetProperty("error", out var errProp))
                        return (null, errProp.GetString());
                }
                catch { }
                return (null, $"Registration failed ({(int)response.StatusCode})");
            }
            catch (Exception ex)
            {
                return (null, $"Could not reach server: {ex.Message}");
            }
        }

        // Marks the user as having completed the getting-started onboarding
        public async Task<bool> CompleteOnboardingAsync(int employeeId)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Auth/complete-onboarding",
                    new { EmployeeId = employeeId });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // Sets a password for Google-only accounts that have no password yet
        public async Task<(bool success, string? error)> SetPasswordAsync(string email, string password)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Auth/set-password",
                    new { Email = email, Password = password });

                if (response.IsSuccessStatusCode) return (true, null);

                var body = await response.Content.ReadAsStringAsync();
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(body);
                    if (json.RootElement.TryGetProperty("error", out var e))
                        return (false, e.GetString());
                }
                catch { }
                return (false, body.Trim('"'));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // Creates a new organization from the Getting-Started form.
        // Returns the new organization's Id on success, or null on failure.
        public async Task<int?> CreateOrganizationAsync(
            int    employeeId,
            string orgName,
            string country,
            string phone,
            string countryCode,
            string industry,
            string orgSize,
            string ownerRole)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/organizations", new
                {
                    Name                = orgName,
                    Country             = country,
                    PhoneNumber         = phone,
                    CountryCode         = countryCode,
                    Industry            = industry,
                    OrganizationSize    = orgSize,
                    OwnerRole           = ownerRole,
                    EmployeeId          = employeeId
                });

                if (!response.IsSuccessStatusCode)
                    return null;

                // The API returns the full OrganizationDto; we only need the Id
                var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return json.GetProperty("id").GetInt32();
            }
            catch { return null; }
        }

        // Saves the selected devices for an organization
        public async Task<bool> SaveDevicesAsync(int organizationId, string selectedDevices)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PutAsJsonAsync(
                    $"api/organizations/{organizationId}/devices",
                    new { SelectedDevices = selectedDevices });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // Google self-registration — creates or returns an existing account from Google email
        public async Task<(LoginResponse? result, string? error)> GoogleRegisterAsync(
            string email, string? fullName, string? password = null)
        {
            try
            {
                var client = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync("api/Auth/google-register",
                    new { Email = email, FullName = fullName, Password = password });

                if (response.IsSuccessStatusCode)
                    return (await response.Content.ReadFromJsonAsync<LoginResponse>(), null);

                var body = await response.Content.ReadAsStringAsync();
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(body);
                    if (json.RootElement.TryGetProperty("error", out var errProp))
                        return (null, errProp.GetString());
                }
                catch { }
                return (null, $"Google registration failed ({(int)response.StatusCode})");
            }
            catch (Exception ex)
            {
                return (null, $"Could not reach server: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns all organizations a user belongs to (with their role in each).
        /// Call this on login or when the user opens the org-switcher.
        /// </summary>
        public async Task<List<UserOrgMembership>> GetUserOrganizationsAsync(int employeeId)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.GetAsync($"api/organizations/by-employee/{employeeId}");
                if (!response.IsSuccessStatusCode) return new();

                var list = await response.Content.ReadFromJsonAsync<List<UserOrgMembership>>();
                return list ?? new();
            }
            catch { return new(); }
        }

        /// <summary>
        /// Fetches the members of a specific organization.
        /// </summary>
        public async Task<List<OrgMemberSummary>> GetOrgMembersAsync(int organizationId)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.GetAsync($"api/organizations/{organizationId}/employees");
                if (!response.IsSuccessStatusCode) return new();

                var list = await response.Content.ReadFromJsonAsync<List<OrgMemberSummary>>();
                return list ?? new();
            }
            catch { return new(); }
        }

        /// <summary>
        /// Adds an already-registered employee to an organization by email.
        /// </summary>
        public async Task<(bool success, string? error)> AddMemberToOrgAsync(
            int orgId, string email, string orgRole = "Employee")
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.PostAsJsonAsync(
                    $"api/organizations/{orgId}/members",
                    new { Email = email, OrgRole = orgRole });

                if (response.IsSuccessStatusCode) return (true, null);
                var body = await response.Content.ReadAsStringAsync();
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(body);
                    if (json.RootElement.TryGetProperty("message", out var m))
                        return (false, m.GetString());
                }
                catch { }
                return (false, body.Trim('"'));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        /// <summary>
        /// Removes a member from an organization.
        /// </summary>
        public async Task<(bool success, string? error)> RemoveMemberFromOrgAsync(
            int orgId, int employeeId)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.DeleteAsync(
                    $"api/organizations/{orgId}/members/{employeeId}");

                if (response.IsSuccessStatusCode) return (true, null);
                var body = await response.Content.ReadAsStringAsync();
                return (false, body.Trim('"'));
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        /// <summary>
        /// Updates a member's role within an organization.
        /// </summary>
        public async Task<bool> UpdateMemberRoleAsync(int orgId, int employeeId, string newRole)
        {
            try
            {
                var client   = _factory.CreateClient("API");
                var response = await client.PatchAsJsonAsync(
                    $"api/organizations/{orgId}/members/{employeeId}/role",
                    new { OrgRole = newRole });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

    }
}

// ── Private helper DTOs ───────────────────────────────────────
file record TokenValidationResult(string Email);
