using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed partial class TrainRouteLockings(ILogger<TrainRouteLockings> logger)
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly List<PointLock> _pointLocks = [];
    private readonly List<TrainRouteCommand> _currentTrainRouteCommands = [];
    private readonly Dictionary<int, IReadOnlyList<PointCommand>> _shuntingAreaLocks = [];
    private HashSet<int> _shunterOwnedPoints = [];

    public IEnumerable<PointLock> PointLocks => _pointLocks.AsReadOnly();
    public IReadOnlyList<TrainRouteCommand> CurrentRoutes => _currentTrainRouteCommands.AsReadOnly();
    public IEnumerable<PointCommand> PointCommands => _pointLocks.Select(pl => pl.PointCommand);

    /// <summary>
    /// Returns only the points from the route that actually conflict with existing locks (different position).
    /// </summary>
    public IEnumerable<PointCommand> LockedPointsFor(TrainRouteCommand trainRouteCommand) =>
        trainRouteCommand.PointCommands.Where(pc => IsLocked(pc));

    public bool CanReserveLocksFor(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsUndefined) return false;
        if (trainRouteCommand.IsSet && trainRouteCommand.PointCommands.Any(s => IsLocked(s))) return false;
        if (trainRouteCommand.IsSet && HasCoordinateConflict(trainRouteCommand)) return false;
        return true;
    }

    /// <summary>
    /// Returns the active routes that have overlapping switch coordinates with the given route.
    /// </summary>
    public IEnumerable<TrainRouteCommand> ConflictingRoutesFor(TrainRouteCommand trainRouteCommand)
    {
        var newCoordinates = trainRouteCommand.PointCommands
            .Where(pc => pc.IsOnRoute && pc.SwitchCoordinate.HasValue)
            .Select(pc => pc.SwitchCoordinate!.Value)
            .ToHashSet();

        if (newCoordinates.Count == 0) yield break;

        foreach (var existingRoute in _currentTrainRouteCommands)
        {
            if (existingRoute.PointCommands
                .Where(pc => pc.IsOnRoute && pc.SwitchCoordinate.HasValue)
                .Any(pc => newCoordinates.Contains(pc.SwitchCoordinate!.Value)))
            {
                yield return existingRoute;
            }
        }
    }

    private bool HasCoordinateConflict(TrainRouteCommand trainRouteCommand)
    {
        var newCoordinates = trainRouteCommand.PointCommands
            .Where(pc => pc.IsOnRoute && pc.SwitchCoordinate.HasValue)
            .Select(pc => pc.SwitchCoordinate!.Value)
            .ToHashSet();

        if (newCoordinates.Count == 0) return false;

        return _currentTrainRouteCommands.Any(existingRoute =>
            existingRoute.PointCommands
                .Where(pc => pc.IsOnRoute && pc.SwitchCoordinate.HasValue)
                .Any(pc => newCoordinates.Contains(pc.SwitchCoordinate!.Value)));
    }

    /// <summary>
    /// Clears locks for a train route. Only releases point locks that are not shared with other active routes.
    /// Returns the list of point commands whose locks were actually released.
    /// </summary>
    public IReadOnlyList<PointCommand> ClearLocks(TrainRouteCommand trainRouteCommand)
    {
        List<PointCommand> releasedPoints = [];

        if (trainRouteCommand.IsClear || trainRouteCommand.State.IsCancel)
        {
            var existingRoute = _currentTrainRouteCommands.FirstOrDefault(tpc => tpc.ToSignal == trainRouteCommand.ToSignal);
            if (existingRoute is null)
            {
                LogNoRouteEndingAtSignal(trainRouteCommand.ToSignal);
                return releasedPoints;
            }

            _currentTrainRouteCommands.Remove(existingRoute);

            var routePointCount = 0;
            foreach (var pointCommand in existingRoute.PointCommands)
            {
                routePointCount++;
                if (!IsPointNeededByOtherRoute(pointCommand))
                {
                    ReleaseLock(pointCommand);
                    releasedPoints.Add(pointCommand);
                }
            }

            LogRouteLocksReleased(
                trainRouteCommand.State.IsCancel ? "Canceled" : "Cleared", existingRoute, releasedPoints.Count, routePointCount);
        }

        return releasedPoints;
    }

    public void ReserveLocks(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsSet)
        {
            foreach (var pointCommand in trainRouteCommand.PointCommands) { ReserveLock(pointCommand); }
            _currentTrainRouteCommands.Add(trainRouteCommand);
            LogRouteLocksReserved(trainRouteCommand);
        }
    }
    public void CommitLocks(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsSet)
            foreach (var pointCommand in trainRouteCommand.PointCommands)
                CommitLock(pointCommand);
        LogRouteLocksCommitted(trainRouteCommand);
    }


    public void ReleaseAllLocks()
    {
        if (_currentTrainRouteCommands.Count > 0)
        {
            var count = _currentTrainRouteCommands.Count;
            _currentTrainRouteCommands.Clear();
            LogAllRouteLocksReleased(count);
        }
        if (_pointLocks.Count == 0)
        {
            LogNoLocksToClear();
        }
        else
        {
            var count = _pointLocks.Count;
            _pointLocks.Clear();
            LogAllPointLocksReleased(count);
        }

    }

    private void ReserveLock(PointCommand command)
    {
        if (_pointLocks.Any(s => s.PointCommand.Number == command.Number))
            return; // Point already locked (same position = shared, different position = CanReserveLocksFor would have prevented this)
        _pointLocks.Add(new(command, false));
        LogPointLockReserved(command);
    }

    private void CommitLock(PointCommand command)
    {
        var existing = _pointLocks.FirstOrDefault(s => s.PointCommand.Number == command.Number);
        if (existing != default)
        {
            _pointLocks.Remove(existing);
            _pointLocks.Add(new(existing.PointCommand, true));
            LogPointLockCommitted(command);
        }
    }
    private void ReleaseLock(PointCommand command)
    {
        var existingLock = _pointLocks.FirstOrDefault(s => s.PointCommand.Number == command.Number);
        _pointLocks.Remove(existingLock);
        LogPointLockReleased(command);
    }

    /// <summary>
    /// Holds the flank protection of a released shunting area. These locks behave like a route's —
    /// <see cref="IsLocked"/> reports them, so no route can move the point — but they belong to the
    /// area rather than to any route, so route teardown and <see cref="ReleaseAllLocks"/> leave them
    /// standing. Only locking the area again releases them.
    /// </summary>
    public void LockForShuntingArea(int areaNumber, IReadOnlyList<PointCommand> flankProtection)
    {
        _shuntingAreaLocks[areaNumber] = flankProtection;
        LogFlankProtectionLocked(flankProtection.Count, areaNumber);
    }

    /// <summary>
    /// Releases the flank protection held for a shunting area and returns the commands released.
    /// </summary>
    public IReadOnlyList<PointCommand> UnlockShuntingArea(int areaNumber)
    {
        if (!_shuntingAreaLocks.Remove(areaNumber, out var released)) return [];
        LogFlankProtectionReleased(released.Count, areaNumber);
        return released;
    }

    /// <summary>
    /// True if any of the given points is held by an active train route. A shunting area cannot be
    /// handed over while the interlocking still needs one of its points.
    /// </summary>
    public bool IsAnyLockedByRoute(IEnumerable<int> pointNumbers) =>
        pointNumbers.Any(number => _pointLocks.Any(pl => pl.PointCommand.Number == number));

    private bool IsPointNeededByOtherRoute(PointCommand command) =>
        _currentTrainRouteCommands.Any(route =>
            route.PointCommands.Any(pc => pc.Number == command.Number));

    /// <summary>
    /// Tells the lockings which points the released areas hand to a shunter. A released point
    /// overrides flank protection another released area holds on it: two areas that share a point,
    /// one operating it and one protecting with it, leave the shunter free to move it.
    /// </summary>
    public void SetShunterOwnedPoints(IEnumerable<int> pointNumbers) => _shunterOwnedPoints = [.. pointNumbers];

    public bool IsLocked(PointCommand command) =>
        _pointLocks.Any(s => s.PointCommand.Number == command.Number && s.PointCommand.Position != command.Position) ||
        (!_shunterOwnedPoints.Contains(command.Number) &&
            _shuntingAreaLocks.Values.Any(area => area.Any(pc => pc.Number == command.Number && pc.Position != command.Position)));
    public bool IsUnchanged(PointCommand command) => _pointLocks.Any(s => s.PointCommand.Number == command.Number && s.PointCommand.Position == command.Position && s.Committed);
    public override string ToString() => $"Current locked points: {string.Join(',', _pointLocks.Where(pl => pl.Committed).Select(pl => $"{pl.PointCommand.Number}{pl.PointCommand.Position.Sign}"))}";

    [LoggerMessage(Level = LogLevel.Warning, Message = "No train route found that ends at signal number {ToSignal}")]
    private partial void LogNoRouteEndingAtSignal(int toSignal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Action} locks for train route command {TrainRouteCommand}, released {Count} of {Total} point locks")]
    private partial void LogRouteLocksReleased(string action, TrainRouteCommand trainRouteCommand, int count, int total);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reserved locks for train route command {TrainRouteCommand}")]
    private partial void LogRouteLocksReserved(TrainRouteCommand trainRouteCommand);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Committed locks for train route command {TrainRouteCommand}")]
    private partial void LogRouteLocksCommitted(TrainRouteCommand trainRouteCommand);

    [LoggerMessage(Level = LogLevel.Information, Message = "Released locks for {Count} train route commands.")]
    private partial void LogAllRouteLocksReleased(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "No locks to clear.")]
    private partial void LogNoLocksToClear();

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Count} point locks.")]
    private partial void LogAllPointLocksReleased(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reserved lock for point command {PointCommand}")]
    private partial void LogPointLockReserved(PointCommand pointCommand);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Committed lock for point command {PointCommand}")]
    private partial void LogPointLockCommitted(PointCommand pointCommand);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Released lock for point command {PointCommand}")]
    private partial void LogPointLockReleased(PointCommand pointCommand);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Locked {Count} flank protection points for shunting area {Area}")]
    private partial void LogFlankProtectionLocked(int count, int area);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Released {Count} flank protection points for shunting area {Area}")]
    private partial void LogFlankProtectionReleased(int count, int area);
}
