using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.YardController;
using Tellurian.Trains.YardController.Data;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(configure => configure.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = true;
    options.TimestampFormat = "hh:mm:ss ";
}));

builder.Services.AddHostedService<NumericKeypadControllerInputs>();
builder.Services.AddSingleton<IByteStreamFramer, LocoNetFramer>();
builder.Services.AddSingleton<ICommunicationsChannel, SerialDataChannel>();
builder.Services.AddSingleton<ISerialPortAdapter>(new SerialPortAdapter("COM5", 57600, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One));
builder.Services.AddSingleton<ICommunicationsChannel, SerialDataChannel>();
builder.Services.AddSingleton<TrainRouteLockings>();
builder.Services.AddSingleton<IKeyReader, ConsoleKeyReader>();
builder.Services.AddSingleton<IYardController, LocoNetYardController>();
//builder.Services.AddSingleton<IYardController, LoggingYardController>();

builder.Services.AddScoped<ITrainRouteDataSource, TextFileTrainRouteDataSource>(source =>
{
    var logger = source.GetRequiredService<ILogger<ITrainRouteDataSource>>();
    return new TextFileTrainRouteDataSource(logger, "Data\\TrainRoutes.txt");
});
builder.Services.AddScoped<IPointDataSource, TextFilePointDataSource>(source =>
{
    var logger = source.GetRequiredService<ILogger<IPointDataSource>>();
    return new TextFilePointDataSource(logger, "Data\\Points.txt");
});


IHost host = builder.Build();
host.Run();
