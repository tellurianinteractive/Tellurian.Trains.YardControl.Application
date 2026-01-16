using System.Diagnostics.CodeAnalysis;

namespace Tellurian.Trains.YardController;

public record struct PointLock(PointCommand PointCommand, bool Committed);
public sealed class TrainRouteLockings(ILogger<TrainRouteLockings> logger)
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly List<PointLock> _pointLocks = [];
    private readonly List<TrainRouteCommand> _currentTrainRouteCommands = [];

    public IEnumerable<PointLock> PointLocks => _pointLocks.AsReadOnly();
    private IEnumerable<PointCommand> PointCommands => _pointLocks.Select(pl => pl.PointCommand);
    public IEnumerable<PointCommand> LockedPointsFor(TrainRouteCommand trainRouteCommand) =>
        PointCommands.Intersect(trainRouteCommand.PointCommands, new PointCommandEqualityComparer());

    public bool CanReserveLocksFor(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsUndefined) return false;
        if (trainRouteCommand.IsSet && trainRouteCommand.PointCommands.Any(s => IsLocked(s))) return false;
        return true;
    }
    public void ClearLocks(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsClear)
        {
            if (trainRouteCommand.FromSignal == 0)
            {
                var existingRoute = _currentTrainRouteCommands.FirstOrDefault(tpc => tpc.ToSignal == trainRouteCommand.ToSignal);
                if (existingRoute is null)
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("No train route found that ends at signal number {ToSignal}", trainRouteCommand.ToSignal);
                }
                else
                {
                    foreach (var pointCommand in existingRoute.PointCommands) { ClearLock(pointCommand); }
                    _currentTrainRouteCommands.Remove(existingRoute);
                    if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared locks for train route command {TrainRouteCommand}", existingRoute);
                }
            }
            else
            {
                foreach (var pointCommand in trainRouteCommand.PointCommands) { ClearLock(pointCommand); }
                var clearedCount = _currentTrainRouteCommands.RemoveAll(tpc => tpc.ToSignal == trainRouteCommand.ToSignal);
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared locks for train route command {TrainRouteCommand}", trainRouteCommand);
            }
        }
        else if (trainRouteCommand.State.IsCancel)
        {
            foreach (var pointCommand in trainRouteCommand.PointCommands) { ClearLock(pointCommand); }
            var canceledCount = _currentTrainRouteCommands.RemoveAll(tpc => tpc.ToSignal == trainRouteCommand.ToSignal);
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Canceled locks for train route command {TrainRouteCommand}", trainRouteCommand);

        }
    }

    public void ReserveLocks(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsSet)
        {
            foreach (var pointCommand in trainRouteCommand.PointCommands) { ReserveLock(pointCommand); }
            _currentTrainRouteCommands.Add(trainRouteCommand);
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Reserved locks for train route command {TrainRouteCommand}", trainRouteCommand);
        }
    }
    public void CommitLocks(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsSet)
            foreach (var pointCommand in trainRouteCommand.PointCommands)
                CommitLock(pointCommand);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Committed locks for train route command {TrainRouteCommand}", trainRouteCommand);
    }


    public void ClearAllLocks()
    {
        if (_currentTrainRouteCommands.Count > 0)
        {
            var count = _currentTrainRouteCommands.Count;
            _currentTrainRouteCommands.Clear();
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared {Count} train route commands.", count);
        }
        if (_pointLocks.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("No locks to clear.");
        }
        else
        {
            var count = _pointLocks.Count;
            _pointLocks.Clear();
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared {Count} point locks.", count);
        }

    }

    private void ReserveLock(PointCommand command)
    {
        if (!IsLocked(command))
        {
            _pointLocks.Add(new(command, false));
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Reserved lock for point command {PointCommand}", command);
        }
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
    private void ClearLock(PointCommand command)
    {
        var existingLock = _pointLocks.FirstOrDefault(s => s.PointCommand.Number == command.Number);
        _pointLocks.Remove(existingLock);
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Cleared lock for point command {PointCommand}", command);

    }
    public bool IsLocked(PointCommand command) => _pointLocks.Any(s => s.PointCommand.Number == command.Number && s.PointCommand.Position != command.Position);
    public bool IsUnchanged(PointCommand command) => _pointLocks.Any(s => s.PointCommand.Number == command.Number && s.PointCommand.Position == command.Position && s.Committed);

    public override string ToString() => $"Current locked points: {string.Join(',', _pointLocks.Where(pl => pl.Committed).Select(pl => $"{pl.PointCommand.Number}{pl.PointCommand.Position.Char}"))}";
}

internal class PointCommandEqualityComparer : IEqualityComparer<PointCommand>
{
    public bool Equals(PointCommand? x, PointCommand? y) => x?.Number == y?.Number;
    public int GetHashCode([DisallowNull] PointCommand obj) => obj.Number.GetHashCode();
}
