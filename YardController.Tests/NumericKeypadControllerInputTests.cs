using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using YardController.Web.Services;
using YardController.Web.Services.Testing;

namespace YardController.Tests;

[TestClass]
public class NumericKeypadControllerInputTests
{
    ServiceProvider ServiceProvider = default!;
    IHostedService Sut = default!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        ServiceProvider = ServiceProvider.InstanceForTesting;
        Sut = ServiceProvider.GetRequiredService<IHostedService>();
        Assert.IsNotNull(ServiceProvider, "Service provider not resolved.");
        Assert.IsNotNull(Sut, "SUT not resolved.");
        await Task.Delay(10, default);

    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        if (ServiceProvider is not null)
        {
            await ServiceProvider.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task StartsAndStops()
    {
        await Sut.StartAsync(default);
        await Task.Delay(200, default);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyBasicPointCommands()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        yardData.AddPoint(1, [801], 1000);
        yardData.AddPoint(2, [802, 803], 1000);
        keyReader?.AddKey('1');
        keyReader?.AddKey('+');
        keyReader?.AddKey('2');
        keyReader?.AddKey('-');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        AssertPointCommands(
            [PointCommand.Create(1, PointPosition.Straight, [801]),
            PointCommand.Create(2, PointPosition.Diverging, [802,803])],
            yardController?.Commands);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyTrainRouteCommands()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        yardData.AddPoint(3, [801], 1000);
        yardData.AddPoint(4, [802, 803], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(12, 22, TrainRouteState.SetMain,
            [new PointCommand(3, PointPosition.Diverging),
             new PointCommand(4, PointPosition.Straight)]));
        keyReader?.AddKey('1');
        keyReader?.AddKey('2');
        keyReader?.AddKey('2');
        keyReader?.AddKey('2');
        keyReader?.AddKey('#');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        AssertPointCommands(
           [PointCommand.Create(3, PointPosition.Diverging, [801]),
            PointCommand.Create(4, PointPosition.Straight, [802,803])],
           yardController?.Commands);
        await Sut.StopAsync(default);

    }

    [TestMethod]
    public async Task VerifyNonExistentPoint_IsNotSent()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        yardData.AddPoint(1, [801], 1000);
        // Request point 99 which is not defined
        keyReader?.AddKey('9');
        keyReader?.AddKey('9');
        keyReader?.AddKey('+');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // Non-existent point numbers are rejected (logged as warning)
        Assert.AreEqual(0, yardController?.Commands.Count ?? -1);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyClearInputBuffer()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        yardData.AddPoint(1, [801], 1000);
        yardData.AddPoint(9, [809], 1000);
        // Start typing 9, then clear, then type 1+
        keyReader?.AddKey('9');
        keyReader?.AddKey('<'); // Clear
        keyReader?.AddKey('1');
        keyReader?.AddKey('+');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // Should only have point 1 command, not 91+
        AssertPointCommands(
            [PointCommand.Create(1, PointPosition.Straight, [801])],
            yardController?.Commands);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyClearAllTrainRoutes()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");

        yardData.AddPoint(1, [801], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set a train path first
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');
        // Then clear all with //
        keyReader?.AddKey('/');
        keyReader?.AddKey('/');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // Locks should be cleared
        Assert.IsEmpty(pointLockings.PointLocks);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyTrainRouteClearByDestinationSignal()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");

        yardData.AddPoint(1, [801], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set a train path
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');
        // Then clear it with just destination signal: 31/
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('/');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // Locks should be cleared for route ending at signal 31
        Assert.IsEmpty(pointLockings.PointLocks);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyLockedPointPreventsConflictingRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        yardData.AddPoint(1, [801], 1000);
        // Two routes that conflict on point 1
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
        yardData.AddTrainRoute(new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Diverging)]));

