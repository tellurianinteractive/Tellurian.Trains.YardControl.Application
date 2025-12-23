using System.Diagnostics.CodeAnalysis;

namespace Tellurian.Trains.YardController;

public record struct SwitchLock(SwitchCommand SwitchCommand, bool Committed);
public sealed class SwitchLockings(ILogger<SwitchLockings> logger)
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly List<SwitchLock> _switchLocks = [];
    private readonly List<TrainRouteCommand> _currentTrainRouteCommands = [];

    public IEnumerable<SwitchLock> SwitchLocks => _switchLocks.AsReadOnly();
    private IEnumerable<SwitchCommand> SwitchCommands => _switchLocks.Select(sl => sl.SwitchCommand);
    public IEnumerable<SwitchCommand> LockedSwitchesFor(TrainRouteCommand trainRouteCommand) =>
        SwitchCommands.Intersect(trainRouteCommand.SwitchCommands, new SwitchCommandEqualityComparer());

    public bool CanReserveLocksFor(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsUndefined) return false;
        if (trainRouteCommand.IsSet && trainRouteCommand.SwitchCommands.Any(s => IsLocked(s))) return false;
        return true;
    }
    public void ReserveOrClearLocks(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsSet)
        {
            foreach (var switchCommand in trainRouteCommand.SwitchCommands) { ReserveLock(switchCommand); }
            _currentTrainRouteCommands.Add(trainRouteCommand);
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Reserved locks for train route command {TrainRouteCommand}", trainRouteCommand);

        }
        else if (trainRouteCommand.IsClear)
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
                    foreach (var switchCommand in existingRoute.SwitchCommands) { ClearLock(switchCommand); }
                    _currentTrainRouteCommands.Remove(existingRoute);
                    if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared locks for train route command {TrainRouteCommand}", existingRoute);
                }
            }
            else
            {
                foreach (var switchCommand in trainRouteCommand.SwitchCommands) { ClearLock(switchCommand); }
                var clearedCount = _currentTrainRouteCommands.RemoveAll(tpc => tpc.ToSignal == trainRouteCommand.ToSignal);
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared locks for train route command {TrainRouteCommand}", trainRouteCommand);
            }
        }
        else if (trainRouteCommand.State.IsCancel)
        {
            foreach (var switchCommand in trainRouteCommand.SwitchCommands) { ClearLock(switchCommand); }
            var canceledCount = _currentTrainRouteCommands.RemoveAll(tpc => tpc.ToSignal == trainRouteCommand.ToSignal);
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Canceled locks for train route command {TrainRouteCommand}", trainRouteCommand);

        }
    }
    public void CommitLocks(TrainRouteCommand trainRouteCommand)
    {
        if (trainRouteCommand.IsSet)
            foreach (var switchCommand in trainRouteCommand.SwitchCommands)
                CommitLock(switchCommand);
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
        if (_switchLocks.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("No locks to clear.");
        }
        else
        {
            var count = _switchLocks.Count;
            _switchLocks.Clear();
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared {Count} switch locks.", count);
        }

    }

    private void ReserveLock(SwitchCommand command)
    {
        if (!IsLocked(command))
        {
            _switchLocks.Add(new(command, false));
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Reserved lock for switch command {SwitchCommand}", command);
        }
    }

    private void CommitLock(SwitchCommand command)
    {
        var existing = _switchLocks.FirstOrDefault(s => s.SwitchCommand.Number == command.Number);
        if (existing != default)
        {
            _switchLocks.Remove(existing);
            _switchLocks.Add(new(existing.SwitchCommand, true));
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Committed lock for switch command {SwitchCommand}", command);
        }
    }
    private void ClearLock(SwitchCommand command)
    {
        var existingLock = _switchLocks.FirstOrDefault(s => s.SwitchCommand.Number == command.Number);
        _switchLocks.Remove(existingLock);
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Cleared lock for switch command {SwitchCommand}", command);

    }
    public bool IsLocked(SwitchCommand command) => _switchLocks.Any(s => s.SwitchCommand.Number == command.Number && s.SwitchCommand.Direction != command.Direction);
    public bool IsUnchanged(SwitchCommand command) => _switchLocks.Any(s => s.SwitchCommand.Number == command.Number && s.SwitchCommand.Direction == command.Direction && s.Committed);

    public override string ToString() => $"Current locked switches: {string.Join(',', _switchLocks.Where(sl => sl.Committed).Select(sl => $"{sl.SwitchCommand.Number}{sl.SwitchCommand.Direction.Char}"))}";
}

internal class SwitchCommandEqualityComparer : IEqualityComparer<SwitchCommand>
{
    public bool Equals(SwitchCommand? x, SwitchCommand? y) => x?.Number == y?.Number;
    public int GetHashCode([DisallowNull] SwitchCommand obj) => obj.Number.GetHashCode();
}