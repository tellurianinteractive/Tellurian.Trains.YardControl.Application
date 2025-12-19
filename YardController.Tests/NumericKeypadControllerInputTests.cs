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
            [SwitchCommand.Create(1, SwitchDirection.Diverging, [801]),
            SwitchCommand.Create(2, SwitchDirection.Straight, [802,803])],
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
        testTrainPaths?.AddTrainPathCommand(new TrainRouteCommand(12, 22, TrainRouteState.Set,
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

    private static void AssertSwitchCommands(SwitchCommand[] expected, IReadOnlyList<SwitchCommand>? actual)
    {
        Assert.HasCount(expected.Length, actual ?? [], "Number of commands do not match.");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.IsTrue(expected[i].Equals(actual![i]), $"Command {actual![i]} do not match.");
        }
    }


}
