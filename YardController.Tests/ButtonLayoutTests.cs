using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Tests;

/// <summary>
/// The command pad's two sets of buttons, and the station file section that declares them
/// (Specifications/InputDevices.md §4, §5).
/// </summary>
[TestClass]
public class ButtonLayoutTests
{
    private const string Header = """
        Test Station

        [Tracks]
        1.0-1.2-1.4

        """;

    private static UnifiedStationData Parse(string sections) =>
        new UnifiedStationParser().Parse(Header + sections);

    [TestMethod]
    public void AStationWithNoButtonsSectionGetsTheBuiltInArrangement()
    {
        // A panel should work at a station whose file has never mentioned one.
        var data = Parse("");
        Assert.AreEqual(PanelCommand.PointStraight, data.Buttons[ButtonSet.Main, 0]);
        Assert.AreEqual(PanelCommand.ButtonsAlternate, data.Buttons[ButtonSet.Main, ButtonLayout.SwitchKeyIndex]);
        Assert.AreEqual(PanelCommand.PointLock, data.Buttons[ButtonSet.Alternate, 0]);
    }

    [TestMethod]
    public void BothSetsAreReadFromTheStationFile()
    {
        var data = Parse("""
            [Buttons]
            Main:
            point-straight, point-diverging, signal-stop, signal-go
            route-train-set, route-train-cancel, route-shunting-set, route-shunting-release
            train-approved, train-advance, -, -
            area-lock, area-release, -, buttons-alternate
            Alternate:
            point-lock, point-unlock, signal-lock-stop, signal-unlock-stop
            -, -, -, -
            -, -, -, -
            -, -, -, buttons-main
            """);

        Assert.AreEqual(PanelCommand.SignalGo, data.Buttons[ButtonSet.Main, 3]);
        Assert.AreEqual(PanelCommand.TrainAdvance, data.Buttons[ButtonSet.Main, 9]);
        Assert.AreEqual(PanelCommand.None, data.Buttons[ButtonSet.Main, 14], "position 4-3 is deliberately spare");
        Assert.AreEqual(PanelCommand.ButtonsAlternate, data.Buttons[ButtonSet.Main, 15]);
        Assert.AreEqual(PanelCommand.SignalUnlockStop, data.Buttons[ButtonSet.Alternate, 3]);
        Assert.AreEqual(PanelCommand.ButtonsMain, data.Buttons[ButtonSet.Alternate, 15]);
    }

    [TestMethod]
    public void CounterpartsKeepThePositionOfTheMainButtonTheyAnswerTo()
    {
        // §5.3: Point Lock where Point Straight was, Signal Lock stop where Signal Set stop was.
        var layout = ButtonLayout.Default;
        Assert.AreEqual(PanelCommand.PointStraight, layout[ButtonSet.Main, 0]);
        Assert.AreEqual(PanelCommand.PointLock, layout[ButtonSet.Alternate, 0]);
        Assert.AreEqual(PanelCommand.SignalStop, layout[ButtonSet.Main, 2]);
        Assert.AreEqual(PanelCommand.SignalLockStop, layout[ButtonSet.Alternate, 2]);
    }

    [TestMethod]
    public void AKeyNamedAsSomethingThatIsNotAnIconIsLeftBlank()
    {
        // A station file is edited while the application runs; a typo should darken one key.
        var data = Parse("""
            [Buttons]
            Main:
            point-straight, nonsense-key, signal-stop, signal-go
            """);

        Assert.AreEqual(PanelCommand.PointStraight, data.Buttons[ButtonSet.Main, 0]);
        Assert.AreEqual(PanelCommand.None, data.Buttons[ButtonSet.Main, 1]);
        Assert.AreEqual(PanelCommand.SignalStop, data.Buttons[ButtonSet.Main, 2]);
    }

    [TestMethod]
    public void ASetMayStopShortAndTheRestOfItIsBlank()
    {
        var data = Parse("""
            [Buttons]
            Main:
            point-straight, point-diverging
            """);

        Assert.AreEqual(PanelCommand.PointDiverging, data.Buttons[ButtonSet.Main, 1]);
        Assert.AreEqual(PanelCommand.None, data.Buttons[ButtonSet.Main, 2]);
        Assert.AreEqual(PanelCommand.None, data.Buttons[ButtonSet.Main, 15]);
    }

    [TestMethod]
    public void ALayoutWithNoAlternateCommandsSaysSo()
    {
        // A station with one set of buttons should not spend a key switching to an empty one.
        var data = Parse("""
            [Buttons]
            Main:
            point-straight, point-diverging, signal-stop, signal-go
            """);

        Assert.IsFalse(data.Buttons.HasAlternateSet);
        Assert.IsTrue(ButtonLayout.Default.HasAlternateSet);
    }

    [TestMethod]
    public void EveryCommandHasAnIconAndEveryIconIsDrawn()
    {
        // The icon name is the key's identity, its picture and the name its label is looked up
        // under; a command with no icon would be a key that cannot be drawn or declared.
        var iconFolder = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "YardController.Panel", "Icons");

        foreach (var command in Enum.GetValues<PanelCommand>())
        {
            if (command == PanelCommand.None) continue;
            var name = command.IconName;
            Assert.IsFalse(string.IsNullOrEmpty(name), $"{command} has no icon name");
            Assert.IsTrue(File.Exists(Path.Combine(iconFolder, name + ".svg")), $"{name}.svg is not drawn");
        }
    }

    [TestMethod]
    public void TheNumericPadReplicatesAKeypad()
    {
        CollectionAssert.AreEqual(
            new[] { '7', '8', '9', '-', '4', '5', '6', '+', '1', '2', '3', '*', '/', '0', '.', '#' },
            NumericPad.Keys.ToArray());
    }
}
