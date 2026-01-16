using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController;
using Tellurian.Trains.YardController.Data;
using Tellurian.Trains.YardController.Tests;

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
            services.AddSingleton<IHostedService,NumericKeypadControllerInputs>();
            services.AddSingleton<IKeyReader, TestKeyReader>();
            services.AddSingleton<IYardController, TestYardController>();
            services.AddSingleton<ITrainRouteDataSource, InMemoryTrainRouteDataSource>();
            services.AddSingleton<IPointDataSource, InMemoryPointDataSource>();
            services.AddSingleton<TrainRouteLockings>();
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
