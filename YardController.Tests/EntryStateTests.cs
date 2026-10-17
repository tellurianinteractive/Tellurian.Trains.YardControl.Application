using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Resources;
using YardController.Web.Services;
using YardController.Web.Services.Testing;

namespace YardController.Tests;

/// <summary>
/// Character-by-character entry display (Specifications/InputDevices.md §8). The point of these is
/// §8.3 — that a keypad station gets the same line as a panel one, because the work sits in the
/// input pipeline rather than in the hardware.
/// </summary>
[TestClass]
public class EntryStateTests
{
    private ServiceProvider _services = default!;
    private IHostedService _sut = default!;

    [TestInitialize]
    public void TestInitialize()
    {
        _services = ServiceProvider.InstanceForTesting;
        _sut = _services.GetRequiredService<IHostedService>();
    }

    [TestCleanup]
    public async Task TestCleanup() => await _services.DisposeAsync();

    [TestMethod]
    public void AnUnknownStationIsIdle()
    {
        var entries = _services.GetRequiredService<IEntryStateService>();
        var entry = entries.GetEntry("Nowhere");

        Assert.IsTrue(entry.IsIdle);
        Assert.AreEqual(PanelCommand.None, entry.Command);
        Assert.AreEqual("", entry.RawKeys);
    }

    [TestMethod]
    public async Task TheKeypadPublishesEveryKeystrokeNotOnlyTheCommit()
    {
        // Today the keypad accumulates keys privately and publishes nothing until a command
        // commits, which is why the diagram cannot tell that anything is being typed at all.
        var entries = _services.GetRequiredService<IEntryStateService>();
        var keyReader = _services.GetRequiredService<IKeyReader>() as TestKeyReader;
        var seen = new List<string>();
        entries.EntryChanged += state => seen.Add(state.RawKeys);

        keyReader?.AddKeys("12");
        await _sut.StartAsync(default);
        await Task.Delay(300, default);
        await _sut.StopAsync(default);

        CollectionAssert.Contains(seen, "1");
        CollectionAssert.Contains(seen, "12");
    }

    [TestMethod]
    public async Task TheKeypadNamesNoCommandBecauseItsGrammarIsPostfix()
    {
        // A panel knows the command from the first press; the keypad cannot know it until the
        // terminator arrives, so it publishes the characters and nothing else.
        var entries = _services.GetRequiredService<IEntryStateService>();
        var keyReader = _services.GetRequiredService<IKeyReader>() as TestKeyReader;
        EntryState? last = null;
        entries.EntryChanged += state => last = state;

        keyReader?.AddKeys("21");
        await _sut.StartAsync(default);
        await Task.Delay(300, default);
        await _sut.StopAsync(default);

        Assert.IsNotNull(last);
        Assert.AreEqual(PanelCommand.None, last.Command);
        Assert.AreEqual(0, last.Candidates.Count);
        Assert.IsFalse(last.IsComplete);
    }

    [TestMethod]
    public async Task TheEntryIsClearedOnceTheCommandHasCommitted()
    {
        var yardData = _services.GetRequiredService<TestYardDataService>();
        var entries = _services.GetRequiredService<IEntryStateService>();
        var keyReader = _services.GetRequiredService<IKeyReader>() as TestKeyReader;
        yardData.AddPoint(1, [801], 1000);
        var cleared = new List<bool>();
        entries.EntryChanged += state => cleared.Add(state.IsIdle);

        keyReader?.AddKeys("1+");
        await _sut.StartAsync(default);
        await Task.Delay(500, default);
        await _sut.StopAsync(default);

        CollectionAssert.Contains(cleared, true, "the line must empty once the command has gone");
    }

    [TestMethod]
    public void APanelEntryShowsItsParametersOnceNotTwice()
    {
        // Reported from the panel: Shunting route · Release then a digit showed the digit twice.
        // The pending text already carries the parameters, so appending the raw characters to it
        // prints the same digits again.
        var entry = new EntryState(
            "Munkeröd", OperatorRole.Dispatcher, PanelCommand.ShuntingRouteRelease, ButtonSet.Main,
            [], "2", false, [21, 22], false, "2", "Lös 2");

        Assert.AreEqual("Lös 2", entry.DisplayText);
    }

    [TestMethod]
    public void AKeypadEntryShowsTheCharactersBecauseItHasNoCommandToName()
    {
        // §8.2 asks for the raw characters on a keypad's behalf, and there they are all there is:
        // the grammar is postfix, so nothing can be named until the terminator arrives.
        var entry = new EntryState(
            "Munkeröd", OperatorRole.Dispatcher, PanelCommand.None, ButtonSet.Main,
            [], "", false, [], false, "2131", null);

        Assert.AreEqual("2131", entry.DisplayText);
    }

