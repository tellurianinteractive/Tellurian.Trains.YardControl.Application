using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellurian.Trains.YardController;
using Tellurian.Trains.YardController.Data;
using Tellurian.Trains.YardController.Tests;

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
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        testPoints?.AddPoint(1, [801]);
        testPoints?.AddPoint(2, [802, 803]);
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
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var testTrainRoutes = ServiceProvider.GetRequiredService<ITrainRouteDataSource>() as InMemoryTrainRouteDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        testPoints?.AddPoint(3, [801]);
        testPoints?.AddPoint(4, [802, 803]);
        testTrainRoutes?.AddTrainRouteCommand(new TrainRouteCommand(12, 22, TrainRouteState.SetMain,
            [new PointCommand(3, PointPosition.Diverging),
             new PointCommand(4, PointPosition.Straight)]));
        keyReader?.AddKey('1');
        keyReader?.AddKey('2');
        keyReader?.AddKey('2');
        keyReader?.AddKey('2');
        keyReader?.AddKey('=');

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
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        testPoints?.AddPoint(1, [801]);
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
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        testPoints?.AddPoint(1, [801]);
        testPoints?.AddPoint(9, [809]);
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
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var testTrainRoutes = ServiceProvider.GetRequiredService<ITrainRouteDataSource>() as InMemoryTrainRouteDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockings>();

        testPoints?.AddPoint(1, [801]);
        testTrainRoutes?.AddTrainRouteCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set a train path first
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('=');
        // Then clear all with //
        keyReader?.AddKey('/');
        keyReader?.AddKey('/');

        await Sut.StartAsync(default);
        await Task.Delay(30, default);

        // Locks should be cleared
        Assert.IsEmpty(pointLockings.PointLocks);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyTrainRouteClearByDestinationSignal()
    {
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var testTrainRoutes = ServiceProvider.GetRequiredService<ITrainRouteDataSource>() as InMemoryTrainRouteDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var pointLockings = ServiceProvider.GetRequiredService<TrainRouteLockings>();

        testPoints?.AddPoint(1, [801]);
        testTrainRoutes?.AddTrainRouteCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set a train path
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('=');
        // Then clear it with just destination signal: 31/
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('/');

        await Sut.StartAsync(default);
        await Task.Delay(30, default);

        // Locks should be cleared for route ending at signal 31
        Assert.IsEmpty(pointLockings.PointLocks);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyLockedPointPreventsConflictingRoute()
    {
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var testTrainRoutes = ServiceProvider.GetRequiredService<ITrainRouteDataSource>() as InMemoryTrainRouteDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        testPoints?.AddPoint(1, [801]);
        // Two routes that conflict on point 1
        testTrainRoutes?.AddTrainRouteCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
        testTrainRoutes?.AddTrainRouteCommand(new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Diverging)]));

        // Set first route (21-31)
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('=');
        // Try to set conflicting route (22-32)
        keyReader?.AddKey('2');
        keyReader?.AddKey('2');
        keyReader?.AddKey('3');
        keyReader?.AddKey('2');
        keyReader?.AddKey('=');

        await Sut.StartAsync(default);
        await Task.Delay(30, default);

        // Only first route's point command should be sent
        Assert.AreEqual(1, yardController?.Commands.Count ?? 0);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyPointCommandsAlwaysSent()
    {
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var testTrainRoutes = ServiceProvider.GetRequiredService<ITrainRouteDataSource>() as InMemoryTrainRouteDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        testPoints?.AddPoint(1, [801]);
        testPoints?.AddPoint(2, [802]);
        // Two routes sharing point 1 with same position
        testTrainRoutes?.AddTrainRouteCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));
        testTrainRoutes?.AddTrainRouteCommand(new TrainRouteCommand(31, 41, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight),
             new PointCommand(2, PointPosition.Diverging)]));

        // Set first route (21-31)
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('=');
        // Set second route (31-41)
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('4');
        keyReader?.AddKey('1');
        keyReader?.AddKey('=');

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
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var testTrainRoutes = ServiceProvider.GetRequiredService<ITrainRouteDataSource>() as InMemoryTrainRouteDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        testPoints?.AddPoint(1, [801]);
        testTrainRoutes?.AddTrainRouteCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
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
        var testPoints = ServiceProvider.GetRequiredService<IPointDataSource>() as InMemoryPointDataSource;
        var testTrainRoutes = ServiceProvider.GetRequiredService<ITrainRouteDataSource>() as InMemoryTrainRouteDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        testPoints?.AddPoint(1, [801]);
        testTrainRoutes?.AddTrainRouteCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]));

        // Set route using signal divider: 21.31=
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('.');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('=');

        await Sut.StartAsync(default);
        await Task.Delay(30, default);

        // Single segment should be set
        AssertPointCommands(
            [PointCommand.Create(1, PointPosition.Straight, [801])],
            yardController?.Commands);
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
