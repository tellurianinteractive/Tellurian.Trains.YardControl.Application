using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using YardController.Web.Services;
using YardController.Web.Services.Data;

namespace YardController.Tests;

[TestClass]
public class TrainRouteDataSourceTests
{
    private string _tempDir = null!;
    private string _routesPath = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _routesPath = Path.Combine(_tempDir, "TrainRoutes.txt");
        // Create minimal topology file required by YardDataService
        File.WriteAllText(Path.Combine(_tempDir, "Topology.txt"), "TestStation\n[Tracks]\n");
        // Create empty Points.txt to avoid warnings
        File.WriteAllText(Path.Combine(_tempDir, "Points.txt"), "");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private async Task<YardDataService> CreateAndInitialize(string routesContent)
    {
        File.WriteAllText(_routesPath, routesContent);
        var settings = Options.Create(new StationSettings
        {
            Stations = [new StationConfig { Name = "Test", DataFolder = _tempDir }]
        });
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var service = new YardDataService(settings, loggerFactory.CreateLogger<YardDataService>(), loggerFactory);
        await service.InitializeAsync();
        return service;
    }

    #region File Not Found Tests

    [TestMethod]
    public async Task GetTrainRouteCommands_ReturnsEmpty_WhenFileNotFound()
    {
        // Don't write TrainRoutes.txt - delete the one from TestInitialize
        File.Delete(_routesPath);
        var settings = Options.Create(new StationSettings
        {
            Stations = [new StationConfig { Name = "Test", DataFolder = _tempDir }]
        });
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var service = new YardDataService(settings, loggerFactory.CreateLogger<YardDataService>(), loggerFactory);
        await service.InitializeAsync();

        Assert.IsEmpty(service.TrainRoutes);
    }

    #endregion

    #region Empty File Tests

