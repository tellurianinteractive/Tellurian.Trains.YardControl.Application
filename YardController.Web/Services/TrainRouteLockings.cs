using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed class TrainRouteLockings(ILogger<TrainRouteLockings> logger)
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly List<PointLock> _pointLocks = [];
    private readonly List<TrainRouteCommand> _currentTrainRouteCommands = [];

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
        return true;
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
                if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("No train route found that ends at signal number {ToSignal}", trainRouteCommand.ToSignal);
                return releasedPoints;
            }

            _currentTrainRouteCommands.Remove(existingRoute);

            foreach (var pointCommand in existingRoute.PointCommands)
            {
                if (!IsPointNeededByOtherRoute(pointCommand))
                {
                    ReleaseLock(pointCommand);
                    releasedPoints.Add(pointCommand);
                }
            }

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("{Action} locks for train route command {TrainRouteCommand}, released {Count} of {Total} point locks",
                    trainRouteCommand.State.IsCancel ? "Canceled" : "Cleared", existingRoute, releasedPoints.Count, existingRoute.PointCommands.Count());
        }

        return releasedPoints;
    }

    public void ReserveLocks(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsSet)
        {
            foreach (var pointCommand in trainRouteCommand.PointCommands) { ReserveLock(pointCommand); }
            _currentTrainRouteCommands.Add(trainRouteCommand);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Reserved locks for train route command {TrainRouteCommand}", trainRouteCommand);
        }
    }
    public void CommitLocks(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsSet)
            foreach (var pointCommand in trainRouteCommand.PointCommands)
                CommitLock(pointCommand);
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Committed locks for train route command {TrainRouteCommand}", trainRouteCommand);
    }


    public void ReleaseAllLocks()
    {
        if (_currentTrainRouteCommands.Count > 0)
        {
            var count = _currentTrainRouteCommands.Count;
            _currentTrainRouteCommands.Clear();
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Relesed locks for {Count} train route commands.", count);
        }
        if (_pointLocks.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("No locks to clear.");
        }
        else
        {
            var count = _pointLocks.Count;
            _pointLocks.Clear();
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Released {Count} point locks.", count);
        }

    }

    private void ReserveLock(PointCommand command)
    {
        if (_pointLocks.Any(s => s.PointCommand.Number == command.Number))
            return; // Point already locked (same position = shared, different position = CanReserveLocksFor would have prevented this)
        _pointLocks.Add(new(command, false));
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Reserved lock for point command {PointCommand}", command);
    }

    private void CommitLock(PointCommand command)
    {
        var existing = _pointLocks.FirstOrDefault(s => s.PointCommand.Number == command.Number);
        if (existing != default)
        {
            _pointLocks.Remove(existing);
            _pointLocks.Add(new(existing.PointCommand, true));
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Committed lock for point command {PointCommand}", command);
        }
    }
    private void ReleaseLock(PointCommand command)
    {
        var existingLock = _pointLocks.FirstOrDefault(s => s.PointCommand.Number == command.Number);
        _pointLocks.Remove(existingLock);
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Released lock for point command {PointCommand}", command);
    }

    private bool IsPointNeededByOtherRoute(PointCommand command) =>
        _currentTrainRouteCommands.Any(route =>
            route.PointCommands.Any(pc => pc.Number == command.Number));

    public bool IsLocked(PointCommand command) => _pointLocks.Any(s => s.PointCommand.Number == command.Number && s.PointCommand.Position != command.Position);
    public bool IsUnchanged(PointCommand command) => _pointLocks.Any(s => s.PointCommand.Number == command.Number && s.PointCommand.Position == command.Position && s.Committed);
    public override string ToString() => $"Current locked points: {string.Join(',', _pointLocks.Where(pl => pl.Committed).Select(pl => $"{pl.PointCommand.Number}{pl.PointCommand.Position.Char}"))}";
}