    [TestMethod]
    public void ControlCharactersAreShownAsSomethingReadable()
    {
        // Two of the keypad's terminators are control characters; left as they are, the line looks
        // as though nothing had been typed.
        var entry = new EntryState(
            "Munkeröd", OperatorRole.Dispatcher, PanelCommand.None, ButtonSet.Main,
            [], "", false, [], false, "21\u001b", null);

        Assert.AreEqual("21ESC", entry.DisplayText);
    }

    [TestMethod]
    public void AnIdleEntryShowsNothing()
    {
        Assert.AreEqual("", EntryState.Idle("Munkeröd").DisplayText);
    }

    [TestMethod]
    [DataRow(PanelEntryRejection.NotImplemented, "en", "Not supported yet")]
    [DataRow(PanelEntryRejection.NotImplemented, "sv", "Stöds inte ännu")]
    [DataRow(PanelEntryRejection.NotImplemented, "de", "Noch nicht unterstützt")]
    [DataRow(PanelEntryRejection.ButtonSetLocked, "en", "Finish or cancel the command first")]
    [DataRow(PanelEntryRejection.NoSuchNumber, "sv", "Numret finns inte på denna station")]
    public void ARefusalIsWordedInTheOperatorsLanguage(PanelEntryRejection rejection, string language, string expected)
    {
        // The language is passed rather than taken from the ambient culture, which on a panel
        // thread answers with the machine's own.
        Assert.AreEqual(expected, Messages.PanelRefusal(rejection, language));
    }

    [TestMethod]
    public void EveryRefusalHasSomethingToSayInEveryLanguage()
    {
        // A refusal with no words is the fault being fixed: a key that does nothing and says
        // nothing. None is the only value that stands for "not refused".
        foreach (var rejection in Enum.GetValues<PanelEntryRejection>())
        {
            if (rejection == PanelEntryRejection.None) continue;
            foreach (var language in new[] { "en", "sv", "da", "nb", "de" })
            {
                var message = Messages.PanelRefusal(rejection, language);
                Assert.IsFalse(string.IsNullOrWhiteSpace(message), $"{rejection} says nothing in {language}");
                Assert.AreNotEqual(rejection.ToString(), message,
                    $"{rejection} in {language} fell back to the resource name");
            }
        }
    }

    [TestMethod]
    public void NotRefusedSaysNothing()
    {
        Assert.AreEqual("", Messages.PanelRefusal(PanelEntryRejection.None, "en"));
    }

    [TestMethod]
    public void ARefusalIsPublishedEvenWhenTheEntryIsOtherwiseIdle()
    {
        // Refusing the very first press of a command leaves nothing else to say, so an idle entry
        // carrying a refusal must still be published rather than treated as a clear.
        var entries = new EntryStateService();
        EntryState? seen = null;
        entries.EntryChanged += state => seen = state;

        entries.NotifyEntryChanged(EntryState.Idle("Munkeröd") with { Rejection = "Not supported yet" });

        Assert.IsNotNull(seen);
        Assert.AreEqual("Not supported yet", seen.Rejection);
        Assert.IsTrue(seen.IsIdle, "the entry itself is still idle — nothing was taken");
    }

    [TestMethod]
    public void ClearingKeepsTheButtonSetTheEntryWasOn()
    {
        // Cancelling an entry does not return the pad to the main buttons (§5.3), so the set is
        // part of the panel's standing state rather than of the entry that was abandoned.
        var entries = new EntryStateService();
        entries.NotifyEntryChanged(new EntryState(
            "Munkeröd", OperatorRole.Dispatcher, PanelCommand.PointLock, ButtonSet.Alternate,
            [], "1", false, [], false, "1", null));

        entries.NotifyEntryCleared("Munkeröd");

        var entry = entries.GetEntry("Munkeröd");
        Assert.IsTrue(entry.IsIdle);
        Assert.AreEqual(ButtonSet.Alternate, entry.ButtonSet);
    }

    [TestMethod]
    public void EntriesAreKeptPerStation()
    {
        // A component created part-way through an entry should show what is being typed, and two
        // stations being worked at once must not overwrite each other's line.
        var entries = new EntryStateService();
        entries.NotifyEntryChanged(new EntryState(
            "Munkeröd", OperatorRole.Dispatcher, PanelCommand.TrainRouteSet, ButtonSet.Main,
            ["21"], "3", false, [31, 33], false, "213", "Lägg 21 → 3"));

        Assert.AreEqual("213", entries.GetEntry("Munkeröd").RawKeys);
        Assert.IsTrue(entries.GetEntry("Steinsnes").IsIdle);
        CollectionAssert.AreEqual(new[] { 31, 33 }, entries.GetEntry("Munkeröd").Candidates.ToArray());
    }
}
