using APM.StaffZen.Blazor.ViewModels;
using Microsoft.JSInterop;
using System.Text.Json;

namespace APM.StaffZen.Blazor.Services
{
    public class SessionService
    {
        private readonly IJSRuntime _js;

        public SessionService(IJSRuntime js)
        {
            _js = js;
        }

        // ── Identity ────────────────────────────────────────────
        public int     Id          { get; private set; }
        public string? FullName    { get; private set; }
        public string? Email       { get; private set; }
        public string? Role        { get; private set; }
        public bool    IsOnboarded { get; private set; }

        // ── Active organization context ──────────────────────────
        /// <summary>
        /// The ID of the organization currently selected by the user.
        /// All data fetches (employees, attendance) are scoped to this org.
        /// </summary>
        public int?    ActiveOrganizationId   { get; private set; }
        public string? ActiveOrganizationName { get; private set; }

        /// <summary>The user's role within the active organization.</summary>
        public string? ActiveOrgRole { get; private set; }

        // ── Legacy compat ─────────────────────────────────────────
        public int?    OrganizationId
        {
            get => ActiveOrganizationId;
            set => ActiveOrganizationId = value;
        }
        public string? OrganizationName
        {
            get => ActiveOrganizationName;
            set => ActiveOrganizationName = value;
        }

        public bool IsLoggedIn => !string.IsNullOrEmpty(Role);

        /// <summary>True if the user is Admin, Manager, Team Lead, or HR in the active org.</summary>
        public bool CanManageActiveOrg =>
            string.Equals(ActiveOrgRole, "Admin",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ActiveOrgRole, "Manager",   StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ActiveOrgRole, "Team Lead", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ActiveOrgRole, "HR",        StringComparison.OrdinalIgnoreCase);

        // ── Events ────────────────────────────────────────────────
        public event Func<Task>? OnSessionReady;
        public event Func<Task>? OnOrgSwitched;
        public event Func<Task>? OnProfileChanged;

        /// <summary>
        /// Updates the user's display name in the session (in-memory + persisted),
        /// then fires OnProfileChanged so any subscribed component (e.g. Sidebar) re-renders.
        /// </summary>
        public async Task UpdateFullNameAsync(string fullName)
        {
            FullName = fullName;
            await PersistAsync();

            if (OnProfileChanged != null)
            {
                foreach (var handler in OnProfileChanged.GetInvocationList().Cast<Func<Task>>())
                {
                    try { await handler(); } catch { }
                }
            }
        }

        // ── Fires OnSessionReady — kept for backward compat with AppLayout ──
        public async Task NotifySessionUpdatedAsync()
        {
            if (OnSessionReady != null)
            {
                foreach (var handler in OnSessionReady.GetInvocationList().Cast<Func<Task>>())
                {
                    try { await handler(); } catch { }
                }
            }
        }

        // ── Called after successful login ──────────────────────────
        public async Task SetSessionAsync(LoginResponse response)
        {
            Id           = response.Id;
            FullName     = response.FullName;
            Email        = response.Email;
            Role         = response.Role;
            IsOnboarded  = response.IsOnboarded;

            // Set active org from login response if present
            if (response.ActiveOrganizationId.HasValue)
            {
                ActiveOrganizationId   = response.ActiveOrganizationId;
                ActiveOrganizationName = response.OrganizationName;
                ActiveOrgRole          = response.ActiveOrgRole ?? Role;
            }
            else if (response.OrganizationName != null)
            {
                // Legacy: just the name was returned
                ActiveOrganizationName = response.OrganizationName;
            }

            await PersistAsync();
        }

        /// <summary>
        /// Switch the user's active organization context.
        /// All subsequent data queries will be scoped to this org.
        /// </summary>
        public async Task SwitchOrganizationAsync(int orgId, string orgName, string orgRole)
        {
            ActiveOrganizationId   = orgId;
            ActiveOrganizationName = orgName;
            ActiveOrgRole          = orgRole;

            await PersistAsync();

            if (OnOrgSwitched != null)
            {
                foreach (var handler in OnOrgSwitched.GetInvocationList().Cast<Func<Task>>())
                {
                    try { await handler(); } catch { }
                }
            }
        }

        public void SetSession(LoginResponse response)
        {
            Id                     = response.Id;
            FullName               = response.FullName;
            Email                  = response.Email;
            Role                   = response.Role;
            IsOnboarded            = response.IsOnboarded;
            ActiveOrganizationName = response.OrganizationName;
            if (response.ActiveOrganizationId.HasValue)
            {
                ActiveOrganizationId = response.ActiveOrganizationId;
                ActiveOrgRole        = response.ActiveOrgRole ?? Role;
            }
        }

        public async Task<bool> TryRestoreAsync()
        {
            try
            {
                var json = await _js.InvokeAsync<string?>("SessionStorage.load", "staffzen_session");
                if (string.IsNullOrWhiteSpace(json)) return false;

                var dto = JsonSerializer.Deserialize<SessionDto>(json);
                if (dto == null || string.IsNullOrEmpty(dto.Role)) return false;

                Id                     = dto.Id;
                FullName               = dto.FullName;
                Email                  = dto.Email;
                Role                   = dto.Role;
                IsOnboarded            = dto.IsOnboarded;
                ActiveOrganizationId   = dto.ActiveOrganizationId;
                ActiveOrganizationName = dto.ActiveOrganizationName;
                ActiveOrgRole          = dto.ActiveOrgRole;

                if (OnSessionReady != null)
                {
                    foreach (var handler in OnSessionReady.GetInvocationList().Cast<Func<Task>>())
                    {
                        try { await handler(); } catch { }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        public async Task PersistAsync()
        {
            try
            {
                var dto  = new SessionDto
                {
                    Id                     = Id,
                    FullName               = FullName,
                    Email                  = Email,
                    Role                   = Role,
                    IsOnboarded            = IsOnboarded,
                    ActiveOrganizationId   = ActiveOrganizationId,
                    ActiveOrganizationName = ActiveOrganizationName,
                    ActiveOrgRole          = ActiveOrgRole
                };
                var json = JsonSerializer.Serialize(dto);
                await _js.InvokeVoidAsync("SessionStorage.save", "staffzen_session", json);
            }
            catch { }
        }

        public async Task ClearSessionAsync()
        {
            Id                     = 0;
            FullName               = null;
            Email                  = null;
            Role                   = null;
            IsOnboarded            = false;
            ActiveOrganizationId   = null;
            ActiveOrganizationName = null;
            ActiveOrgRole          = null;
            try { await _js.InvokeVoidAsync("SessionStorage.remove", "staffzen_session"); } catch { }
        }

        public void ClearSession()
        {
            Id       = 0;
            FullName = null;
            Email    = null;
            Role     = null;
            _ = Task.Run(async () =>
            {
                try { await _js.InvokeVoidAsync("SessionStorage.remove", "staffzen_session"); } catch { }
            });
        }

        private class SessionDto
        {
            public int     Id                     { get; set; }
            public string? FullName               { get; set; }
            public string? Email                  { get; set; }
            public string? Role                   { get; set; }
            public bool    IsOnboarded            { get; set; }
            public int?    ActiveOrganizationId   { get; set; }
            public string? ActiveOrganizationName { get; set; }
            public string? ActiveOrgRole          { get; set; }
        }
    }
}
