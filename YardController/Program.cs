using Tellurian.Trains.YardController;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLogging(configure => configure.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = true;
    options.TimestampFormat = "hh:mm:ss ";
}));
builder.Services.AddHttpClient();

builder.Services.AddSingleton<IKeyReader, ConsoleKeyReader>();
builder.Services.AddHostedService<NumericKeypadControllerInputs>();
builder.Services.AddSingleton<IYardController, LoggingYardController>();
builder.Services.AddSingleton<SwitchLockings>();
builder.Services.AddScoped<ITrainPathDataSource, TextFileTrainPathDataSource>(source =>
{
    var logger = source.GetRequiredService<ILogger<ITrainPathDataSource>>();
    return new TextFileTrainPathDataSource(logger, "Data\\TrainPaths.txt");
});
  

IHost host = builder.Build();
host.Run();

