using APM.StaffZen.Blazor.Components;
using APM.StaffZen.Blazor.Configuration;
using APM.StaffZen.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Extend SignalR circuit timeouts so the "Attempting to connect to server" toast
// never appears while face recognition is running (which does heavy JS work).
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitMaxRetained = 100;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(5);
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(2);
    options.DetailedErrors = false;
}).AddHubOptions(hub =>
{
    hub.ClientTimeoutInterval        = TimeSpan.FromMinutes(3);   // was default 30s
    hub.HandshakeTimeout             = TimeSpan.FromSeconds(30);
    hub.KeepAliveInterval            = TimeSpan.FromSeconds(15);
    hub.MaximumReceiveMessageSize    = 512 * 1024;                 // 512 KB (selfie base64)
});

// Configure API settings
var apiSettings = builder.Configuration.GetSection("ApiSettings").Get<ApiSettings>();
builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection("ApiSettings"));

// Configure HttpClient for API calls
builder.Services.AddHttpClient("API", client =>
{
    if (apiSettings != null && !string.IsNullOrEmpty(apiSettings.BaseUrl))
    {
        client.BaseAddress = new Uri(apiSettings.BaseUrl);
    }
});

// Register HttpClient for Google Calendar iCal fetching
builder.Services.AddHttpClient("GoogleCalendar", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; StaffZen/1.0)");
    client.Timeout = TimeSpan.FromSeconds(20);
});

// Register application services
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<NotificationSettingsService>();
builder.Services.AddScoped<TimeOffPolicyService>();
builder.Services.AddScoped<OrgService>();
builder.Services.AddScoped<LocationService>();
builder.Services.AddScoped<WorkScheduleService>();
builder.Services.AddScoped<TimeTrackingPolicyService>();
builder.Services.AddScoped<PayPeriodService>();
builder.Services.AddScoped<LeaveRequestService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AttendanceCorrectionService>();
builder.Services.AddScoped<NotificationBus>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection(); // only redirect to HTTPS in production
}
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();