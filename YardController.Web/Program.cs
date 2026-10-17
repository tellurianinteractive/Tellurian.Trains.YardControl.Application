using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Localization;
using Tellurian.Trains.Adapters.Z21;
using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Communications.Interfaces;
using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web;
using YardController.Web.Components;
using YardController.Web.Hardware;
using YardController.Web.Services;
using Tellurian.Trains.YardController.Panel.Hardware;
using Tellurian.Trains.YardController.Panel.Rendering;
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
builder.Services.AddSingleton<ShuntingAreaService>();
builder.Services.AddSingleton<IShuntingAreaService>(sp => sp.GetRequiredService<ShuntingAreaService>());
// Seeded with the default the request localisation below uses, not the machine's own culture:
// a panel lettered from the operating system while the diagram is lettered from configuration
// would be two languages in one room.
// The language this installation is worked in, from configuration. It seeds both the panel and
// the request localisation below, so a yard whose operator never touches the picker still has its
// keys and its diagram lettered alike — and the panel is right before any browser has been opened.
var configuredLanguage = builder.Configuration["Language"] ?? OperatorLanguageService.DefaultLanguage;
builder.Services.AddSingleton(new OperatorLanguageService(configuredLanguage));
builder.Services.AddSingleton<IOperatorLanguageService>(sp => sp.GetRequiredService<OperatorLanguageService>());
builder.Services.AddSingleton<EntryStateService>();
builder.Services.AddSingleton<IEntryStateService>(sp => sp.GetRequiredService<EntryStateService>());

// The operator's button panel. It is wired whenever any station is configured for one; whether
// the hardware is actually attached is the panel's own business, and a station worked with a
// keypad simply never sees it. Absence of the device must never prevent start-up (§4), so a
// machine with no module gets the null panel and the service runs as a no-op.
var stationSettings = builder.Configuration.Get<StationSettings>() ?? new StationSettings();
var wantsPanel = stationSettings.Stations.Any(s => s.InputDevice == InputDevice.StreamDeck);
if (wantsPanel)
{
    builder.Services.AddSingleton(sp => new PanelKeyFaces());
    builder.Services.AddSingleton(builder.Configuration.GetSection("Panel").Get<StreamDeckPanelOptions>()
        ?? new StreamDeckPanelOptions());
    builder.Services.AddSingleton<IButtonPanel>(sp => new StreamDeckButtonPanel(
        sp.GetRequiredService<StreamDeckPanelOptions>(),
        sp.GetRequiredService<ILogger<StreamDeckButtonPanel>>()));
    builder.Services.AddHostedService<PanelControllerInputs>();
}

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
    builder.Services.AddSingleton<LoggingOccupancySectionService>();
    builder.Services.AddLatchingOccupancy<LoggingOccupancySectionService>();
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
        // OccupiedStretch adds the LocoNet detector broadcasts the track detection stretches
        // are read from; without it the Z21 reports no occupancy at all.
        // LocoNetTurnouts adds the switch traffic on the Z21's LocoNet bus (OPC_SW_REQ/OPC_SW_REP),
        // which is where a LocoNet-connected accessory decoder reports the position it actually
        // reached. LAN_X_TURNOUT_INFO alone is only the Z21's record of what was last commanded.
        builder.Services.AddSingleton(sp => new Tellurian.Trains.Adapters.Z21.Adapter(
            sp.GetRequiredService<ICommunicationsChannel>(),
            sp.GetRequiredService<ILogger<Tellurian.Trains.Adapters.Z21.Adapter>>(),
            BroadcastSubjects.RunningAndSwitching | BroadcastSubjects.LocoNetTurnouts | BroadcastSubjects.OccupiedStretch,
            useLocoNetForAccessories: false,
            minSendIntervalMs: 50));
        builder.Services.AddSingleton<IAccessory>(sp => sp.GetRequiredService<Tellurian.Trains.Adapters.Z21.Adapter>());
        builder.Services.AddSingleton<IObservable<Tellurian.Trains.Communications.Interfaces.Notification>>(sp => sp.GetRequiredService<Tellurian.Trains.Adapters.Z21.Adapter>());
        builder.Services.AddSingleton<IHostedService>(sp => new CommandStationInitializer(
            sp.GetRequiredService<Tellurian.Trains.Adapters.Z21.Adapter>().StartReceiveAsync,
            sp.GetRequiredService<ILogger<CommandStationInitializer>>()));
        // The Z21 forgets a client — and its broadcast flags with it — after a minute of silence,
        // which a yard between point commands easily is. See Z21KeepAliveService.
        builder.Services.AddHostedService<Z21KeepAliveService>();
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
    builder.Services.AddSingleton<AccessoryOccupancySectionService>();
    builder.Services.AddLatchingOccupancy<AccessoryOccupancySectionService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AccessoryOccupancySectionService>());
}

var app = builder.Build();
StartupLog.ApplicationStarting(app.Logger, app.Environment.EnvironmentName);
app.LogListeningAddresses();

// Validate serial port availability when running against a LocoNet-over-serial command station
if (!app.Environment.IsDevelopment())
{
    var commandStationSettings = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandStationSettings>>().Value;
    if (string.Equals(commandStationSettings.Type, "Serial", StringComparison.OrdinalIgnoreCase))
    {
        var availablePorts = System.IO.Ports.SerialPort.GetPortNames();
        if (!availablePorts.Contains(commandStationSettings.SerialPort.PortName, StringComparer.OrdinalIgnoreCase))
        {
            StartupLog.SerialPortNotFound(
                app.Logger,
                commandStationSettings.SerialPort.PortName,
                availablePorts.Length > 0 ? string.Join(", ", availablePorts) : "(none)");
            return;
        }
    }
}

// Initialize yard data service (loads all data files and starts file watchers)
var yardDataService = app.Services.GetRequiredService<YardDataService>();
await yardDataService.InitializeAsync();

// Configure the HTTP request pipeline. The application is served over plain HTTP on every network
// interface (Kestrel:Endpoints in appsettings.json) so a tablet or a second computer on the layout's
// local network can reach it. There is no HTTPS binding: a certificate would have to be valid for
// whatever address the operator types, which on a layout network is an IP that changes with the DHCP
// lease. So neither HTTPS redirection nor HSTS is used — both would send the browser to a port
// nothing listens on, and HSTS would keep it going there long after the application had stopped.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// One list for both, so the diagram and the panel can never be lettered from different sets.
var supportedCultures = OperatorLanguageService.SupportedLanguages.ToArray();
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture(app.Services.GetRequiredService<OperatorLanguageService>().Language)
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

// The operator's choice of language lives in a cookie, and only a request can read one. A panel
// has no request: left to itself it starts in the configured default and stays there until the
// picker is next used, which is why the diagram could come up in Danish beside keys lettered in
// English. So every request tells the panel what the browser actually resolved to — placed after
// the localisation middleware, which is what sets the culture being read. Choosing the language
// already in use raises nothing, so this costs a comparison per request.
app.Use(async (context, next) =>
{
    context.RequestServices.GetRequiredService<IOperatorLanguageService>()
        .SetLanguage(CultureInfo.CurrentUICulture.Name);
    await next(context);
});

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

await app.RunAsync();
