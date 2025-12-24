using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class TrainRouteCommandTests
{
    #region TrainRouteState Extension Tests

    [TestMethod]
    public void IsSet_ReturnsTrue_ForSetMainAndSetShunting()
    {
        Assert.IsTrue(TrainRouteState.SetMain.IsSet);
        Assert.IsTrue(TrainRouteState.SetShunting.IsSet);
    }

    [TestMethod]
    public void IsSet_ReturnsFalse_ForOtherStates()
    {
        Assert.IsFalse(TrainRouteState.Undefined.IsSet);
        Assert.IsFalse(TrainRouteState.Unset.IsSet);
        Assert.IsFalse(TrainRouteState.Clear.IsSet);
        Assert.IsFalse(TrainRouteState.Cancel.IsSet);
    }

    [TestMethod]
    public void IsClear_ReturnsTrue_OnlyForClear()
    {
        Assert.IsTrue(TrainRouteState.Clear.IsClear);
        Assert.IsFalse(TrainRouteState.SetMain.IsClear);
        Assert.IsFalse(TrainRouteState.Cancel.IsClear);
    }

    [TestMethod]
    public void IsCancel_ReturnsTrue_OnlyForCancel()
    {
        Assert.IsTrue(TrainRouteState.Cancel.IsCancel);
        Assert.IsFalse(TrainRouteState.Clear.IsCancel);
        Assert.IsFalse(TrainRouteState.SetMain.IsCancel);
    }

    #endregion

    #region TrainRouteCommand IsSet/IsClear Tests

    [TestMethod]
    public void Command_IsSet_ReturnsTrue_ForSetStates()
    {
        var mainRoute = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);
        var shuntRoute = new TrainRouteCommand(21, 31, TrainRouteState.SetShunting,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        Assert.IsTrue(mainRoute.IsSet);
        Assert.IsTrue(shuntRoute.IsSet);
    }

    [TestMethod]
    public void Command_IsClear_ReturnsTrue_ForClearState()
    {
        var clearRoute = new TrainRouteCommand(21, 31, TrainRouteState.Clear,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        Assert.IsTrue(clearRoute.IsClear);
    }

    #endregion

    #region IsUndefined Tests

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenFromSignalZeroAndSetMain()
    {
        var route = new TrainRouteCommand(0, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenFromSignalZeroAndSetShunting()
    {
        var route = new TrainRouteCommand(0, 31, TrainRouteState.SetShunting,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenToSignalIsZero()
    {
        var route = new TrainRouteCommand(21, 0, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenSetCommandHasNoSwitchCommands()
    {
        var route = new TrainRouteCommand(21, 31, TrainRouteState.SetMain, []);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenSetCommandHasAllUndefinedSwitchCommands()
    {
        var route = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(0, SwitchDirection.Undefined)]);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsFalse_ForValidSetCommand()
    {
        var route = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        Assert.IsFalse(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsFalse_ForClearWithEmptySwitchCommands()
    {
        // Clear commands with empty SwitchCommands are valid (clear by ToSignal)
        var route = new TrainRouteCommand(0, 31, TrainRouteState.Clear, []);

        Assert.IsFalse(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsFalse_ForCancelWithEmptySwitchCommands()
    {
        // Cancel commands with empty SwitchCommands are valid
        var route = new TrainRouteCommand(0, 31, TrainRouteState.Cancel, []);

        Assert.IsFalse(route.IsUndefined);
    }

    #endregion

    #region IsInConflictWith Tests

    [TestMethod]
    public void IsInConflictWith_ReturnsTrue_WhenSameSwitchDifferentDirection()
    {
        var route1 = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);
        var route2 = new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Diverging)]);

        Assert.IsTrue(route1.IsInConflictWith(route2));
    }

    [TestMethod]
    public void IsInConflictWith_ReturnsFalse_WhenSameSwitchSameDirection()
    {
        var route1 = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);
        var route2 = new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        Assert.IsFalse(route1.IsInConflictWith(route2));
    }

    [TestMethod]
    public void IsInConflictWith_ReturnsFalse_WhenNoOverlappingSwitches()
    {
        var route1 = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);
        var route2 = new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new SwitchCommand(2, SwitchDirection.Diverging)]);

        Assert.IsFalse(route1.IsInConflictWith(route2));
    }

    [TestMethod]
    public void IsInConflictWith_ReturnsTrue_WhenAnyConflictExists()
    {
        var route1 = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight),
             new SwitchCommand(2, SwitchDirection.Diverging)]);
        var route2 = new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight),  // Same direction - no conflict
             new SwitchCommand(2, SwitchDirection.Straight)]); // Different direction - conflict!

        Assert.IsTrue(route1.IsInConflictWith(route2));
    }

    [TestMethod]
    public void IsInConflictWith_ReturnsFalse_WhenBothHaveEmptyCommands()
    {
        var route1 = new TrainRouteCommand(21, 31, TrainRouteState.SetMain, []);
        var route2 = new TrainRouteCommand(22, 32, TrainRouteState.SetMain, []);

        Assert.IsFalse(route1.IsInConflictWith(route2));
    }

    #endregion

    #region ToString Tests

    [TestMethod]
    public void ToString_ReturnsUndefined_ForUndefinedCommand()
    {
        var route = new TrainRouteCommand(0, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        Assert.AreEqual("Undefined", route.ToString());
    }

    [TestMethod]
    public void ToString_FormatsCorrectly_WhenFromSignalIsZero()
    {
        var route = new TrainRouteCommand(0, 31, TrainRouteState.Clear,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        // FromSignal=0 shows as "-ToSignal:State" format
        Assert.AreEqual("-31:Clear", route.ToString());
    }

    [TestMethod]
    public void ToString_FormatsCorrectly_ForValidCommand()
    {
        var route = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight),
             new SwitchCommand(2, SwitchDirection.Diverging)]);

        var result = route.ToString();

        Assert.Contains("21-31", result);
        Assert.Contains("1", result);
        Assert.Contains("2", result);
    }

    #endregion
}
