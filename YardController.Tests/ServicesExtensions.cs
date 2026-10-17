using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Panel.Rendering;
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

        /// <summary>
        /// The same container with a button panel in it, standing in for the hardware. Kept apart
        /// from the default so the panel service is not started by every test that wants a yard.
        /// </summary>
        public static ServiceProvider InstanceWithPanel =>
             new ServiceCollection()
             .AddServices()
             .AddPanel()
             .BuildServiceProvider();
    }

    extension(IServiceCollection services)
    {
        public IServiceCollection AddServices()
        {
            services.AddSingleton<IHostedService, NumericKeypadControllerInputs>();
            services.AddSingleton<TestKeyReader>();
            services.AddSingleton<IKeyReader>(sp => sp.GetRequiredService<TestKeyReader>());
            services.AddSingleton<IBufferedKeyReader>(sp => sp.GetRequiredService<TestKeyReader>());
            services.AddSingleton<IYardController, TestYardController>();
            services.AddSingleton<TestYardDataService>();
            services.AddSingleton<IYardDataService>(sp => sp.GetRequiredService<TestYardDataService>());
            services.AddSingleton<TrainRouteLockingsManager>();
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
            services.AddSingleton(new OperatorLanguageService("en"));
            services.AddSingleton<IOperatorLanguageService>(sp => sp.GetRequiredService<OperatorLanguageService>());
            services.AddSingleton<EntryStateService>();
            services.AddSingleton<IEntryStateService>(sp => sp.GetRequiredService<EntryStateService>());
            services.AddSingleton<ShuntingAreaService>();
            services.AddSingleton<IShuntingAreaService>(sp => sp.GetRequiredService<ShuntingAreaService>());
            services.AddSingleton<LoggingOccupancySectionService>();
            services.AddLatchingOccupancy<LoggingOccupancySectionService>();
            services.AddSingleton<IHostEnvironment, TestHostEnvironment>();
            services.AddLogging(configure => configure.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "hh:mm:ss ";
            }));

            return services;
        }

        /// <summary>The panel service and a stand-in panel a test can press keys on.</summary>
        public IServiceCollection AddPanel()
        {
            services.AddSingleton<TestButtonPanel>();
            services.AddSingleton<IButtonPanel>(sp => sp.GetRequiredService<TestButtonPanel>());
            services.AddSingleton(sp => new PanelKeyFaces(new KeyFaceOptions { Size = 96 }));
            services.AddSingleton<PanelControllerInputs>();
            return services;
        }
    }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Created by the service provider.")]
internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "YardController.Tests";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
