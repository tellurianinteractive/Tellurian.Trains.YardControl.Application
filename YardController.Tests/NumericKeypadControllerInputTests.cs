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
    public async Task VerifyMainRouteToOutboundMainSignal_SetsToSignalToGo()
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

        // FROM signal 21 should be Go, and OutboundMain signal 31 (TO) should also be Go
        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "FROM signal 21 should be set to Go");
        Assert.IsTrue(yardController.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "OutboundMain TO signal 31 should be set to Go for main route");
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

    [TestMethod]
    public async Task VerifyAdvanceTrainNumber_MovesTrainForward()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumberService = ServiceProvider.GetRequiredService<ITrainNumberService>();

        yardData.AddPoint(1, [801], 1000);
        yardData.AddTrainRoute(new TrainRouteCommand(51, 81, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set route 51-81 with train number 7777 — train should land at FromSignal 51
        foreach (var c in "5181=7777#") keyReader?.AddKey(c);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.AreEqual("7777", trainNumberService.GetTrainNumber(51), "Train number should be at FromSignal after route set");
        Assert.IsNull(trainNumberService.GetTrainNumber(81), "Train number should NOT be at ToSignal yet");

        // Type =7777# to advance the train forward
        foreach (var c in "=7777#") keyReader?.AddKey(c);
        await Task.Delay(300, default);

        Assert.IsNull(trainNumberService.GetTrainNumber(51), "Train number should be removed from FromSignal after advance");
        Assert.AreEqual("7777", trainNumberService.GetTrainNumber(81), "Train number should be at ToSignal after advance");

        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyAdvanceTrainNumber_RemovesWhenNoFurtherSignal()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var trainNumberService = ServiceProvider.GetRequiredService<ITrainNumberService>();

        // Manually place train 7777 at signal 81 with no active route from 81
        trainNumberService.AssignTrainNumber(81, "7777");

        // Type =7777# — no route extends from 81, so the train number should be removed
        foreach (var c in "=7777#") keyReader?.AddKey(c);

        await Sut.StartAsync(default);
        await Task.Delay(300, default);

        Assert.IsNull(trainNumberService.GetTrainNumber(81), "Train number should be removed when no further signal");

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


}
