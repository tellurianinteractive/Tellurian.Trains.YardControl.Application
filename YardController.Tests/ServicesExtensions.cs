using Microsoft.Extensions.DependencyInjection;
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
