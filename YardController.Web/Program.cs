using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Components;
using YardController.Web.LocoNet;
using YardController.Web.Services;
using YardController.Web.Services.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure settings from appsettings.json
builder.Services.Configure<PointDataSourceSettings>(builder.Configuration.GetSection("PointDataSource"));
builder.Services.Configure<TrainRouteDataSourceSettings>(builder.Configuration.GetSection("TrainRouteDataSource"));
builder.Services.Configure<TopologyServiceSettings>(builder.Configuration.GetSection("TopologyService"));

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

// Data sources (for backward compatibility with tests)
builder.Services.AddSingleton<IPointDataSource, TextFilePointDataSource>();
builder.Services.AddSingleton<ITrainRouteDataSource, TextFileTrainRouteDataSource>();

// Yard controller - use LoggingYardController for development, LocoNetYardController for production
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IYardController, LoggingYardController>();
}
else
{
    // LocoNet hardware communication
    builder.Services.AddSingleton<IByteStreamFramer, LocoNetFramer>();
    builder.Services.AddSingleton<ISerialPortAdapter>(new SerialPortAdapter("COM3", 57600, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One));
    builder.Services.AddSingleton<ICommunicationsChannel, SerialDataChannel>();
    builder.Services.AddSingleton<IYardController, LocoNetYardController>();
}

var app = builder.Build();

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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
