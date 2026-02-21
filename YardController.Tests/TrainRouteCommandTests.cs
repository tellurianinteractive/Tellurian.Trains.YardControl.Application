using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

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

    [TestMethod]
    public void IsTeardown_ReturnsTrue_ForClearAndCancel()
    {
        Assert.IsTrue(TrainRouteState.Clear.IsTeardown);
        Assert.IsTrue(TrainRouteState.Cancel.IsTeardown);
    }

    [TestMethod]
    public void IsTeardown_ReturnsFalse_ForOtherStates()
    {
        Assert.IsFalse(TrainRouteState.Undefined.IsTeardown);
        Assert.IsFalse(TrainRouteState.Unset.IsTeardown);
        Assert.IsFalse(TrainRouteState.SetMain.IsTeardown);
        Assert.IsFalse(TrainRouteState.SetShunting.IsTeardown);
    }

    #endregion

    #region TrainRouteCommand IsSet/IsClear Tests

    [TestMethod]
    public void Command_IsSet_ReturnsTrue_ForSetStates()
    {
        var mainRoute = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]);
        var shuntRoute = new TrainRouteCommand(21, 31, TrainRouteState.SetShunting,
            [new PointCommand(1, PointPosition.Straight)]);

        Assert.IsTrue(mainRoute.IsSet);
        Assert.IsTrue(shuntRoute.IsSet);
    }

    [TestMethod]
    public void Command_IsClear_ReturnsTrue_ForClearState()
    {
        var clearRoute = new TrainRouteCommand(21, 31, TrainRouteState.Clear,
            [new PointCommand(1, PointPosition.Straight)]);

        Assert.IsTrue(clearRoute.IsClear);
    }

    #endregion

    #region IsUndefined Tests

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenFromSignalZeroAndSetMain()
    {
        var route = new TrainRouteCommand(0, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenFromSignalZeroAndSetShunting()
    {
        var route = new TrainRouteCommand(0, 31, TrainRouteState.SetShunting,
            [new PointCommand(1, PointPosition.Straight)]);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenToSignalIsZero()
    {
        var route = new TrainRouteCommand(21, 0, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenSetCommandHasNoPointCommands()
    {
        var route = new TrainRouteCommand(21, 31, TrainRouteState.SetMain, []);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_WhenSetCommandHasAllUndefinedPointCommands()
    {
        var route = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(0, PointPosition.Undefined)]);

        Assert.IsTrue(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsFalse_ForValidSetCommand()
    {
        var route = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]);

        Assert.IsFalse(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsFalse_ForClearWithEmptyPointCommands()
    {
        // Clear commands with empty PointCommands are valid (clear by ToSignal)
        var route = new TrainRouteCommand(0, 31, TrainRouteState.Clear, []);

        Assert.IsFalse(route.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsFalse_ForCancelWithEmptyPointCommands()
    {
        // Cancel commands with empty PointCommands are valid
        var route = new TrainRouteCommand(0, 31, TrainRouteState.Cancel, []);

        Assert.IsFalse(route.IsUndefined);
    }

    #endregion

    #region IsInConflictWith Tests

    [TestMethod]
    public void IsInConflictWith_ReturnsTrue_WhenSamePointDifferentPosition()
    {
        var route1 = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]);
        var route2 = new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Diverging)]);

        Assert.IsTrue(route1.IsInConflictWith(route2));
    }

    [TestMethod]
    public void IsInConflictWith_ReturnsFalse_WhenSamePointSamePosition()
    {
        var route1 = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]);
        var route2 = new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]);

        Assert.IsFalse(route1.IsInConflictWith(route2));
    }

    [TestMethod]
    public void IsInConflictWith_ReturnsFalse_WhenNoOverlappingPoints()
    {
        var route1 = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight)]);
        var route2 = new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new PointCommand(2, PointPosition.Diverging)]);

        Assert.IsFalse(route1.IsInConflictWith(route2));
    }

    [TestMethod]
    public void IsInConflictWith_ReturnsTrue_WhenAnyConflictExists()
    {
        var route1 = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight),
             new PointCommand(2, PointPosition.Diverging)]);
        var route2 = new TrainRouteCommand(22, 32, TrainRouteState.SetMain,
            [new PointCommand(1, PointPosition.Straight),  // Same position - no conflict
             new PointCommand(2, PointPosition.Straight)]); // Different position - conflict!

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
}
