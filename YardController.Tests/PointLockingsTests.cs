using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using YardController.Web.Services;

namespace YardController.Tests;

[TestClass]
public class PointLockingsTests
{
    private TrainRouteLockings _sut = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _sut = new TrainRouteLockings(loggerFactory.CreateLogger<TrainRouteLockings>());
    }

    #region IsLocked Tests

    [TestMethod]
    public void IsLocked_ReturnsFalse_WhenNoLocksExist()
    {
        var command = new PointCommand(1, PointPosition.Straight);
        Assert.IsFalse(_sut.IsLocked(command));
    }

    [TestMethod]
    public void IsLocked_ReturnsFalse_WhenLockExistsWithSamePosition()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));

        _sut.ReserveLocks(route);

        Assert.IsFalse(_sut.IsLocked(new PointCommand(1, PointPosition.Straight)));
    }

    [TestMethod]
    public void IsLocked_ReturnsTrue_WhenLockExistsWithDifferentPosition()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));

        _sut.ReserveLocks(route);

        Assert.IsTrue(_sut.IsLocked(new PointCommand(1, PointPosition.Diverging)));
    }

    #endregion

    #region IsUnchanged Tests

    [TestMethod]
    public void IsUnchanged_ReturnsFalse_WhenNoLocksExist()
    {
        var command = new PointCommand(1, PointPosition.Straight);
        Assert.IsFalse(_sut.IsUnchanged(command));
    }

    [TestMethod]
    public void IsUnchanged_ReturnsFalse_WhenLockExistsButNotCommitted()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));

        _sut.ReserveLocks(route);

        Assert.IsFalse(_sut.IsUnchanged(new PointCommand(1, PointPosition.Straight)));
    }

    [TestMethod]
    public void IsUnchanged_ReturnsTrue_WhenLockExistsAndCommittedWithSamePosition()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));

        _sut.ReserveLocks(route);
        _sut.CommitLocks(route);

        Assert.IsTrue(_sut.IsUnchanged(new PointCommand(1, PointPosition.Straight)));
    }

    [TestMethod]
    public void IsUnchanged_ReturnsFalse_WhenCommittedButDifferentPosition()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));

        _sut.ReserveLocks(route);
        _sut.CommitLocks(route);

        Assert.IsFalse(_sut.IsUnchanged(new PointCommand(1, PointPosition.Diverging)));
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
            new PointCommand(1, PointPosition.Straight));

        Assert.IsTrue(_sut.CanReserveLocksFor(route));
    }

    [TestMethod]
    public void CanReserveLocksFor_ReturnsFalse_WhenConflictingLockExists()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));
        _sut.ReserveLocks(route1);

        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Diverging));

        Assert.IsFalse(_sut.CanReserveLocksFor(route2));
    }

    [TestMethod]
    public void CanReserveLocksFor_ReturnsTrue_WhenSamePositionLockExists()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));
        _sut.ReserveLocks(route1);

        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));

        Assert.IsTrue(_sut.CanReserveLocksFor(route2));
    }

    #endregion

    #region ReserveOrClearLocks Tests

    [TestMethod]
    public void ReserveLocks_AddsLocks_WhenStateIsSet()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));

        _sut.ReserveLocks(route);

        Assert.HasCount(2, _sut.PointLocks);
    }

    [TestMethod]
    public void ReserveLocks_ClearsLocks_WhenStateIsClear()
    {
        var setRoute = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));
        _sut.ReserveLocks(setRoute);

        var clearRoute = CreateTrainRoute(21, 31, TrainRouteState.Clear,
            new PointCommand(1, PointPosition.Straight));
        _sut.ClearLocks(clearRoute);

        Assert.IsEmpty(_sut.PointLocks);
    }

    [TestMethod]
    public void ReserveLocks_ClearsLocksByToSignal_WhenFromSignalIsZero()
    {
        var setRoute = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));
        _sut.ReserveLocks(setRoute);
        _sut.CommitLocks(setRoute);

        // Clear with FromSignal = 0, which should find route by ToSignal
        var clearRoute = new TrainRouteCommand(0, 31, TrainRouteState.Clear, []);
        _sut.ClearLocks(clearRoute);

        Assert.IsEmpty(_sut.PointLocks);
    }

    #endregion

    #region CommitLocks Tests

    [TestMethod]
    public void CommitLocks_TransitionsLocksToCommitted()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));

        _sut.ReserveLocks(route);
        Assert.IsFalse(_sut.PointLocks.First().Committed);

        _sut.CommitLocks(route);
        Assert.IsTrue(_sut.PointLocks.First().Committed);
    }

    [TestMethod]
    public void CommitLocks_DoesNothingForNonSetState()
    {
        var clearRoute = CreateTrainRoute(21, 31, TrainRouteState.Clear,
            new PointCommand(1, PointPosition.Straight));

        _sut.CommitLocks(clearRoute); // Should not throw or add locks

        Assert.IsEmpty(_sut.PointLocks);
    }

    #endregion

    #region ClearAllLocks Tests

    [TestMethod]
    public void ClearAllLocks_RemovesAllLocks()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));
        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new PointCommand(2, PointPosition.Diverging));

        _sut.ReserveLocks(route1);
        _sut.ReserveLocks(route2);
        Assert.HasCount(2, _sut.PointLocks);

        _sut.ReleaseAllLocks();

        Assert.IsEmpty(_sut.PointLocks);
    }

    #endregion

    #region LockedPointsFor Tests

    [TestMethod]
    public void LockedPointsFor_ReturnsConflictingPoints()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));
        _sut.ReserveLocks(route1);

        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Diverging), // Conflicts
            new PointCommand(3, PointPosition.Straight)); // No conflict

        var lockedPoints = _sut.LockedPointsFor(route2).ToList();

        Assert.HasCount(1, lockedPoints);
        Assert.AreEqual(1, lockedPoints[0].Number);
    }

    [TestMethod]
    public void LockedPointsFor_ReturnsEmpty_WhenNoConflicts()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));
        _sut.ReserveLocks(route1);

        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new PointCommand(2, PointPosition.Diverging));

        var lockedPoints = _sut.LockedPointsFor(route2).ToList();

        Assert.IsEmpty(lockedPoints);
    }

    [TestMethod]
    public void LockedPointsFor_ExcludesSamePositionPoints()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));
        _sut.ReserveLocks(route1);

        // Route2 shares point 2 at same position (not a conflict) and conflicts on point 1
        var route2 = CreateTrainRoute(22, 32, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Diverging),  // Conflicts
            new PointCommand(2, PointPosition.Diverging));  // Same position - not a conflict

        var lockedPoints = _sut.LockedPointsFor(route2).ToList();

        Assert.HasCount(1, lockedPoints);
        Assert.AreEqual(1, lockedPoints[0].Number);
    }

    #endregion

    #region Full Lock Lifecycle Tests

    [TestMethod]
    public void FullLockLifecycle_ReserveCommitClear()
    {
        var route = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));

        // Reserve
        _sut.ReserveLocks(route);
        Assert.HasCount(2, _sut.PointLocks);
        Assert.IsTrue(_sut.PointLocks.All(sl => !sl.Committed));

        // Commit
        _sut.CommitLocks(route);
        Assert.IsTrue(_sut.PointLocks.All(sl => sl.Committed));

        // Clear
        var clearRoute = route with { State = TrainRouteState.Clear };
        _sut.ClearLocks(clearRoute);
        Assert.IsEmpty(_sut.PointLocks);
    }

    [TestMethod]
    public void MultipleRoutes_WithOverlappingPoints_SamePosition()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));

        var route2 = CreateTrainRoute(31, 41, TrainRouteState.SetMain,
            new PointCommand(2, PointPosition.Diverging), // Same point, same position
            new PointCommand(3, PointPosition.Straight));

        _sut.ReserveLocks(route1);
        Assert.IsTrue(_sut.CanReserveLocksFor(route2)); // Should be allowed
    }

    [TestMethod]
    public void SharedLock_NotDuplicated_WhenSamePositionReserved()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));

        var route2 = CreateTrainRoute(31, 41, TrainRouteState.SetMain,
            new PointCommand(2, PointPosition.Diverging),
            new PointCommand(3, PointPosition.Straight));

        _sut.ReserveLocks(route1);
        _sut.ReserveLocks(route2);

        // Point 2 should only have one lock entry, not two
        Assert.HasCount(3, _sut.PointLocks); // Points 1, 2, 3
    }

    [TestMethod]
    public void SharedLock_Retained_WhenOneRouteCleared()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));

        var route2 = CreateTrainRoute(31, 41, TrainRouteState.SetMain,
            new PointCommand(2, PointPosition.Diverging),
            new PointCommand(3, PointPosition.Straight));

        _sut.ReserveLocks(route1);
        _sut.CommitLocks(route1);
        _sut.ReserveLocks(route2);
        _sut.CommitLocks(route2);

        // Clear route1 - point 2 should remain locked because route2 still needs it
        var clearRoute1 = route1 with { State = TrainRouteState.Clear };
        var released = _sut.ClearLocks(clearRoute1);

        Assert.HasCount(1, released); // Only point 1 released
        Assert.AreEqual(1, released[0].Number);
        Assert.HasCount(2, _sut.PointLocks); // Points 2 and 3 still locked
        Assert.IsTrue(_sut.PointLocks.Any(pl => pl.PointCommand.Number == 2));
        Assert.IsTrue(_sut.PointLocks.Any(pl => pl.PointCommand.Number == 3));
    }

    [TestMethod]
    public void SharedLock_Released_WhenAllRoutesCleared()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));

        var route2 = CreateTrainRoute(31, 41, TrainRouteState.SetMain,
            new PointCommand(2, PointPosition.Diverging),
            new PointCommand(3, PointPosition.Straight));

        _sut.ReserveLocks(route1);
        _sut.CommitLocks(route1);
        _sut.ReserveLocks(route2);
        _sut.CommitLocks(route2);

        // Clear route1
        _sut.ClearLocks(route1 with { State = TrainRouteState.Clear });
        // Clear route2 - now point 2 should also be released
        var released = _sut.ClearLocks(route2 with { State = TrainRouteState.Clear });

        Assert.HasCount(2, released); // Points 2 and 3 released
        Assert.IsEmpty(_sut.PointLocks);
    }

    [TestMethod]
    public void SharedLock_PreventsConflictingRoute_WhileShared()
    {
        var route1 = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(2, PointPosition.Diverging));

        var route2 = CreateTrainRoute(31, 41, TrainRouteState.SetMain,
            new PointCommand(2, PointPosition.Diverging));

        _sut.ReserveLocks(route1);
        _sut.ReserveLocks(route2);

        // A third route wanting point 2 in a different position should be blocked
        var conflicting = CreateTrainRoute(51, 61, TrainRouteState.SetMain,
            new PointCommand(2, PointPosition.Straight));

        Assert.IsFalse(_sut.CanReserveLocksFor(conflicting));

        // Clear route1 - point 2 still needed by route2
        _sut.ClearLocks(route1 with { State = TrainRouteState.Clear });

        // Still blocked
        Assert.IsFalse(_sut.CanReserveLocksFor(conflicting));

        // Clear route2 - point 2 now free
        _sut.ClearLocks(route2 with { State = TrainRouteState.Clear });

        // Now allowed
        Assert.IsTrue(_sut.CanReserveLocksFor(conflicting));
    }

    #endregion

    #region Cancel State Tests

    [TestMethod]
    public void ClearLocks_HandlesCancelState()
    {
        var setRoute = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));
        _sut.ReserveLocks(setRoute);
        _sut.CommitLocks(setRoute);

        var cancelRoute = CreateTrainRoute(21, 31, TrainRouteState.Cancel,
            new PointCommand(1, PointPosition.Straight),
            new PointCommand(2, PointPosition.Diverging));
        _sut.ClearLocks(cancelRoute);

        Assert.IsEmpty(_sut.PointLocks);
    }

    [TestMethod]
    public void ClearLocks_CancelStateRemovesFromCurrentRoutes()
    {
        var setRoute = CreateTrainRoute(21, 31, TrainRouteState.SetMain,
            new PointCommand(1, PointPosition.Straight));
        _sut.ReserveLocks(setRoute);

        var cancelRoute = CreateTrainRoute(21, 31, TrainRouteState.Cancel,
            new PointCommand(1, PointPosition.Straight));
        _sut.ClearLocks(cancelRoute);

        // After cancel, the lock should be removed
        Assert.IsEmpty(_sut.PointLocks);
    }

    #endregion

    private static TrainRouteCommand CreateTrainRoute(int from, int to, TrainRouteState state, params PointCommand[] points)
    {
        return new TrainRouteCommand(from, to, state, points);
    }
}
