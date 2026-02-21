using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Services;
using YardController.Web.Services.Data;
using YardController.Web.Services.Testing;

namespace YardController.Tests;

public static class ServicesExtensions
{
    extension(ServiceProvider serviceProvider)
    {
        public static ServiceProvider InstanceForTesting =>
             new ServiceCollection()
             .AddServices()
             .BuildServiceProvider();
    }

    extension(IServiceCollection services)
    {
        public IServiceCollection AddServices()
        {
            services.AddSingleton<IHostedService, NumericKeypadControllerInputs>();
            services.AddSingleton<IKeyReader, TestKeyReader>();
            services.AddSingleton<IYardController, TestYardController>();
            services.AddSingleton<TestYardDataService>();
            services.AddSingleton<IYardDataService>(sp => sp.GetRequiredService<TestYardDataService>());
            services.AddSingleton<TrainRouteLockings>();
            services.AddSingleton<ITrainRouteNotificationService, TrainRouteNotificationService>();
            services.AddSingleton<IPointNotificationService, PointNotificationService>();
            services.AddSingleton<LoggingPointPositionService>();
            services.AddSingleton<IPointPositionService>(sp => sp.GetRequiredService<LoggingPointPositionService>());
            services.AddSingleton<SignalNotificationService>();
            services.AddSingleton<ISignalNotificationService>(sp => sp.GetRequiredService<SignalNotificationService>());
            services.AddSingleton<LoggingSignalStateService>();
            services.AddSingleton<ISignalStateService>(sp => sp.GetRequiredService<LoggingSignalStateService>());
            services.AddSingleton<TrainNumberService>();
            services.AddSingleton<ITrainNumberService>(sp => sp.GetRequiredService<TrainNumberService>());
            services.AddSingleton<IHostEnvironment, TestHostEnvironment>();
            services.AddLogging(configure => configure.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "hh:mm:ss ";
            }));

            return services;
        }
    }
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "YardController.Tests";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
