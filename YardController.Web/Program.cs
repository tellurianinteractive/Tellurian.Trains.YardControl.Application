using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Localization;
using Tellurian.Trains.Adapters.Z21;
using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Communications.Interfaces;
using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Components;
using YardController.Web.Hardware;
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
builder.Services.Configure<CommandStationSettings>(builder.Configuration.GetSection("CommandStation"));

// Add yard data service as singleton (coordinates all data loading, file watching, and validation)
builder.Services.AddSingleton<YardDataService>();
builder.Services.AddSingleton<IYardDataService>(sp => sp.GetRequiredService<YardDataService>());

// Add yard control services
builder.Services.AddHostedService<NumericKeypadControllerInputs>();
builder.Services.AddSingleton<TrainRouteLockingsManager>();
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

// Yard controller and feedback services — dev mode uses logging stand-ins; production wires a real
// command station selected by CommandStation:Type (Serial → LocoNet, Z21 → UDP).
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
    var commandStation = builder.Configuration.GetSection("CommandStation").Get<CommandStationSettings>()
        ?? new CommandStationSettings();

    if (string.Equals(commandStation.Type, "Z21", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton<ICommunicationsChannel>(sp =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandStationSettings>>().Value;
            var remoteEndPoint = new IPEndPoint(IPAddress.Parse(settings.Z21.Address), settings.Z21.CommandPort);
            var logger = sp.GetRequiredService<ILogger<UdpDataChannel>>();
            return new UdpDataChannel(settings.Z21.FeedbackPort, remoteEndPoint, logger);
        });
        // Z21 does not cross-broadcast accessory changes between its XpressNet and
        // LocoNet-over-UDP streams, so for third-party clients (Z21 App, WLANMaus) to see
        // our commands — and for us to see theirs — everyone must speak the same protocol.
        // Send accessory commands as native XpressNet (useLocoNetForAccessories: false) and
        // subscribe to RunningAndSwitching, which delivers LAN_X_TURNOUT_INFO for every
        // turnout change on the system. The Z21 bridges XpressNet accessory commands to its
        // LocoNet bus internally so LocoNet-connected accessory decoders still receive them.
        builder.Services.AddSingleton(sp => new Tellurian.Trains.Adapters.Z21.Adapter(
            sp.GetRequiredService<ICommunicationsChannel>(),
            sp.GetRequiredService<ILogger<Tellurian.Trains.Adapters.Z21.Adapter>>(),
            BroadcastSubjects.RunningAndSwitching,
            useLocoNetForAccessories: false,
            minSendIntervalMs: 50));
        builder.Services.AddSingleton<IAccessory>(sp => sp.GetRequiredService<Tellurian.Trains.Adapters.Z21.Adapter>());
        builder.Services.AddSingleton<IObservable<Tellurian.Trains.Communications.Interfaces.Notification>>(sp => sp.GetRequiredService<Tellurian.Trains.Adapters.Z21.Adapter>());
        builder.Services.AddSingleton<IHostedService>(sp => new CommandStationInitializer(
            sp.GetRequiredService<Tellurian.Trains.Adapters.Z21.Adapter>().StartReceiveAsync,
            sp.GetRequiredService<ILogger<CommandStationInitializer>>()));
    }
    else if (string.Equals(commandStation.Type, "Serial", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton<IByteStreamFramer, LocoNetFramer>();
        builder.Services.AddSingleton<ISerialPortAdapter>(sp =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandStationSettings>>().Value;
            return new SerialPortAdapter(settings.SerialPort.PortName, settings.SerialPort.BaudRate, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One);
        });
        builder.Services.AddSingleton<ICommunicationsChannel, SerialDataChannel>();
        builder.Services.AddSingleton<Tellurian.Trains.Adapters.LocoNet.Adapter>();
        builder.Services.AddSingleton<IAccessory>(sp => sp.GetRequiredService<Tellurian.Trains.Adapters.LocoNet.Adapter>());
        builder.Services.AddSingleton<IObservable<Tellurian.Trains.Communications.Interfaces.Notification>>(sp => sp.GetRequiredService<Tellurian.Trains.Adapters.LocoNet.Adapter>());
        builder.Services.AddSingleton<IHostedService>(sp => new CommandStationInitializer(
            sp.GetRequiredService<Tellurian.Trains.Adapters.LocoNet.Adapter>().StartReceiveAsync,
            sp.GetRequiredService<ILogger<CommandStationInitializer>>()));
    }
    else
    {
        throw new InvalidOperationException(
            $"Unknown CommandStation:Type \"{commandStation.Type}\". Supported values are \"Serial\" and \"Z21\".");
    }

    builder.Services.AddSingleton<IYardController, AccessoryYardController>();
    builder.Services.AddSingleton<AccessoryPointPositionService>();
    builder.Services.AddSingleton<IPointPositionService>(sp => sp.GetRequiredService<AccessoryPointPositionService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AccessoryPointPositionService>());
    builder.Services.AddSingleton<AccessorySignalStateService>();
    builder.Services.AddSingleton<ISignalStateService>(sp => sp.GetRequiredService<AccessorySignalStateService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AccessorySignalStateService>());
}

var app = builder.Build();
app.Logger.LogInformation("Application starting in {Environment} environment", app.Environment.EnvironmentName);

// Validate serial port availability when running against a LocoNet-over-serial command station
if (!app.Environment.IsDevelopment())
{
    var commandStationSettings = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandStationSettings>>().Value;
    if (string.Equals(commandStationSettings.Type, "Serial", StringComparison.OrdinalIgnoreCase))
    {
        var availablePorts = System.IO.Ports.SerialPort.GetPortNames();
        if (!availablePorts.Contains(commandStationSettings.SerialPort.PortName, StringComparer.OrdinalIgnoreCase))
        {
            app.Logger.LogCritical(
                "Configured serial port '{PortName}' not found. Available ports: {AvailablePorts}",
                commandStationSettings.SerialPort.PortName,
                availablePorts.Length > 0 ? string.Join(", ", availablePorts) : "(none)");
            return;
        }
    }
}

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
