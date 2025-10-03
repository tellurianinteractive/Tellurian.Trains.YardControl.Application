using Tellurian.Trains.YardController;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddHostedService<NumericKeypadControllerInputs>();
builder.Services.AddSingleton<IYardController, LoggingYardController>();
builder.Services.AddScoped<ITrainPathDataSource, TextFileTrainPathDataSource>(_ => new TextFileTrainPathDataSource("Data"));
builder.Services.AddLogging(configure => configure.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = true;
    options.TimestampFormat = "hh:mm:ss ";
}));

IHost host = builder.Build();
host.Run();

