using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.YardController;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(configure => configure.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = true;
    options.TimestampFormat = "hh:mm:ss ";
}));

builder.Services.AddHostedService<NumericKeypadControllerInputs>();

builder.Services.AddSingleton<SwitchLockings>();
builder.Services.AddSingleton<IKeyReader, ConsoleKeyReader>();
builder.Services.AddSingleton<IYardController, LoggingYardController>();

builder.Services.AddScoped<ITrainPathDataSource, TextFileTrainPathDataSource>(source =>
{
    var logger = source.GetRequiredService<ILogger<ITrainPathDataSource>>();
    return new TextFileTrainPathDataSource(logger, "Data\\TrainRoutes.txt");
});
builder.Services.AddScoped<ISwitchDataSource, TextFileSwitchDataSource>(source =>
{
    var logger = source.GetRequiredService<ILogger<ISwitchDataSource>>();
    return new TextFileSwitchDataSource(logger, "Data\\Switches.txt");
});

builder.Services.AddSingleton<ISerialPortAdapter, SerialPortAdapter>(provider => new("COM3:"));
builder.Services.AddSingleton<IByteStreamFramer, LocoNetFramer>();
builder.Services.AddSingleton<ICommunicationsChannel, SerialDataChannel>();

IHost host = builder.Build();
host.Run();
