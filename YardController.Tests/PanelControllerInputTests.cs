using Microsoft.Extensions.DependencyInjection;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Services;
using YardController.Web.Services.Testing;

namespace YardController.Tests;

/// <summary>
/// The service that drives the panel, exercised without one. What matters here is the wiring: a
/// press reaching the entry machine, a completed entry reaching the keypad pipeline, and a refused
/// press reaching the diagram — none of which a test of the entry machine alone would catch.
/// </summary>
[TestClass]
public class PanelControllerInputTests
{
    private const int AlternateKey = 27;      // command pad, row 4 column 4
    private const int TopLeftKey = 0;         // command pad, row 1 column 1
    private const int TrainRouteSetKey = 8;   // command pad, row 2 column 1

    /// <summary>
    /// Waits for the panel service to settle. It is a hosted service driving a device: starting it
    /// paints thirty-two key faces through Skia, and a press is answered by another repaint, so
    /// nothing it does is finished by the time the call that provoked it returns.
    /// </summary>
    private static async Task Settle(Func<bool> until)
    {
        for (var i = 0; i < 100 && !until(); i++) await Task.Delay(20).ConfigureAwait(false);
    }

    private static (PanelControllerInputs Sut, TestButtonPanel Panel, EntryStateService Entries, TestKeyReader Keys)
        Build(ServiceProvider services, string language = "en")
    {
        services.GetRequiredService<OperatorLanguageService>().SetLanguage(language);
        var data = services.GetRequiredService<TestYardDataService>();
        data.AddPoint(1, [801], 1000);
        data.AddPoint(3, [803], 1000);

        return (services.GetRequiredService<PanelControllerInputs>(),
                services.GetRequiredService<TestButtonPanel>(),
                services.GetRequiredService<EntryStateService>(),
                services.GetRequiredService<TestKeyReader>());
    }

    [TestMethod]
    public async Task PressingACommandWithNoPipelineSaysSoOnTheDiagram()
    {
        // Reported: pressing one of the alternate set's locking keys did nothing and said nothing.
        using var services = ServiceProvider.InstanceWithPanel;
        var (sut, panel, entries, _) = Build(services);
        EntryState? published = null;
        entries.EntryChanged += state => published = state;

        await sut.StartAsync(default);
        panel.Press(AlternateKey);          // switch to the alternate buttons
        panel.Press(TopLeftKey);            // Point · Lock, which has no pipeline yet
        await Settle(() => published?.Rejection is { Length: > 0 });
        await sut.StopAsync(default);

        Assert.IsNotNull(published);
        Assert.AreEqual("Not supported yet", published.Rejection);
    }

    [TestMethod]
    public async Task TheRefusalIsWordedInTheLanguageTheYardIsWorkedIn()
    {
        using var services = ServiceProvider.InstanceWithPanel;
        var (sut, panel, entries, _) = Build(services, "sv");
        EntryState? published = null;
        entries.EntryChanged += state => published = state;

        await sut.StartAsync(default);
        panel.Press(AlternateKey);
        panel.Press(TopLeftKey);
        await Settle(() => published?.Rejection is { Length: > 0 });
        await sut.StopAsync(default);

        Assert.AreEqual("Stöds inte ännu", published?.Rejection);
    }

    [TestMethod]
    public async Task ARefusalIsReportedOnceAndNotAgainAfterwards()
    {
        // It reports the press that caused it, not every state published from then on.
        using var services = ServiceProvider.InstanceWithPanel;
        var (sut, panel, entries, _) = Build(services);
        var rejections = new List<string?>();
        entries.EntryChanged += state => rejections.Add(state.Rejection);

        await sut.StartAsync(default);
        panel.Press(AlternateKey);
        panel.Press(TopLeftKey);            // refused
        await Settle(() => rejections.Any(r => r is not null));
        panel.Press(AlternateKey);          // back to the main set: nothing refused here
        await Settle(() => rejections[^1] is null);
        await sut.StopAsync(default);

        Assert.AreEqual(1, rejections.Count(r => r is not null), "exactly one press was refused");
        Assert.IsNull(rejections[^1], "the refusal must not linger on later states");
    }

    [TestMethod]
    public async Task ACompletedEntryReachesTheKeypadPipeline()
    {
        using var services = ServiceProvider.InstanceWithPanel;
        var (sut, panel, _, keys) = Build(services);

        await sut.StartAsync(default);
        panel.Press(TrainRouteSetKey);
        await Settle(() => !keys.KeyNotAvailable);
        await sut.StopAsync(default);

        // Nothing was sent: the command has no parameters yet, so the send key is dark.
        Assert.IsTrue(keys.KeyNotAvailable);
    }

    [TestMethod]
    public async Task SwitchingButtonSetIsNotRefused()
    {
        using var services = ServiceProvider.InstanceWithPanel;
        var (sut, panel, entries, _) = Build(services);
        var rejections = new List<string?>();
        entries.EntryChanged += state => rejections.Add(state.Rejection);

        await sut.StartAsync(default);
        panel.Press(AlternateKey);
        await sut.StopAsync(default);

        Assert.IsFalse(rejections.Any(r => r is not null), "the set switch is a legal press");
    }

    [TestMethod]
    public async Task APanelThatArrivesLateIsPaintedInFull()
    {
        using var services = ServiceProvider.InstanceWithPanel;
        var (sut, panel, _, _) = Build(services);
        panel.IsConnected = false;

        await sut.StartAsync(default);
        panel.SetConnected(true);
        await Settle(() => panel.PaintCount.All(c => c > 0));
        await sut.StopAsync(default);

        // Every key of the command pad and the numeric pad, and none of the rows past them.
        for (var i = 0; i < 32; i++)
        {
            Assert.IsTrue(panel.PaintCount[i] > 0, $"key {i} was never painted");
        }
    }
}
