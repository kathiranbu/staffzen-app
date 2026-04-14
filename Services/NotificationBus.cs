namespace APM.StaffZen.Blazor.Services
{
    /// <summary>
    /// Scoped event bus that allows any page/component to push an in-app bell notification
    /// to the BasePage that is currently wrapping it in the same Blazor circuit.
    ///
    /// Usage:
    ///   1. BasePage subscribes: NotificationBus.OnNotify += HandleNotify;
    ///   2. Any child page raises: await NotificationBus.NotifyAsync("Title", "Body");
    ///
    /// Because Blazor Server circuits are per-connection, a scoped service naturally
    /// scopes the event to the correct user session.
    /// </summary>
    public class NotificationBus
    {
        // Subscribers receive (title, body) pairs
        public event Func<string, string, Task>? OnNotify;

        /// <summary>
        /// Raised whenever attendance records are created or updated (approve / reject / manual edit).
        /// The Sidebar subscribes to this to keep the unmarked-count badge in sync.
        /// </summary>
        public event Func<Task>? OnAttendanceUpdated;

        /// <summary>
        /// Raises an in-app notification to all current subscribers (typically just BasePage).
        /// Fire-and-forget safe — exceptions in handlers are swallowed.
        /// </summary>
        public async Task NotifyAsync(string title, string body)
        {
            if (OnNotify == null) return;
            foreach (var handler in OnNotify.GetInvocationList().Cast<Func<string, string, Task>>())
            {
                try { await handler(title, body); }
                catch { /* never propagate handler failures */ }
            }
        }

        /// <summary>
        /// Signals that attendance data has changed so the sidebar badge can refresh.
        /// Fire-and-forget safe — exceptions in handlers are swallowed.
        /// </summary>
        public async Task NotifyAttendanceUpdatedAsync()
        {
            if (OnAttendanceUpdated == null) return;
            foreach (var handler in OnAttendanceUpdated.GetInvocationList().Cast<Func<Task>>())
            {
                try { await handler(); }
                catch { /* never propagate handler failures */ }
            }
        }
    }
}
