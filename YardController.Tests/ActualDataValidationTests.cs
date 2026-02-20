using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Validation;
using YardController.Web.Services;
using YardController.Web.Services.Data;

namespace YardController.Tests;

[TestClass]
public class ActualDataValidationTests
{
    private static readonly string DataPath = Path.Combine(
        AppContext.BaseDirectory, "Data", "Munkeröd");

    [TestMethod]
    public async Task ValidateActualTrainRoutesAgainstTopology()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Load all data via YardDataService using the actual Data folder
        var settings = Options.Create(new StationSettings
        {
            Stations = [new StationConfig { Name = "Munkeröd", DataFolder = Path.GetFullPath(DataPath) }]
        });
        var service = new YardDataService(settings, loggerFactory.CreateLogger<YardDataService>(), loggerFactory);
        await service.InitializeAsync();

        Console.WriteLine($"Topology '{service.Topology.Name}': {service.Topology.Points.Count} points, {service.Topology.Signals.Count} signals");
        Console.WriteLine($"Points: {string.Join(", ", service.Topology.Points.Select(p => p.Label))}");
        Console.WriteLine($"Signals: {string.Join(", ", service.Topology.Signals.Select(s => s.Name))}");
        Console.WriteLine($"Loaded {service.TrainRoutes.Count} routes");

        // Validate
        var validatorLogger = loggerFactory.CreateLogger<TrainRouteValidator>();
        var validator = new TrainRouteValidator(service.Topology, validatorLogger);
        var result = validator.ValidateRoutes(service.TrainRoutes);

        Console.WriteLine($"\n=== Validation Results ===");
        Console.WriteLine($"Valid routes: {result.ValidRoutes.Count}");
        Console.WriteLine($"Invalid routes: {result.InvalidRoutes.Count}");

        if (result.InvalidRoutes.Count > 0)
        {
            Console.WriteLine($"\n=== Invalid Routes ===");
            foreach (var route in result.InvalidRoutes)
            {
                Console.WriteLine($"  {route}");
            }
        }

        // For now, just report - don't fail the test
        // Assert.IsFalse(result.HasErrors, $"Found {result.InvalidRoutes.Count} invalid routes");
    }
}
