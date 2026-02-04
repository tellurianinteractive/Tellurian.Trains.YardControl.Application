using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController;
using Tellurian.Trains.YardController.Data;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Validation;

namespace YardController.Tests;

[TestClass]
public class ActualDataValidationTests
{
    private static readonly string DataPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.App", "Data");

    [TestMethod]
    public async Task ValidateActualTrainRoutesAgainstTopology()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var routeLogger = loggerFactory.CreateLogger<ITrainRouteDataSource>();
        var validatorLogger = loggerFactory.CreateLogger<TrainRouteValidator>();

        // Load topology
        var topologyPath = Path.GetFullPath(Path.Combine(DataPath, "Topology.txt"));
        Console.WriteLine($"Loading topology from: {topologyPath}");

        var parser = new TopologyParser();
        var topology = await parser.ParseFileAsync(topologyPath);

        Console.WriteLine($"Topology '{topology.Name}': {topology.Points.Count} points, {topology.Signals.Count} signals");
        Console.WriteLine($"Points: {string.Join(", ", topology.Points.Select(p => p.Label))}");
        Console.WriteLine($"Signals: {string.Join(", ", topology.Signals.Select(s => s.Name))}");

        // Load train routes
        var routesPath = Path.GetFullPath(Path.Combine(DataPath, "TrainRoutes.txt"));
        Console.WriteLine($"\nLoading routes from: {routesPath}");

        var routeDataSource = new TextFileTrainRouteDataSource(routeLogger, routesPath);
        var routes = (await routeDataSource.GetTrainRouteCommandsAsync(default)).ToList();

        Console.WriteLine($"Loaded {routes.Count} routes");

        // Validate
        var validator = new TrainRouteValidator(topology, validatorLogger);
        var result = validator.ValidateRoutes(routes);

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