    [TestMethod]
    public async Task GetTrainRouteCommands_ReturnsEmpty_WhenFileIsEmpty()
    {
        var service = await CreateAndInitialize("");

        Assert.IsEmpty(service.TrainRoutes);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_ReturnsEmpty_WhenFileHasOnlyWhitespace()
    {
        var service = await CreateAndInitialize("   \n\n   \n");

        Assert.IsEmpty(service.TrainRoutes);
    }

    #endregion

    #region Basic Format Parsing Tests

    [TestMethod]
    public async Task GetTrainRouteCommands_ParsesBasicFormat()
    {
        var service = await CreateAndInitialize("21-31:1+,3-");

        Assert.HasCount(1, service.TrainRoutes);
        Assert.AreEqual(21, service.TrainRoutes[0].FromSignal);
        Assert.AreEqual(31, service.TrainRoutes[0].ToSignal);
        Assert.HasCount(2, service.TrainRoutes[0].PointCommands);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_ParsesMultipleLines()
    {
        var service = await CreateAndInitialize("21-31:1+,3-\n31-41:5+,7-");

        Assert.HasCount(2, service.TrainRoutes);
        Assert.AreEqual(21, service.TrainRoutes[0].FromSignal);
        Assert.AreEqual(31, service.TrainRoutes[1].FromSignal);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_ParsesSinglePointCommand()
    {
        var service = await CreateAndInitialize("21-31:1+");

        Assert.HasCount(1, service.TrainRoutes);
        Assert.HasCount(1, service.TrainRoutes[0].PointCommands);
    }

    #endregion

    #region Composite Format Parsing Tests

    [TestMethod]
    public async Task GetTrainRouteCommands_ParsesCompositeFormat()
    {
        // First define base routes, then composite
        var service = await CreateAndInitialize("21-31:1+,3-\n31-41:5+,7-\n21-41:21.31.41");

        Assert.HasCount(3, service.TrainRoutes);

        var compositeRoute = service.TrainRoutes[2];
        Assert.AreEqual(21, compositeRoute.FromSignal);
        Assert.AreEqual(41, compositeRoute.ToSignal);
        // Should have combined points from 21-31 and 31-41
        Assert.HasCount(4, compositeRoute.PointCommands);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_CompositeRoute_IncludesFoundSegmentsEvenIfSomeMissing()
    {
        // Composite route where one base route exists (21-31) but another (31-51) doesn't.
        // YardDataService includes the composite with whatever points it finds.
        var service = await CreateAndInitialize("21-31:1+\n21-51:21.31.51");

        Assert.HasCount(2, service.TrainRoutes);
        var compositeRoute = service.TrainRoutes[1];
        Assert.AreEqual(21, compositeRoute.FromSignal);
        Assert.AreEqual(51, compositeRoute.ToSignal);
        // Only points from the found segment (21-31)
        Assert.HasCount(1, compositeRoute.PointCommands);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_CompositeRoute_WithThreeSegments()
    {
        var service = await CreateAndInitialize("21-31:1+\n31-41:2+\n41-51:3+\n21-51:21.31.41.51");

        Assert.HasCount(4, service.TrainRoutes);

        var compositeRoute = service.TrainRoutes[3];
        Assert.AreEqual(21, compositeRoute.FromSignal);
        Assert.AreEqual(51, compositeRoute.ToSignal);
        Assert.HasCount(3, compositeRoute.PointCommands);
    }

    #endregion

    #region Invalid Format Tests

    [TestMethod]
    public async Task GetTrainRouteCommands_SkipsInvalidFormat_MissingColon()
    {
        var service = await CreateAndInitialize("21-31,1+,3-");

        Assert.IsEmpty(service.TrainRoutes);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_SkipsInvalidFormat_MissingHyphen()
    {
        var service = await CreateAndInitialize("2131:1+,3-");

        Assert.IsEmpty(service.TrainRoutes);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_ContinuesAfterInvalidLine()
    {
        var service = await CreateAndInitialize("invalid-line\n21-31:1+");

        Assert.HasCount(1, service.TrainRoutes);
        Assert.AreEqual(21, service.TrainRoutes[0].FromSignal);
    }

    #endregion

    #region Point Command Handling in Composite Routes

    [TestMethod]
    public async Task GetTrainRouteCommands_CompositeRoute_CombinesPointCommands()
    {
        // Routes share point 2 - Distinct() is called but compares by reference
        // so duplicates may still exist. This test documents actual behavior.
        var service = await CreateAndInitialize("21-31:1+,2+\n31-41:2+,3+\n21-41:21.31.41");

        var compositeRoute = service.TrainRoutes[2];
        // Distinct() uses PointCommand's Equals which checks Number, Position, AND Addresses
        // Since addresses are empty and different instances, they are not deduplicated
        Assert.HasCount(4, compositeRoute.PointCommands);
    }

    #endregion

    #region Off-Route Point (x prefix) Tests

    [TestMethod]
    public async Task GetTrainRouteCommands_ParsesOffRoutePoints_WithXPrefix()
    {
        var service = await CreateAndInitialize("35-95:25+,x33-");

        Assert.HasCount(1, service.TrainRoutes);
        Assert.HasCount(2, service.TrainRoutes[0].PointCommands);

        var pointCommands = service.TrainRoutes[0].PointCommands.ToList();
        Assert.AreEqual(25, pointCommands[0].Number);
        Assert.IsTrue(pointCommands[0].IsOnRoute);
        Assert.AreEqual(33, pointCommands[1].Number);
        Assert.IsFalse(pointCommands[1].IsOnRoute);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_MixedOnAndOffRoutePoints()
    {
        var service = await CreateAndInitialize("21-31:1+,x2-,3+,x4-");

        Assert.HasCount(1, service.TrainRoutes);
        var route = service.TrainRoutes[0];
        Assert.HasCount(4, route.PointCommands);

        Assert.HasCount(2, route.OnRoutePoints);
        Assert.HasCount(2, route.OffRoutePoints);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_OffRoutePoints_AreMarkedCorrectly()
    {
        var service = await CreateAndInitialize("21-31:x1+,x2-");

        Assert.HasCount(1, service.TrainRoutes);
        var route = service.TrainRoutes[0];

        Assert.IsEmpty(route.OnRoutePoints);
        Assert.HasCount(2, route.OffRoutePoints);

        var offRoutePoints = route.OffRoutePoints.ToList();
        Assert.AreEqual(1, offRoutePoints[0].Number);
        Assert.AreEqual(PointPosition.Straight, offRoutePoints[0].Position);
        Assert.AreEqual(2, offRoutePoints[1].Number);
        Assert.AreEqual(PointPosition.Diverging, offRoutePoints[1].Position);
    }

    [TestMethod]
    public async Task GetTrainRouteCommands_UppercaseXPrefix_WorksCorrectly()
    {
        var service = await CreateAndInitialize("21-31:1+,X2-");

        Assert.HasCount(1, service.TrainRoutes);
        var route = service.TrainRoutes[0];
        var pointCommands = route.PointCommands.ToList();

        Assert.IsTrue(pointCommands[0].IsOnRoute);
        Assert.IsFalse(pointCommands[1].IsOnRoute);
    }

    #endregion
}