        // Set first route (21-31)
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');
        // Try to set conflicting route (22-32)
        keyReader?.AddKey('2');
        keyReader?.AddKey('2');
        keyReader?.AddKey('3');
        keyReader?.AddKey('2');
        keyReader?.AddKey('#');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // Only first route's point command should be sent
        Assert.AreEqual(1, yardController?.Commands.Count ?? 0);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyPointCommandsAlwaysSent()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        yardData.AddPoint(1, [801], 1000);
        yardData.AddPoint(2, [802], 1000);
        // Two routes sharing point 1 with same position
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
        yardData.AddTrainRoute(new TrainRouteCommand(31, 41, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight),
             new PointCommand(2, PointPosition.Diverging)]));

        // Set first route (21-31)
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');
        // Set second route (31-41)
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('4');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // All point commands are sent every time (points can be changed externally)
        // First route: point 1, Second route: point 1 + point 2 = 3 total
        Assert.AreEqual(3, yardController?.Commands.Count ?? 0);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShuntingRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        yardData.AddPoint(1, [801], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set shunting route with *
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('*');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        AssertPointCommands(
            [PointCommand.Create(1, PointPosition.Straight, [801])],
            yardController?.Commands);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyTwoSignalRouteWithDivider()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        yardData.AddPoint(1, [801], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set route using signal divider: 21.31=
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('.');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // Single segment should be set
        AssertPointCommands(
            [PointCommand.Create(1, PointPosition.Straight, [801])],
            yardController?.Commands);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMainRouteToOutboundMainSignal_KeepsToSignalAtStop()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Signal 21 at 1.0 (>), Signal 31 at 1.5 (>) - OutboundMain type
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:\n1.5:31>:u");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500));
        yardData.AddSignal(new Signal("31", 501) { Type = SignalType.OutboundMain });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set main route with # (Enter)
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // FROM signal 21 should be Go; the TO signal ends the route and is never sent Go
        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "FROM signal 21 should be set to Go");
        Assert.IsFalse(yardController.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "TO signal 31 ends the route and must stay at stop");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShuntingRouteToOutboundMainSignal_DoesNotSetToSignalToGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Signal 31 is OutboundMain but shunting route should not set it to Go
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:\n1.5:31>:u");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500));
        yardData.AddSignal(new Signal("31", 501) { Type = SignalType.OutboundMain });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set shunting route with *
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('*');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // FROM signal 21 should be Go, but OutboundMain signal 31 should NOT be Go for shunting
        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "FROM signal 21 should be set to Go");
        Assert.IsFalse(yardController.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "OutboundMain TO signal 31 should NOT be set to Go for shunting route");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMainRouteToNonOutboundMainSignal_DoesNotSetToSignalToGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Signal 31 is Default type (not OutboundMain) - should not get auto Go
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:\n1.5:31>:\n1.10:41>:");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500));
        yardData.AddSignal(new Signal("31", 501));
        yardData.AddSignal(new Signal("41", 502));
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set main route with #
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // FROM signal 21 Go, but TO signal 31 should NOT be Go (not OutboundMain)
        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "FROM signal 21 should be set to Go");
        Assert.IsFalse(yardController.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "Non-OutboundMain TO signal 31 should NOT be set to Go");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShuntingRouteFromInboundMain_DoesNotSetFromSignalToGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Signal 21 is InboundMain (i), Signal 31 is default
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:i\n1.5:31>:");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.InboundMain });
        yardData.AddSignal(new Signal("31", 501));
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set shunting route with *
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('*');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // FROM signal 21 (InboundMain) should NOT be set to Go for shunting route
        Assert.IsFalse(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "InboundMain FROM signal 21 should NOT be set to Go for shunting route");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMainRouteFromInboundMain_SetsFromSignalToGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Signal 21 is InboundMain (i), Signal 31 is exit signal
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:i\n1.5:31>:");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.InboundMain });
        yardData.AddSignal(new Signal("31", 501));
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set main route with #
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // FROM signal 21 (InboundMain) SHOULD be set to Go for main route
        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "InboundMain FROM signal 21 should be set to Go for main route");
        await Sut.StopAsync(default);
    }


    [TestMethod]
    public async Task VerifyMainRouteFromShuntingDwarf_IsSetAsShuntingRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var setRoutes = new List<TrainRouteCommand>();
        notifications.RouteChanged += result =>
        {
            if (result.ResultType == TrainRouteResultType.Set) setRoutes.Add(result.Route);
        };

        // Signal 21 is a shunting dwarf (d), signal 31 is OutboundMain (u)
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:d\n1.5:31>:u");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.ShuntingDwarf });
        yardData.AddSignal(new Signal("31", 501) { Type = SignalType.OutboundMain });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Request a main route with #
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(1, setRoutes.Count, "One route should have been set.");
        Assert.AreEqual(TrainRouteState.SetShunting, setRoutes[0].State,
            "Route from shunting dwarf signal 21 should be set as a shunting route.");
        Assert.IsFalse(yardController!.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "OutboundMain TO signal 31 should NOT be set to Go for a shunting route.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyCompositeMainRouteFromShuntingDwarf_IsSetAsShuntingRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var setRoutes = new List<TrainRouteCommand>();
        notifications.RouteChanged += result =>
        {
            if (result.ResultType == TrainRouteResultType.Set) setRoutes.Add(result.Route);
        };

        // Signal 21 is a shunting dwarf (d) starting a composite route 21.31.35
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10-1.15\n[Features]\n1.0:21>:d\n1.5:31>:\n1.10:35>:u");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddPoint(3, [803], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.ShuntingDwarf });
        yardData.AddSignal(new Signal("31", 501));
        yardData.AddSignal(new Signal("35", 502) { Type = SignalType.OutboundMain });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
        yardData.AddTrainRoute(new TrainRouteCommand(31, 35, TrainRouteState.SetMain,
            [new PointCommand(3, PointPosition.Straight)]));

        foreach (var key in "21.31.35#") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(1, setRoutes.Count, "One composite route should have been set.");
        Assert.AreEqual(TrainRouteState.SetShunting, setRoutes[0].State,
            "Composite route starting at shunting dwarf signal 21 should be set as a shunting route.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMainRouteToShuntingDwarf_IsSetAsShuntingRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var setRoutes = new List<TrainRouteCommand>();
        notifications.RouteChanged += result =>
        {
            if (result.ResultType == TrainRouteResultType.Set) setRoutes.Add(result.Route);
        };

        // Signal 21 is an InboundMain (i), signal 31 is a shunting dwarf (d)
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:i\n1.5:31>:d");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.InboundMain });
        yardData.AddSignal(new Signal("31", 501) { Type = SignalType.ShuntingDwarf });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Request a main route with #
        foreach (var key in "2131#") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(1, setRoutes.Count, "One route should have been set.");
        Assert.AreEqual(TrainRouteState.SetShunting, setRoutes[0].State,
            "Route ending at shunting dwarf signal 31 should be set as a shunting route.");
        Assert.IsFalse(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "InboundMain FROM signal 21 should NOT be set to Go for a shunting route.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMainRouteToBufferStop_IsSetAsShuntingRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var setRoutes = new List<TrainRouteCommand>();
        notifications.RouteChanged += result =>
        {
            if (result.ResultType == TrainRouteResultType.Set) setRoutes.Add(result.Route);
        };

        // Signal 21 is an InboundMain (i); 47 is the buffer stop closing the dead end at 1.10
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:i\n1.10:47>s");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.InboundMain });
        yardData.AddSignal(new Signal("47") { Type = SignalType.BufferStop });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 47, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Request a main route with #
        foreach (var key in "2147#") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(1, setRoutes.Count, "One route should have been set.");
        Assert.AreEqual(TrainRouteState.SetShunting, setRoutes[0].State,
            "A movement into a dead end ending at buffer stop 47 can only be a shunting route.");
        Assert.IsFalse(yardController!.SignalCommands.Any(c => c.SignalNumber == 47),
            "A buffer stop is never commanded: it has no aspect to show.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyCompositeMainRouteToShuntingDwarf_IsSetAsShuntingRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var setRoutes = new List<TrainRouteCommand>();
        notifications.RouteChanged += result =>
        {
            if (result.ResultType == TrainRouteResultType.Set) setRoutes.Add(result.Route);
        };

        // Composite route 21.31.35 ending at the shunting dwarf (d) signal 35
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10-1.15\n[Features]\n1.0:21>:i\n1.5:31>:\n1.10:35>:d");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddPoint(3, [803], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.InboundMain });
        yardData.AddSignal(new Signal("31", 501));
        yardData.AddSignal(new Signal("35", 502) { Type = SignalType.ShuntingDwarf });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
        yardData.AddTrainRoute(new TrainRouteCommand(31, 35, TrainRouteState.SetMain,
            [new PointCommand(3, PointPosition.Straight)]));

        foreach (var key in "21.31.35#") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(1, setRoutes.Count, "One composite route should have been set.");
        Assert.AreEqual(TrainRouteState.SetShunting, setRoutes[0].State,
            "Composite route ending at shunting dwarf signal 35 should be set as a shunting route.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShuntingRouteKeepsMainSignalOnRouteAtStop()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Composite shunting route 21.31.35 where the intermediate signal 31 is an inbound main signal
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10-1.15\n[Features]\n1.0:21>:d\n1.5:31>:i\n1.10:35>:h");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddPoint(3, [803], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.ShuntingDwarf });
        yardData.AddSignal(new Signal("31", 501) { Type = SignalType.InboundMain });
        yardData.AddSignal(new Signal("35", 502) { Type = SignalType.MainDwarf });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
        yardData.AddTrainRoute(new TrainRouteCommand(31, 35, TrainRouteState.SetMain,
            [new PointCommand(3, PointPosition.Straight)]));

        foreach (var key in "21.31.35*") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "Shunting dwarf FROM signal 21 should be set to Go.");
        Assert.IsFalse(yardController.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "InboundMain intermediate signal 31 should stay at Stop for a shunting route.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShuntingRouteClearsMainDwarfSignalOnRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Same composite route, but the intermediate signal 31 has a dwarf part (h)
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10-1.15\n[Features]\n1.0:21>:d\n1.5:31>:h\n1.10:35>:h");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddPoint(3, [803], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.ShuntingDwarf });
        yardData.AddSignal(new Signal("31", 501) { Type = SignalType.MainDwarf });
        yardData.AddSignal(new Signal("35", 502) { Type = SignalType.MainDwarf });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
        yardData.AddTrainRoute(new TrainRouteCommand(31, 35, TrainRouteState.SetMain,
            [new PointCommand(3, PointPosition.Straight)]));

        foreach (var key in "21.31.35*") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "MainDwarf intermediate signal 31 should be set to Go so its dwarf part shows proceed.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShuntingRouteFromOutboundMain_DoesNotSetFromSignalToGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:u\n1.5:31>:h");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.OutboundMain });
        yardData.AddSignal(new Signal("31", 501) { Type = SignalType.MainDwarf });
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        foreach (var key in "2131*") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.IsFalse(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "OutboundMain FROM signal 21 should NOT be set to Go for a shunting route.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyKeypadCascadesSlaveCommand()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Point 10 (master) — when set to -, also set 8 to -.
        yardData.AddPoint(new Point(10, [813], [822], 1000,
            Slaves: [new SlaveCommand(PointPosition.Diverging, 8, PointPosition.Diverging)]));
        yardData.AddPoint(new Point(8, [809], [812], 1000));

        // Type "10-"
        keyReader?.AddKey('1');
        keyReader?.AddKey('0');
        keyReader?.AddKey('-');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        // Both decoder commands fire: master 10 (diverging, 822) and slave 8 (diverging, 812)
        Assert.HasCount(2, yardController!.Commands);
        Assert.IsTrue(yardController.Commands.Any(c => c.Number == 10 && c.Position == PointPosition.Diverging));
        Assert.IsTrue(yardController.Commands.Any(c => c.Number == 8 && c.Position == PointPosition.Diverging));
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyKeypadDoesNotCascadeOnNonMatchingPosition()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Point 10: only the - position cascades. Set 10+ → no cascade.
        yardData.AddPoint(new Point(10, [813], [822], 1000,
            Slaves: [new SlaveCommand(PointPosition.Diverging, 8, PointPosition.Diverging)]));
        yardData.AddPoint(new Point(8, [809], [812], 1000));

        keyReader?.AddKey('1');
        keyReader?.AddKey('0');
        keyReader?.AddKey('+');

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.HasCount(1, yardController!.Commands);
        Assert.AreEqual(10, yardController.Commands[0].Number);
        await Sut.StopAsync(default);
    }

    /// <summary>
    /// A through station: the track runs in from the west edge past inbound signal 21, on to
    /// outbound signal 81, and out to the east edge. One detection stretch per run, so every step
    /// of a train's life through the station lands on a stretch of its own.
    /// </summary>
    private const string ThroughStation = """
        Test
        [Tracks]
        1.0-1.5-1.10-1.15
        [Signals]
        1.5:21>:i
        1.10:81>:u
        [OccupancySections]
        1:1.0,1.5
        2:1.5,1.10
        3:1.10,1.15
        """;

    private const int ApproachSection = 1;    // west edge up to inbound signal 21
    private const int PlatformSection = 2;    // signal 21 up to outbound signal 81
    private const int DepartureSection = 3;   // signal 81 out to the east edge

    private void ArrangeThroughStation()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var data = new UnifiedStationParser().Parse(ThroughStation);
        yardData.SetTopology(data.Topology);
        foreach (var section in data.OccupancySections) yardData.AddOccupancySection(section);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.InboundMain });
        yardData.AddSignal(new Signal("81", 501) { Type = SignalType.OutboundMain });
        // A route with no points at all counts as undefined, so the route carries one.
        yardData.AddPoint(1, [801], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 81, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
    }

    /// <summary>
    /// The same run with a signal for the opposite direction standing halfway along it: 22 faces
    /// west at 1.7, so a train running east passes it from behind. One stretch either side of it,
    /// so which of the two a movement is put on says which side the app thinks it came from.
    /// </summary>
    private const string StationWithOpposingSignal = """
        Test
        [Tracks]
        1.0-1.5-1.7-1.10-1.15
        [Signals]
        1.5:21>:i
        1.7:<22:h
        1.10:81>:u
        [OccupancySections]
        1:1.0,1.5
        2:1.5,1.7
        3:1.7,1.10
        4:1.10,1.15
        """;

    private void ArrangeStationWithOpposingSignal()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var data = new UnifiedStationParser().Parse(StationWithOpposingSignal);
        yardData.SetTopology(data.Topology);
        foreach (var section in data.OccupancySections) yardData.AddOccupancySection(section);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.InboundMain });
        yardData.AddSignal(new Signal("22", 502) { Type = SignalType.MainDwarf });
        yardData.AddSignal(new Signal("81", 501) { Type = SignalType.OutboundMain });
        // A route with no points at all counts as undefined, so the route carries one.
        yardData.AddPoint(1, [801], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 81, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
    }

    private static void AddKeys(TestKeyReader? keyReader, string keys)
    {
        foreach (var key in keys) keyReader?.AddKey(key);
    }

    [TestMethod]
    public async Task VerifyTrainNumberLifecycle_WalksStretchByStretchOutOfTheStation()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        // Accepted from the previous station: the train stands on the run in from the edge.
        AddKeys(keyReader, "2181=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", ApproachSection),
            "An accepted train stands on the stretch from the edge up to the first signal");
        Assert.IsNull(trainNumbers.GetTrainNumber("", PlatformSection));

        // Arrived: up to the stretch in front of the route's final signal.
        AddKeys(keyReader, "=7777#");
        await Task.Delay(300, default);
        Assert.IsNull(trainNumbers.GetTrainNumber("", ApproachSection));
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", PlatformSection),
            "An arrived train stands on the stretch leading up to the final signal");

        // Departed: out past the outbound signal onto the run to the edge.
        AddKeys(keyReader, "=7777#");
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", DepartureSection),
            "A departed train stands between the last signal and the edge");

        // Arrived at the next station: off the diagram.
        AddKeys(keyReader, "=7777#");
        await Task.Delay(300, default);
        Assert.IsNull(trainNumbers.GetTrainNumber("", DepartureSection),
            "A train reported arrived at the next station occupies no stretch");
        Assert.HasCount(0, trainNumbers.GetTrainNumbers(""));

        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyClearRoute_MovesTrainNumberUpToTheDestinationStretch()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        AddKeys(keyReader, "2181=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        AddKeys(keyReader, "81/");
        await Task.Delay(300, default);

        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", PlatformSection),
            "Clearing the route confirms arrival, moving the number up to the destination stretch");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyArrival_ClearsTheRouteTheTrainRan()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        // A delay long enough that a route waiting it out would still be holding its locks below.
        ServiceProvider.GetRequiredService<TestYardDataService>().LockReleaseDelaySeconds = 30;

        AddKeys(keyReader, "2181=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);
        Assert.IsNotEmpty(pointLockings.CurrentRoutes, "The route must actually be set, or the arrival below proves nothing");

        // Arrived: the movement the route was set for is over the moment the train is reported
        // at the final signal, so the route goes with it.
        AddKeys(keyReader, "=7777#");
        await Task.Delay(300, default);

        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", PlatformSection),
            "An arrived train stands on the stretch leading up to the final signal");
        Assert.IsEmpty(pointLockings.CurrentRoutes, "Arriving at the final signal ends the route");
        Assert.IsEmpty(pointLockings.PointLocks,
            "The train is standing at the destination, clear of the points, so the locks go at once whatever LockReleaseDelay says");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMoveTrainNumberToFinalSignal_ClearsTheRoute()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        ServiceProvider.GetRequiredService<TestYardDataService>().LockReleaseDelaySeconds = 30;

        AddKeys(keyReader, "2181=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        // [signal]= up to the route's own final signal says the same thing as =[trainNumber]: arrived.
        AddKeys(keyReader, "81=#");
        await Task.Delay(300, default);

        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", PlatformSection));
        Assert.IsEmpty(pointLockings.CurrentRoutes, "Moving the train up to the final signal ends the route");
        Assert.IsEmpty(pointLockings.PointLocks, "The train is standing clear of the points, so they are unlocked without the delay");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyDeparture_ClearsTheOutboundRouteAndTakesTheTrainOutToTheEdge()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        ServiceProvider.GetRequiredService<TestYardDataService>().LockReleaseDelaySeconds = 30;

        // A train standing where the outbound route ends: it is at the last signal with the route
        // still set, which is what the departure report below has to tidy up after.
        AddKeys(keyReader, "81=7777#2181#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", PlatformSection));
        Assert.IsNotEmpty(pointLockings.CurrentRoutes, "The route must actually be set, or the departure below proves nothing");

        AddKeys(keyReader, "=7777#");
        await Task.Delay(300, default);

        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", DepartureSection),
            "A departed train stands on the stretch from the last signal out to the edge");
        Assert.IsEmpty(pointLockings.CurrentRoutes, "The train has left, so the route it left on ends with it");
        Assert.IsEmpty(pointLockings.PointLocks, "The train is out past the last signal, so the points are unlocked without the delay");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMoveTrainNumberToSignal_MovesForwardButRefusesToGoBack()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        AddKeys(keyReader, "2181=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        AddKeys(keyReader, "81=#");
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", PlatformSection),
            "[signal]= moves the train up to the stretch in front of that signal");

        // 21 lies behind the train now, so the command must leave it where it stands.
        AddKeys(keyReader, "21=#");
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", PlatformSection),
            "A signal behind the train is refused — a train number only moves the way the train goes");
        Assert.IsNull(trainNumbers.GetTrainNumber("", ApproachSection));

        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMoveTrainNumberToSignal_TakesTheStretchOnTheSideTheTrainComesFrom()
    {
        const int Approach = 1;    // west edge up to inbound signal 21
        const int NearHalf = 2;    // signal 21 up to signal 22, which faces the other way
        const int FarHalf = 3;     // signal 22 on to outbound signal 81

        ArrangeStationWithOpposingSignal();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        AddKeys(keyReader, "2181=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", Approach));

        // Signal 22 faces west while the train runs east, so the train comes up to it over the
        // stretch behind it. Taking the one in front would put it a whole stretch too far along.
        AddKeys(keyReader, "22=#");
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", NearHalf),
            "The train waits at a signal on the side it approaches from, whichever way that signal faces");
        Assert.IsNull(trainNumbers.GetTrainNumber("", FarHalf));

        // On again to the route's own final signal: the stretch ahead of where it stands.
        AddKeys(keyReader, "81=#");
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", FarHalf),
            "The next stretch along the route is ahead of the train, so it may move up to it");

        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyAssignTrainNumberToSignal_PlacesAnInactiveTrainOnTheStretch()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        // No route is set, so this is a train standing in the yard rather than one running.
        AddKeys(keyReader, "81=1234#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.AreEqual("1234", trainNumbers.GetTrainNumber("", PlatformSection),
            "[signal]=[trainNumber] puts the number on the stretch leading up to that signal");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyDepartToEdge_RunsTheTrainOutPastTheOutboundSignal()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        // A delay long enough that a route waiting it out would still be holding its locks below.
        ServiceProvider.GetRequiredService<TestYardDataService>().LockReleaseDelaySeconds = 30;

        // The train stands in front of outbound signal 81 with the route it leaves on set.
        AddKeys(keyReader, "2181#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);
        AddKeys(keyReader, "81=7777#");
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", PlatformSection));
        Assert.IsNotEmpty(pointLockings.CurrentRoutes, "The route must be set, or ending it below proves nothing");

        AddKeys(keyReader, "81.=#");
        await Task.Delay(300, default);

        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", DepartureSection),
            "[signal].= runs the train out past the signal onto the track leading off the layout");
        Assert.IsNull(trainNumbers.GetTrainNumber("", PlatformSection));
        Assert.IsEmpty(pointLockings.CurrentRoutes, "A departure ends the route the train left on");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyAcceptFromEdge_PutsTheTrainOnTheRunInFromThePreviousStation()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        // No route yet: the previous station has offered the train, and nothing more.
        AddKeys(keyReader, ".21=1234#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.AreEqual("1234", trainNumbers.GetTrainNumber("", ApproachSection),
            ".[signal]=[trainNumber] puts the number on the track running in from the edge");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyAssignBeyondSignal_PlacesADepartedTrainOnTheRunToTheEdge()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        AddKeys(keyReader, "81.=1234#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.AreEqual("1234", trainNumbers.GetTrainNumber("", DepartureSection),
            "[signal].=[trainNumber] puts the number straight onto the outbound edge stretch");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyEdgeCommand_RefusesTheSideOfTheSignalTheTrackDoesNotLeaveOn()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        // 21 is the inbound signal, so nothing leaves the station through it; 81 is the outbound
        // one, so nothing comes in through it. A dot on the wrong side names no stretch at all.
        AddKeys(keyReader, "21.=1234#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);
        AddKeys(keyReader, ".81=1234#");
        await Task.Delay(300, default);
        // And a train is only ever moved out to the edge, never back in from it.
        AddKeys(keyReader, ".21=#");
        await Task.Delay(300, default);

        Assert.HasCount(0, trainNumbers.GetTrainNumbers(""));
        await Sut.StopAsync(default);
    }

    /// <summary>
    /// Munkeröd itself, loaded from the file the application ships with. Route 22-30 is composite
    /// and runs the length of the station, which is what makes it worth walking a train over:
    /// every stretch it crosses and every sub-route it is built from has to be there.
    /// </summary>
    private void ArrangeMunkerod()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.Web", "Data", "Munkeröd.txt"));
        var data = new UnifiedStationParser().Parse(File.ReadAllText(path));
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();

        yardData.SetTopology(data.Topology);
        foreach (var section in data.OccupancySections) yardData.AddOccupancySection(section);
        foreach (var definition in data.Topology.Signals) yardData.AddSignal(Signal.FromDefinition(definition));
        foreach (var point in data.Points) yardData.AddPoint(point);
        foreach (var route in data.TrainRoutes) yardData.AddTrainRoute(route);
    }

    /// <summary>
    /// The whole of the reported failure in one go: a train accepted on route 22-30 and then
    /// advanced. It used to leave the diagram at that second command, because signal 30 had been
    /// added to the station after its detection stretches were deduced and so no stretch answered
    /// for the end of the route.
    /// </summary>
    [TestMethod]
    public async Task VerifyAdvanceTrainNumber_WalksTheLengthOfMunkerod()
    {
        ArrangeMunkerod();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();
        var topology = ServiceProvider.GetRequiredService<TestYardDataService>().Topology;
        var sections = ServiceProvider.GetRequiredService<TestYardDataService>().OccupancySections;
        var tracks = topology.MapOccupancySectionTracks(sections);

        var approach = topology.SectionLeadingUpTo(22, sections, tracks);
        var destination = topology.SectionLeadingUpTo(30, sections, tracks);
        Assert.IsNotNull(approach, "Signal 22 must have a stretch leading up to it");
        Assert.IsNotNull(destination, "Signal 30 must have a stretch leading up to it");

        AddKeys(keyReader, "22.30=123#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);
        Assert.AreEqual("123", trainNumbers.GetTrainNumber("", approach.Value),
            "An accepted train stands on the stretch in front of the route's from signal");

        AddKeys(keyReader, "=123#");
        await Task.Delay(300, default);

        Assert.AreEqual("123", trainNumbers.GetTrainNumber("", destination.Value),
            "An arrived train stands on the stretch in front of the route's final signal");
        Assert.HasCount(1, trainNumbers.GetTrainNumbers(""), "The train is on exactly one stretch");
        await Sut.StopAsync(default);
    }

    /// <summary>
    /// The same run, but the stretches were written down before signal 81 was put in: nothing
    /// ends at 1.10, so no stretch answers for the far end of route 21-81. The train stands on
    /// the run in from the edge, which is where a train waiting at signal 21 belongs and also
    /// what a train that had departed westwards would occupy — so nothing about the track itself
    /// says which of the two it is. Only the route it was given does.
    /// </summary>
    private const string StationWithStaleOccupancySections = """
        Test
        [Tracks]
        1.0-1.5-1.10-1.15
        [Signals]
        1.5:21>:i
        1.10:81>:u
        [OccupancySections]
        1:1.0,1.5
        """;

    private void ArrangeStationWithStaleOccupancySections()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var data = new UnifiedStationParser().Parse(StationWithStaleOccupancySections);
        yardData.SetTopology(data.Topology);
        foreach (var section in data.OccupancySections) yardData.AddOccupancySection(section);
        yardData.AddSignal(new Signal("21", 500) { Type = SignalType.InboundMain });
        yardData.AddSignal(new Signal("81", 501) { Type = SignalType.OutboundMain });
        // A route with no points at all counts as undefined, so the route carries one.
        yardData.AddPoint(1, [801], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 81, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
    }

    /// <summary>
    /// A train the diagram cannot advance must stay where it is. It is running the route it was
    /// given, so it has not left the station, and taking the number off would say that it had —
    /// the one reading of the yard an operator could not tell from the truth.
    /// </summary>
    [TestMethod]
    public async Task VerifyAdvanceTrainNumber_KeepsATrainWhoseRouteHasNoDestinationStretch()
    {
        ArrangeStationWithStaleOccupancySections();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        AddKeys(keyReader, "2181=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", ApproachSection),
            "The route must actually be set, or the advance below proves nothing");

        AddKeys(keyReader, "=7777#");
        await Task.Delay(300, default);

        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", ApproachSection),
            "A train that cannot be advanced stays where it stands rather than leaving the diagram");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyAdvanceTrainNumber_IgnoresATrainNumberThatIsNotInTheStation()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        AddKeys(keyReader, "=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.HasCount(0, trainNumbers.GetTrainNumbers(""));
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyCancelRoute_RemovesTheTrainNumber()
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumbers = ServiceProvider.GetRequiredService<ITrainNumberService>();

        AddKeys(keyReader, "2181=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);
        Assert.AreEqual("7777", trainNumbers.GetTrainNumber("", ApproachSection),
            "The route must actually be set, or the cancel below proves nothing");

        AddKeys(keyReader, "81\x1b");
        await Task.Delay(300, default);

        Assert.HasCount(0, trainNumbers.GetTrainNumbers(""),
            "Cancelling undoes the movement, so the train number goes with it");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    [DataRow("81/", DisplayName = "Cleared by command")]
    [DataRow("81\x1b", DisplayName = "Cancelled by command")]
    public async Task VerifyTeardownByCommand_HoldsItsLocksForTheReleaseDelay(string teardownKeys)
    {
        ArrangeThroughStation();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        ServiceProvider.GetRequiredService<TestYardDataService>().LockReleaseDelaySeconds = 30;

        AddKeys(keyReader, "2181=7777#");
        await Sut.StartAsync(default);
        await Task.Delay(300, default);
        Assert.IsNotEmpty(pointLockings.PointLocks, "The route must actually hold its points, or the teardown below proves nothing");

        AddKeys(keyReader, teardownKeys);
        await Task.Delay(300, default);

        Assert.IsNotEmpty(pointLockings.PointLocks,
            "A teardown the operator asks for says nothing about where the train is, so the points stay locked for LockReleaseDelay");
        await Sut.StopAsync(default);
    }

    private static void AssertPointCommands(PointCommand[] expected, IReadOnlyList<PointCommand>? actual)
    {
        Assert.HasCount(expected.Length, actual ?? [], "Number of commands do not match.");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.IsTrue(expected[i].Equals(actual![i]), $"Command {actual![i]} do not match.");
        }
    }



    #region Shunting Area Commands

    [TestMethod]
    public async Task VerifyShuntingAreaReleaseSetsAndLocksFlankProtection()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        var shuntingAreas = ServiceProvider.GetRequiredService<IShuntingAreaService>();

        yardData.AddPoint(4, [804], 0);
        yardData.AddPoint(19, [819], 0);
        yardData.AddPoint(27, [827], 0);
        yardData.AddShuntingArea(new ShuntingArea(5, "Runaround", [
            new PointCommand(4, PointPosition.Undefined, null, false),
            new PointCommand(27, PointPosition.Straight, null, false),
            new PointCommand(19, PointPosition.Undefined, null, false)]));

        foreach (var key in "A5+") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(ShuntingAreaState.Released, shuntingAreas.GetState("", 5));
        var commanded = yardController!.Commands.Single();
        Assert.AreEqual(27, commanded.Number, "Only the flank protection point is commanded.");
        Assert.AreEqual(PointPosition.Straight, commanded.Position);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyReleasedShuntingAreaFlankProtectionBlocksOppositePointCommand()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        yardData.AddPoint(27, [827], 0);
        yardData.AddShuntingArea(new ShuntingArea(5, "Runaround", [
            new PointCommand(27, PointPosition.Straight, null, false)]));

        // Release the area, then try to throw its flank protection point the other way.
        foreach (var key in "A5+27-") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.IsFalse(yardController!.Commands.Any(c => c.Number == 27 && c.Position == PointPosition.Diverging),
            "Flank protection held for a released shunting area must not be moved.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShuntingAreaLockReleasesFlankProtection()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        var shuntingAreas = ServiceProvider.GetRequiredService<IShuntingAreaService>();

        yardData.AddPoint(27, [827], 0);
        yardData.AddShuntingArea(new ShuntingArea(5, "Runaround", [
            new PointCommand(27, PointPosition.Straight, null, false)]));

        // Release, take back, then move the point that is now free again.
        foreach (var key in "A5+A5-27-") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.AreEqual(ShuntingAreaState.Locked, shuntingAreas.GetState("", 5));
        Assert.IsTrue(yardController!.Commands.Any(c => c.Number == 27 && c.Position == PointPosition.Diverging),
            "Once the area is locked again the point is the interlocking's to move.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyUnknownShuntingAreaIsRejected()
    {
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<IPointNotificationService>();
        var shuntingAreas = ServiceProvider.GetRequiredService<IShuntingAreaService>();
        var rejected = new List<PointResult>();
        notifications.PointChanged += result =>
        {
            if (result.ResultType == PointResultType.Rejected) rejected.Add(result);
        };

        foreach (var key in "A9+") keyReader?.AddKey(key);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(ShuntingAreaState.Locked, shuntingAreas.GetState("", 9));
        Assert.AreEqual(1, rejected.Count, "An unknown area number should be rejected.");
        await Sut.StopAsync(default);
    }

    #endregion

    #region Signal Commands

    [TestMethod]
    public async Task VerifySignalCommandGivesSignalGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        yardData.AddSignal(new Signal("22", 522, 622));

        keyReader?.AddKeys("S22+");

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        var commanded = yardController!.SignalCommands.Single();
        Assert.AreEqual(22, commanded.SignalNumber);
        Assert.AreEqual(522, commanded.Address);
        Assert.AreEqual(SignalState.Go, commanded.State);
        Assert.AreEqual(622, commanded.FeedbackAddress);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifySignalCommandPutsSignalToStop()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        yardData.AddSignal(new Signal("22", 522));

        keyReader?.AddKeys("S22-");

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        var commanded = yardController!.SignalCommands.Single();
        Assert.AreEqual(22, commanded.SignalNumber);
        Assert.AreEqual(SignalState.Stop, commanded.State);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyUnknownSignalIsRejected()
    {
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        var notifications = ServiceProvider.GetRequiredService<IPointNotificationService>();
        var rejected = new List<PointResult>();
        notifications.PointChanged += result =>
        {
            if (result.ResultType == PointResultType.Rejected) rejected.Add(result);
        };

        keyReader?.AddKeys("S99+");

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(0, yardController!.SignalCommands.Count);
        Assert.AreEqual(1, rejected.Count, "An unknown signal number should be rejected.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyBufferStopCannotBeGivenGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        yardData.AddSignal(new Signal("47", 547) { Type = SignalType.BufferStop });

        keyReader?.AddKeys("S47+");

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(0, yardController!.SignalCommands.Count, "A buffer stop shows no proceed aspect.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifySignalCommandIsRefusedForShunter()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        yardData.AddSignal(new Signal("22", 522));

        keyReader?.AddKeys("S22+", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(0, yardController!.SignalCommands.Count, "Signals are the dispatcher's alone.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifySignalCommandIsNotTakenForAPointCommand()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // A point and a signal may share a number; the prefix is what tells the two commands apart.
        yardData.AddPoint(22, [822], 0);
        yardData.AddSignal(new Signal("22", 522));

        keyReader?.AddKeys("S22+");

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(0, yardController!.Commands.Count, "No point should be moved by a signal command.");
        Assert.AreEqual(1, yardController.SignalCommands.Count);
        await Sut.StopAsync(default);
    }

    #endregion

    #region Dispatcher and Shunter Roles

    /// <summary>Area 5 hands points 4 and 19 to the shunter and holds point 27 straight for flank protection.</summary>
    private static void AddRunaroundArea(TestYardDataService yardData)
    {
        yardData.AddPoint(4, [804], 0);
        yardData.AddPoint(19, [819], 0);
        yardData.AddPoint(27, [827], 0);
        yardData.AddPoint(9, [809], 0);
        yardData.AddShuntingArea(new ShuntingArea(5, "Runaround", [
            new PointCommand(4, PointPosition.Undefined, null, false),
            new PointCommand(27, PointPosition.Straight, null, false),
            new PointCommand(19, PointPosition.Undefined, null, false)]));
    }

    [TestMethod]
    public async Task VerifyDispatcherCannotMoveAPointReleasedToAShunter()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        AddRunaroundArea(yardData);

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("4-");

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.IsFalse(yardController!.Commands.Any(c => c.Number == 4),
            "A point handed to a shunter is no longer the dispatcher's to move.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterCanMoveAPointInAReleasedArea()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        AddRunaroundArea(yardData);

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("4-", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.IsTrue(yardController!.Commands.Any(c => c.Number == 4 && c.Position == PointPosition.Diverging));
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterCannotMoveAPointOutsideTheReleasedArea()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        AddRunaroundArea(yardData);

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("9-", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.IsFalse(yardController!.Commands.Any(c => c.Number == 9),
            "The shunter owns the area's points and nothing else.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterCannotReleaseAnArea()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var shuntingAreas = ServiceProvider.GetRequiredService<IShuntingAreaService>();
        AddRunaroundArea(yardData);

        keyReader?.AddKeys("A5+", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(200, default);

        Assert.AreEqual(ShuntingAreaState.Locked, shuntingAreas.GetState("", 5),
            "Only the dispatcher hands an area over.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterCanHandBackAnArea()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var shuntingAreas = ServiceProvider.GetRequiredService<IShuntingAreaService>();
        AddRunaroundArea(yardData);

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("A5-", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.AreEqual(ShuntingAreaState.Locked, shuntingAreas.GetState("", 5));
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyDispatcherRouteThroughAReleasedAreaIsRejected()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        AddRunaroundArea(yardData);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(9, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#");

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.IsEmpty(pointLockings.PointLocks, "A route may not claim a point the shunter has.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterRouteWithinTheAreaIsSetAsShunting()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        AddRunaroundArea(yardData);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(19, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        // The route is requested with the main-route key, but a shunter can only authorise shunting.
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        var route = pointLockings.CurrentRoutes.SingleOrDefault();
        Assert.IsNotNull(route, "A route wholly inside the released area is the shunter's to set.");
        Assert.AreEqual(TrainRouteState.SetShunting, route.State);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterRouteReachingOutsideTheAreaIsRejected()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        AddRunaroundArea(yardData);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(9, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.IsEmpty(pointLockings.CurrentRoutes, "Point 9 is outside the area, so the route is not the shunter's.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterCannotClearAllRoutes()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        yardData.LockReleaseDelaySeconds = 30;
        yardData.AddPoint(1, [801], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        keyReader?.AddKeys("2131#");
        keyReader?.AddKeys("//", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.IsNotEmpty(pointLockings.CurrentRoutes, "Clearing the whole yard is the dispatcher's command.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterRouteOverTheAreasFlankProtectionIsSetAsShunting()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        AddRunaroundArea(yardData);
        // Point 27 is the area's flank protection and lies on the way through; the route wants it straight.
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(27, PointPosition.Straight),
             new PointCommand(19, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        var route = pointLockings.CurrentRoutes.SingleOrDefault();
        Assert.IsNotNull(route, "The route passes over point 27 in the position the area already holds it in.");
        Assert.AreEqual(TrainRouteState.SetShunting, route.State);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyDispatcherRouteOverTheAreasFlankProtectionInItsHeldPositionIsSetAndLocksThePoint()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        AddRunaroundArea(yardData);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(27, PointPosition.Straight), new PointCommand(9, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#");

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        var route = pointLockings.CurrentRoutes.SingleOrDefault();
        Assert.IsNotNull(route, "The route leaves point 27 in the position the area holds it in.");
        Assert.IsTrue(route.PointCommands.Any(pc => pc.Number == 27), "Point 27 is part of the route's own locks.");
        Assert.IsTrue(pointLockings.IsLocked(new PointCommand(27, PointPosition.Diverging)));
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterRouteAgainstTheAreasFlankProtectionIsRejected()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        AddRunaroundArea(yardData);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(27, PointPosition.Diverging),
             new PointCommand(19, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.IsEmpty(pointLockings.CurrentRoutes, "Throwing point 27 would break the flank the area is protected by.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyRejectedShunterRouteNamesThePointThatIsNotReleased()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var rejections = new List<string>();
        notifications.RouteChanged += result =>
        {
            if (result.ResultType == TrainRouteResultType.Rejected && result.Message is not null) rejections.Add(result.Message);
        };
        AddRunaroundArea(yardData);
        // Point 9 belongs to no area, so it is the one that puts the route out of reach.
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(9, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.AreEqual(1, rejections.Count, "The route should be rejected once.");
        StringAssert.Contains(rejections[0], "9",
            $"The rejection should name point 9, the one no released area accounts for, but was '{rejections[0]}'.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterRouteAcrossTwoReleasedAreasIsSetAsShunting()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        AddRunaroundArea(yardData);
        yardData.AddPoint(12, [812], 0);
        yardData.AddShuntingArea(new ShuntingArea(6, "Left", [
            new PointCommand(9, PointPosition.Undefined, null, false),
            new PointCommand(12, PointPosition.Undefined, null, false)]));
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(19, PointPosition.Straight),
             new PointCommand(9, PointPosition.Straight), new PointCommand(12, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("A6+");
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(400, default);

        var route = pointLockings.CurrentRoutes.SingleOrDefault();
        Assert.IsNotNull(route, "Every point of the route is owned by one of the two released areas.");
        Assert.AreEqual(TrainRouteState.SetShunting, route.State);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShunterRouteAcrossTwoAreasIsRejectedWhenOnlyOneIsReleased()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        AddRunaroundArea(yardData);
        yardData.AddPoint(12, [812], 0);
        yardData.AddShuntingArea(new ShuntingArea(6, "Left", [
            new PointCommand(9, PointPosition.Undefined, null, false),
            new PointCommand(12, PointPosition.Undefined, null, false)]));
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(19, PointPosition.Straight),
             new PointCommand(9, PointPosition.Straight), new PointCommand(12, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.IsEmpty(pointLockings.CurrentRoutes, "Points 9 and 12 are still the interlocking's.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyTakingBackAnAreaClearsTheRoutesInsideIt()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockingsManager>().GetForStation("");
        AddRunaroundArea(yardData);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(19, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);
        keyReader?.AddKeys("A5-");

        await Sut.StartAsync(default);
        await Task.Delay(400, default);

        Assert.IsEmpty(pointLockings.CurrentRoutes, "The area returns to the interlocking with nothing left standing.");
        Assert.IsEmpty(pointLockings.PointLocks);
        await Sut.StopAsync(default);
    }

    #endregion

    #region Route message wording

    [TestMethod]
    public async Task VerifyShuntingRouteIsNotReportedAsATrainRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var setMessages = new List<(TrainRouteState State, string Message)>();
        notifications.RouteChanged += result =>
        {
            if (result.ResultType == TrainRouteResultType.Set && result.Message is not null)
                setMessages.Add((result.Route.State, result.Message));
        };
        yardData.AddPoint(1, [801], 0);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.Unset,
            [new PointCommand(1, PointPosition.Straight)]));

        keyReader?.AddKeys("2131*");
        keyReader?.AddKeys("31/");
        keyReader?.AddKeys("2131#");

        await Sut.StartAsync(default);
        await Task.Delay(400, default);

        Assert.AreEqual(2, setMessages.Count, "The route is set once as shunting and once as main.");
        Assert.AreEqual(TrainRouteState.SetShunting, setMessages[0].State);
        Assert.AreEqual(TrainRouteState.SetMain, setMessages[1].State);
        Assert.AreNotEqual(setMessages[1].Message, setMessages[0].Message,
            $"A shunting route must not be reported in the words of a train route, but both said '{setMessages[0].Message}'.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyClearedShuntingRouteIsNotReportedAsATrainRoute()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var clearedMessages = new List<string>();
        notifications.RouteChanged += result =>
        {
            if (result.ResultType == TrainRouteResultType.Cleared && result.Message is not null)
                clearedMessages.Add(result.Message);
        };
        yardData.AddPoint(1, [801], 0);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.Unset,
            [new PointCommand(1, PointPosition.Straight)]));

        keyReader?.AddKeys("2131*");
        keyReader?.AddKeys("31/");
        keyReader?.AddKeys("2131#");
        keyReader?.AddKeys("31/");

        await Sut.StartAsync(default);
        await Task.Delay(500, default);

        Assert.AreEqual(2, clearedMessages.Count, "Both routes are cleared by their destination signal.");
        Assert.AreNotEqual(clearedMessages[1], clearedMessages[0],
            $"A teardown reports the kind of the route that stood, but both said '{clearedMessages[0]}'.");
        await Sut.StopAsync(default);
    }

    #endregion

    #region Who a Message Is For

    [TestMethod]
    public async Task VerifyAPointMessageIsAddressedToTheShunterWhoMovedIt()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<IPointNotificationService>();
        var messages = new List<PointResult>();
        notifications.PointChanged += messages.Add;
        AddRunaroundArea(yardData);

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("4-", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        var moved = messages.Single(m => m.PointNumber == 4 && m.ResultType == PointResultType.Set);
        Assert.AreEqual(OperatorRole.Shunter, moved.Audience, "The answer belongs to whoever gave the command.");
        Assert.IsTrue(moved.IsFor(OperatorRole.Shunter));
        Assert.IsFalse(moved.IsFor(OperatorRole.Dispatcher), "The dispatcher did not ask and has nothing to read.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyARefusalIsAddressedToWhoeverWasRefused()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<IPointNotificationService>();
        var rejected = new List<PointResult>();
        notifications.PointChanged += result =>
        {
            if (result.ResultType == PointResultType.Rejected) rejected.Add(result);
        };
        AddRunaroundArea(yardData);

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("9-", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        var refusal = rejected.Single(r => r.PointNumber == 9);
        Assert.AreEqual(OperatorRole.Shunter, refusal.Audience,
            "A refusal must reach the screen of the person who was refused, and no other.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyAShuntingRouteMessageIsAddressedToTheShunter()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var messages = new List<TrainRouteResult>();
        notifications.RouteChanged += messages.Add;
        AddRunaroundArea(yardData);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(19, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        var set = messages.Single(m => m.ResultType == TrainRouteResultType.Set);
        Assert.AreEqual(OperatorRole.Shunter, set.Audience);
        Assert.IsFalse(set.IsFor(OperatorRole.Dispatcher),
            "A route the shunter set says nothing the dispatcher can act on.");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyATrainRouteMessageIsAddressedToTheDispatcher()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var messages = new List<TrainRouteResult>();
        notifications.RouteChanged += messages.Add;
        yardData.AddPoint(1, [801], 0);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.Unset,
            [new PointCommand(1, PointPosition.Straight)]));

        keyReader?.AddKeys("2131#");

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        var set = messages.Single(m => m.ResultType == TrainRouteResultType.Set);
        Assert.AreEqual(OperatorRole.Dispatcher, set.Audience);
        Assert.IsFalse(set.IsFor(OperatorRole.Shunter));
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyClearingTheWholeYardIsAddressedToBothOperators()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var notifications = ServiceProvider.GetRequiredService<ITrainRouteNotificationService>();
        var messages = new List<TrainRouteResult>();
        notifications.RouteChanged += messages.Add;
        AddRunaroundArea(yardData);
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(4, PointPosition.Straight), new PointCommand(19, PointPosition.Straight)]));

        keyReader?.AddKeys("A5+");
        keyReader?.AddKeys("2131#", "", OperatorRole.Shunter);
        keyReader?.AddKeys("//");

        await Sut.StartAsync(default);
        await Task.Delay(500, default);

        var cleared = messages.Single(m => m.ResultType == TrainRouteResultType.Cleared);
        Assert.IsNull(cleared.Audience,
            "The command is the dispatcher's and the route it takes down is the shunter's, so both are told.");
        Assert.IsTrue(cleared.IsFor(OperatorRole.Shunter));
        Assert.IsTrue(cleared.IsFor(OperatorRole.Dispatcher));

        var allCleared = messages.Single(m => m.ResultType == TrainRouteResultType.AllCleared);
        Assert.IsNull(allCleared.Audience, "Clearing the whole yard is news for whoever is working it.");
        await Sut.StopAsync(default);
    }

    #endregion

}
