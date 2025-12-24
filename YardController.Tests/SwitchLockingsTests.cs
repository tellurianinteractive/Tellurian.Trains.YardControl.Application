using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class SwitchLockingsTests
{
    private SwitchLockings _sut = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sut = new SwitchLockings(loggerFactory.CreateLogger<SwitchLockings>());
    }

    #region IsLocked Tests

    [TestMethod]
    public void IsLocked_ReturnsFalse_WhenNoLocksExist()
    {
        var command = new SwitchCommand(1, SwitchDirection.Straight);
        Assert.IsFalse(_sut.IsLocked(command));
    }

    [TestMethod]
    public void IsLocked_ReturnsFalse_WhenLockExistsWithSameDirection()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));

        _sut.ReserveOrClearLocks(route);

        Assert.IsFalse(_sut.IsLocked(new SwitchCommand(1, SwitchDirection.Straight)));
    }

    [TestMethod]
    public void IsLocked_ReturnsTrue_WhenLockExistsWithDifferentDirection()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));

        _sut.ReserveOrClearLocks(route);

        Assert.IsTrue(_sut.IsLocked(new SwitchCommand(1, SwitchDirection.Diverging)));
    }

    #endregion

    #region IsUnchanged Tests

    [TestMethod]
    public void IsUnchanged_ReturnsFalse_WhenNoLocksExist()
    {
        var command = new SwitchCommand(1, SwitchDirection.Straight);
        Assert.IsFalse(_sut.IsUnchanged(command));
    }

    [TestMethod]
    public void IsUnchanged_ReturnsFalse_WhenLockExistsButNotCommitted()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));

        _sut.ReserveOrClearLocks(route);

        Assert.IsFalse(_sut.IsUnchanged(new SwitchCommand(1, SwitchDirection.Straight)));
    }

    [TestMethod]
    public void IsUnchanged_ReturnsTrue_WhenLockExistsAndCommittedWithSameDirection()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));

        _sut.ReserveOrClearLocks(route);
        _sut.CommitLocks(route);

        Assert.IsTrue(_sut.IsUnchanged(new SwitchCommand(1, SwitchDirection.Straight)));
    }

    [TestMethod]
    public void IsUnchanged_ReturnsFalse_WhenCommittedButDifferentDirection()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));

        _sut.ReserveOrClearLocks(route);
        _sut.CommitLocks(route);

        Assert.IsFalse(_sut.IsUnchanged(new SwitchCommand(1, SwitchDirection.Diverging)));
    }

    #endregion

    #region CanReserveLocksFor Tests

    [TestMethod]
    public void CanReserveLocksFor_ReturnsFalse_WhenCommandIsUndefined()
    {
        var undefinedRoute = new TrainRouteCommand(0, 31, TrainRouteState.SetMain, []);
        Assert.IsFalse(_sut.CanReserveLocksFor(undefinedRoute));
    }

    [TestMethod]
    public void CanReserveLocksFor_ReturnsTrue_WhenNoConflicts()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));

        Assert.IsTrue(_sut.CanReserveLocksFor(route));
    }

    [TestMethod]
    public void CanReserveLocksFor_ReturnsFalse_WhenConflictingLockExists()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));
        _sut.ReserveOrClearLocks(route1);

        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Diverging));

        Assert.IsFalse(_sut.CanReserveLocksFor(route2));
    }

    [TestMethod]
    public void CanReserveLocksFor_ReturnsTrue_WhenSameDirectionLockExists()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));
        _sut.ReserveOrClearLocks(route1);

        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));

        Assert.IsTrue(_sut.CanReserveLocksFor(route2));
    }

    #endregion

    #region ReserveOrClearLocks Tests

    [TestMethod]
    public void ReserveOrClearLocks_AddsLocks_WhenStateIsSet()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight),
            new SwitchCommand(2, SwitchDirection.Diverging));

        _sut.ReserveOrClearLocks(route);

        Assert.HasCount(2, _sut.SwitchLocks);
    }

    [TestMethod]
    public void ReserveOrClearLocks_ClearsLocks_WhenStateIsClear()
    {
        var setRoute = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));
        _sut.ReserveOrClearLocks(setRoute);

        var clearRoute = CreateTrainRoute(21, 31, TrainRouteState.Clear,
            new SwitchCommand(1, SwitchDirection.Straight));
        _sut.ReserveOrClearLocks(clearRoute);

        Assert.IsEmpty(_sut.SwitchLocks);
    }

    [TestMethod]
    public void ReserveOrClearLocks_ClearsLocksByToSignal_WhenFromSignalIsZero()
    {
        var setRoute = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight),
            new SwitchCommand(2, SwitchDirection.Diverging));
        _sut.ReserveOrClearLocks(setRoute);
        _sut.CommitLocks(setRoute);

        // Clear with FromSignal = 0, which should find route by ToSignal
        var clearRoute = new TrainRouteCommand(0, 31, TrainRouteState.Clear, []);
        _sut.ReserveOrClearLocks(clearRoute);

        Assert.IsEmpty(_sut.SwitchLocks);
    }

    #endregion

    #region CommitLocks Tests

    [TestMethod]
    public void CommitLocks_TransitionsLocksToCommitted()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));

        _sut.ReserveOrClearLocks(route);
        Assert.IsFalse(_sut.SwitchLocks.First().Committed);

        _sut.CommitLocks(route);
        Assert.IsTrue(_sut.SwitchLocks.First().Committed);
    }

    [TestMethod]
    public void CommitLocks_DoesNothingForNonSetState()
    {
        var clearRoute = CreateTrainRoute(21, 31, TrainRouteState.Clear,
            new SwitchCommand(1, SwitchDirection.Straight));

        _sut.CommitLocks(clearRoute); // Should not throw or add locks

        Assert.IsEmpty(_sut.SwitchLocks);
    }

    #endregion

    #region ClearAllLocks Tests

    [TestMethod]
    public void ClearAllLocks_RemovesAllLocks()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));
        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new SwitchCommand(2, SwitchDirection.Diverging));

        _sut.ReserveOrClearLocks(route1);
        _sut.ReserveOrClearLocks(route2);
        Assert.HasCount(2, _sut.SwitchLocks);

        _sut.ClearAllLocks();

        Assert.IsEmpty(_sut.SwitchLocks);
    }

    [TestMethod]
    public void ClearAllLocks_WorksOnEmptyLockList()
    {
        _sut.ClearAllLocks(); // Should not throw
        Assert.IsEmpty(_sut.SwitchLocks);
    }

    #endregion

    #region LockedSwitchesFor Tests

    [TestMethod]
    public void LockedSwitchesFor_ReturnsConflictingSwitches()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight),
            new SwitchCommand(2, SwitchDirection.Diverging));
        _sut.ReserveOrClearLocks(route1);

        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Diverging), // Conflicts
            new SwitchCommand(3, SwitchDirection.Straight)); // No conflict

        var lockedSwitches = _sut.LockedSwitchesFor(route2).ToList();

        Assert.HasCount(1, lockedSwitches);
        Assert.AreEqual(1, lockedSwitches[0].Number);
    }

    [TestMethod]
    public void LockedSwitchesFor_ReturnsEmpty_WhenNoConflicts()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight));
        _sut.ReserveOrClearLocks(route1);

        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new SwitchCommand(2, SwitchDirection.Diverging));

        var lockedSwitches = _sut.LockedSwitchesFor(route2).ToList();

        Assert.IsEmpty(lockedSwitches);
    }

    #endregion

    #region Full Lock Lifecycle Tests

    [TestMethod]
    public void FullLockLifecycle_ReserveCommitClear()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight),
            new SwitchCommand(2, SwitchDirection.Diverging));

        // Reserve
        _sut.ReserveOrClearLocks(route);
        Assert.HasCount(2, _sut.SwitchLocks);
        Assert.IsTrue(_sut.SwitchLocks.All(sl => !sl.Committed));

        // Commit
        _sut.CommitLocks(route);
        Assert.IsTrue(_sut.SwitchLocks.All(sl => sl.Committed));

        // Clear
        var clearRoute = route with { State = TrainRouteState.Clear };
        _sut.ReserveOrClearLocks(clearRoute);
        Assert.IsEmpty(_sut.SwitchLocks);
    }

    [TestMethod]
    public void MultipleRoutes_WithOverlappingSwitches_SameDirection()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new SwitchCommand(1, SwitchDirection.Straight),
            new SwitchCommand(2, SwitchDirection.Diverging));

        var route2 = CreateTrainRoute(31, 41, TrainRouteState.SetMain,
            new SwitchCommand(2, SwitchDirection.Diverging), // Same switch, same direction
            new SwitchCommand(3, SwitchDirection.Straight));

        _sut.ReserveOrClearLocks(route1);
        Assert.IsTrue(_sut.CanReserveLocksFor(route2)); // Should be allowed
    }

    #endregion

    private static TrainRouteCommand CreateTrainRoute(int from, int to, TrainRouteState state, params SwitchCommand[] switches)
    {
        return new TrainRouteCommand(from, to, state, switches);
    }
}
