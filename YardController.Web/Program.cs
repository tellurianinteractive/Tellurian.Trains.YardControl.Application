using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Components;
using YardController.Web.LocoNet;
using YardController.Web.Services;
using YardController.Web.Services.Data;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseStaticWebAssets();
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization();

// Configure settings from appsettings.json
builder.Services.Configure<StationSettings>(builder.Configuration);
builder.Services.Configure<SerialPortSettings>(builder.Configuration.GetSection("SerialPort"));

// Add yard data service as singleton (coordinates all data loading, file watching, and validation)
builder.Services.AddSingleton<YardDataService>();
builder.Services.AddSingleton<IYardDataService>(sp => sp.GetRequiredService<YardDataService>());

// Add yard control services
builder.Services.AddHostedService<NumericKeypadControllerInputs>();
builder.Services.AddSingleton<TrainRouteLockings>();
builder.Services.AddSingleton<BufferedKeyReader>();
builder.Services.AddSingleton<IKeyReader>(sp => sp.GetRequiredService<BufferedKeyReader>());
builder.Services.AddSingleton<IBufferedKeyReader>(sp => sp.GetRequiredService<BufferedKeyReader>());
builder.Services.AddSingleton<TrainRouteNotificationService>();
builder.Services.AddSingleton<ITrainRouteNotificationService>(sp => sp.GetRequiredService<TrainRouteNotificationService>());
builder.Services.AddSingleton<PointNotificationService>();
builder.Services.AddSingleton<IPointNotificationService>(sp => sp.GetRequiredService<PointNotificationService>());
builder.Services.AddSingleton<SignalNotificationService>();
builder.Services.AddSingleton<ISignalNotificationService>(sp => sp.GetRequiredService<SignalNotificationService>());
builder.Services.AddSingleton<TrainNumberService>();
builder.Services.AddSingleton<ITrainNumberService>(sp => sp.GetRequiredService<TrainNumberService>());

// Keyboard capture (scoped per circuit - IJSRuntime is circuit-scoped)
builder.Services.AddScoped<KeyboardCaptureService>();

// Yard controller and point position feedback - use logging services for development, LocoNet for production
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IYardController, LoggingYardController>();
    builder.Services.AddSingleton<LoggingPointPositionService>();
    builder.Services.AddSingleton<IPointPositionService>(sp => sp.GetRequiredService<LoggingPointPositionService>());
    builder.Services.AddSingleton<LoggingSignalStateService>();
    builder.Services.AddSingleton<ISignalStateService>(sp => sp.GetRequiredService<LoggingSignalStateService>());
}
else
{
    // LocoNet hardware communication
    builder.Services.AddSingleton<IByteStreamFramer, LocoNetFramer>();
    builder.Services.AddSingleton<ISerialPortAdapter>(sp =>
    {
        var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SerialPortSettings>>().Value;
        return new SerialPortAdapter(settings.PortName, settings.BaudRate, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One);
    });
    builder.Services.AddSingleton<ICommunicationsChannel, SerialDataChannel>();
    builder.Services.AddSingleton<IYardController, LocoNetYardController>();
    builder.Services.AddSingleton<LocoNetPointPositionService>();
    builder.Services.AddSingleton<IPointPositionService>(sp => sp.GetRequiredService<LocoNetPointPositionService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<LocoNetPointPositionService>());
    builder.Services.AddSingleton<LocoNetSignalStateService>();
    builder.Services.AddSingleton<ISignalStateService>(sp => sp.GetRequiredService<LocoNetSignalStateService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<LocoNetSignalStateService>());
}

var app = builder.Build();
app.Logger.LogInformation("Application starting in {Environment} environment", app.Environment.EnvironmentName);

// Initialize yard data service (loads all data files and starts file watchers)
var yardDataService = app.Services.GetRequiredService<YardDataService>();
await yardDataService.InitializeAsync();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

var supportedCultures = new[] { "en", "sv", "da", "nb", "de" };
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

app.UseAntiforgery();

app.MapGet("/culture", (HttpContext context, string culture, string redirectUri) =>
{
    var cultureInfo = CultureInfo.GetCultureInfo(culture);
    context.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(cultureInfo)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

    // Single-user app: set default thread culture for BackgroundService
    CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
    CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

    return Results.Redirect(redirectUri ?? "/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
