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
        await Task.Delay(20, default);
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
        await Task.Delay(20, default);

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
        await Task.Delay(20, default);

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
        await Task.Delay(20, default);

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
        await Task.Delay(20, default);

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
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockings>();

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
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockings>();

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
        await Task.Delay(30, default);

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
        await Task.Delay(30, default);

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
        await Task.Delay(20, default);

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
        await Task.Delay(30, default);

        // Single segment should be set
        AssertPointCommands(
            [PointCommand.Create(1, PointPosition.Straight, [801])],
            yardController?.Commands);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMainRouteToExitSignal_SetsToSignalToGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Set up a topology: track 1.0 - 1.5 - 1.10
        // Signal 21 at 1.0 (>), Signal 31 at 1.5 (>) - exit signal (no signals beyond it)
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:\n1.5:31>:");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500));
        yardData.AddSignal(new Signal("31", 501));
        yardData.AddTrainRoute(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set main route with # (Enter)
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('#');

        await Sut.StartAsync(default);
        await Task.Delay(30, default);

        // FROM signal 21 should be Go, and exit signal 31 (TO) should also be Go
        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "FROM signal 21 should be set to Go");
        Assert.IsTrue(yardController.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "Exit TO signal 31 should be set to Go for main route");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShuntingRouteToExitSignal_DoesNotSetToSignalToGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Same topology - Signal 31 is exit signal
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n1.0-1.5-1.10\n[Features]\n1.0:21>:\n1.5:31>:");
        yardData.SetTopology(topology);

        yardData.AddPoint(1, [801], 1000);
        yardData.AddSignal(new Signal("21", 500));
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
        await Task.Delay(30, default);

        // FROM signal 21 should be Go, but exit signal 31 should NOT be Go
        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "FROM signal 21 should be set to Go");
        Assert.IsFalse(yardController.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "Exit TO signal 31 should NOT be set to Go for shunting route");
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyMainRouteToNonExitSignal_DoesNotSetToSignalToGo()
    {
        var yardData = ServiceProvider.GetRequiredService<TestYardDataService>();
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        // Topology: Signal 21 (>) at 1.0, Signal 31 (>) at 1.5, Signal 41 (>) at 1.10
        // Signal 31 is NOT an exit signal (signal 41 is beyond it)
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
        await Task.Delay(30, default);

        // FROM signal 21 Go, but TO signal 31 should NOT be Go (not an exit signal)
        Assert.IsTrue(yardController!.SignalCommands.Any(c => c.SignalNumber == 21 && c.State == SignalState.Go),
            "FROM signal 21 should be set to Go");
        Assert.IsFalse(yardController.SignalCommands.Any(c => c.SignalNumber == 31 && c.State == SignalState.Go),
            "Non-exit TO signal 31 should NOT be set to Go");
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
