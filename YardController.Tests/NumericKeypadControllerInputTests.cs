using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellurian.Trains.YardController;

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
    public async Task VerifyBasicSwitchCommands()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        testSwitches?.AddSwitch(1, [801]);
        testSwitches?.AddSwitch(2, [802, 803]);
        keyReader?.AddKey('1');
        keyReader?.AddKey('+');
        keyReader?.AddKey('2');
        keyReader?.AddKey('-');

        await Sut.StartAsync(default);
        await Task.Delay(20, default);

        AssertSwitchCommands(
            [SwitchCommand.Create(1, SwitchDirection.Straight, [801]),
            SwitchCommand.Create(2, SwitchDirection.Diverging, [802,803])],
            yardController?.Commands);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyTrainPathCommands()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var testTrainPaths = ServiceProvider.GetRequiredService<ITrainPathDataSource>() as InMemoryTrainPathDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        testSwitches?.AddSwitch(3, [801]);
        testSwitches?.AddSwitch(4, [802, 803]);
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(12, 22, TrainRouteState.SetMain,
            [new SwitchCommand(3, SwitchDirection.Diverging),
             new SwitchCommand(4, SwitchDirection.Straight)]));
        keyReader?.AddKey('1');
        keyReader?.AddKey('2');
        keyReader?.AddKey('2');
        keyReader?.AddKey('2');
        keyReader?.AddKey('=');

        await Sut.StartAsync(default);
        await Task.Delay(20, default);

        AssertSwitchCommands(
           [SwitchCommand.Create(3, SwitchDirection.Diverging, [801]),
            SwitchCommand.Create(4, SwitchDirection.Straight, [802,803])],
           yardController?.Commands);
        await Sut.StopAsync(default);

    }

    [TestMethod]
    public async Task VerifySwitchWithNoAddresses_SendsCommandWithEmptyAddresses()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        testSwitches?.AddSwitch(1, [801]);
        // Request switch 99 which has no addresses defined
        keyReader?.AddKey('9');
        keyReader?.AddKey('9');
        keyReader?.AddKey('+');

        await Sut.StartAsync(default);
        await Task.Delay(20, default);

        // Command is sent but with empty addresses (ToTurnoutCommands will be empty)
        Assert.AreEqual(1, yardController?.Commands.Count ?? -1);
        Assert.AreEqual(99, yardController?.Commands[0].Number);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyClearInputBuffer()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        testSwitches?.AddSwitch(1, [801]);
        testSwitches?.AddSwitch(9, [809]);
        // Start typing 9, then clear, then type 1+
        keyReader?.AddKey('9');
        keyReader?.AddKey('<'); // Clear
        keyReader?.AddKey('1');
        keyReader?.AddKey('+');

        await Sut.StartAsync(default);
        await Task.Delay(20, default);

        // Should only have switch 1 command, not 91+
        AssertSwitchCommands(
            [SwitchCommand.Create(1, SwitchDirection.Straight, [801])],
            yardController?.Commands);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyClearAllTrainRoutes()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var testTrainPaths = ServiceProvider.GetRequiredService<ITrainPathDataSource>() as InMemoryTrainPathDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        var switchLockings = ServiceProvider.GetRequiredService<SwitchLockings>();

        testSwitches?.AddSwitch(1, [801]);
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]));

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
        Assert.AreEqual(0, switchLockings.SwitchLocks.Count());
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyTrainPathClearByDestinationSignal()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var testTrainPaths = ServiceProvider.GetRequiredService<ITrainPathDataSource>() as InMemoryTrainPathDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        var switchLockings = ServiceProvider.GetRequiredService<SwitchLockings>();

        testSwitches?.AddSwitch(1, [801]);
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]));

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
        Assert.AreEqual(0, switchLockings.SwitchLocks.Count());
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyLockedSwitchPreventsConflictingRoute()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var testTrainPaths = ServiceProvider.GetRequiredService<ITrainPathDataSource>() as InMemoryTrainPathDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;
        var switchLockings = ServiceProvider.GetRequiredService<SwitchLockings>();

        testSwitches?.AddSwitch(1, [801]);
        // Two routes that conflict on switch 1
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]));
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Diverging)]));

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

        // Only first route's switch command should be sent
        Assert.AreEqual(1, yardController?.Commands.Count ?? 0);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyUnchangedSwitchNotSentAgain()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var testTrainPaths = ServiceProvider.GetRequiredService<ITrainPathDataSource>() as InMemoryTrainPathDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        testSwitches?.AddSwitch(1, [801]);
        testSwitches?.AddSwitch(2, [802]);
        // Two routes sharing switch 1 with same direction
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]));
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(31, 41, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight),
             new SwitchCommand(2, SwitchDirection.Diverging)]));

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

        // Switch 1 should only be sent once (first route), switch 2 sent in second route
        Assert.AreEqual(2, yardController?.Commands.Count ?? 0);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyShuntingRoute()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var testTrainPaths = ServiceProvider.GetRequiredService<ITrainPathDataSource>() as InMemoryTrainPathDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        testSwitches?.AddSwitch(1, [801]);
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]));

        // Set shunting route with *
        keyReader?.AddKey('2');
        keyReader?.AddKey('1');
        keyReader?.AddKey('3');
        keyReader?.AddKey('1');
        keyReader?.AddKey('*');

        await Sut.StartAsync(default);
        await Task.Delay(20, default);

        AssertSwitchCommands(
            [SwitchCommand.Create(1, SwitchDirection.Straight, [801])],
            yardController?.Commands);
        await Sut.StopAsync(default);
    }

    [TestMethod]
    public async Task VerifyTwoSignalRouteWithDivider()
    {
        var testSwitches = ServiceProvider.GetRequiredService<ISwitchDataSource>() as InMemorySwitchDataSource;
        var testTrainPaths = ServiceProvider.GetRequiredService<ITrainPathDataSource>() as InMemoryTrainPathDataSource;
        var keyReader = ServiceProvider.GetRequiredService<IKeyReader>() as TestKeyReader;
        var yardController = ServiceProvider.GetRequiredService<IYardController>() as TestYardController;

        testSwitches?.AddSwitch(1, [801]);
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]));

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
        AssertSwitchCommands(
            [SwitchCommand.Create(1, SwitchDirection.Straight, [801])],
            yardController?.Commands);
        await Sut.StopAsync(default);
    }

    private static void AssertSwitchCommands(SwitchCommand[] expected, IReadOnlyList<SwitchCommand>? actual)
    {
        Assert.HasCount(expected.Length, actual ?? [], "Number of commands do not match.");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.IsTrue(expected[i].Equals(actual![i]), $"Command {actual![i]} do not match.");
        }
    }


}
