using System.Diagnostics.CodeAnalysis;

namespace Tellurian.Trains.YardController;

public record struct SwitchLock(SwitchCommand SwitchCommand, bool Committed);
public sealed class SwitchLockings(ILogger<SwitchLockings> logger)
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly List<SwitchLock> _switchLocks = [];
    private readonly List<TrainRouteCommand> _currentTrainPathCommands = [];

    public IEnumerable<SwitchLock> SwitchLocks => _switchLocks.AsReadOnly();
    private IEnumerable<SwitchCommand> SwitchCommands => _switchLocks.Select(sl => sl.SwitchCommand);
    public IEnumerable<SwitchCommand> LockedSwitchesFor(TrainRouteCommand trainPathCommand) =>
        SwitchCommands.Intersect(trainPathCommand.SwitchCommands, new SwitchCommandEqualityComparer());

    public bool CanReserveLocksFor(TrainRouteCommand trainPathCommand)
    {
        if (trainPathCommand.IsUndefined) return false;
        if (trainPathCommand.State == TrainRouteState.Set && trainPathCommand.SwitchCommands.Any(s => IsLocked(s))) return false;
        return true;
    }
    public void ReserveOrClearLocks(TrainRouteCommand trainPathCommand)
    {
        switch (trainPathCommand.State)
        {
            case TrainRouteState.Set:
                foreach (var switchCommand in trainPathCommand.SwitchCommands) { ReserveLock(switchCommand); }
                _currentTrainPathCommands.Add(trainPathCommand);
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Reserved locks for train route command {TrainPathCommand}", trainPathCommand);
                break;
            case TrainRouteState.Clear:
                if (trainPathCommand.FromSignal == 0)
                {
                    var existingPath = _currentTrainPathCommands.FirstOrDefault(tpc => tpc.ToSignal == trainPathCommand.ToSignal);
                    if (existingPath != default)
                    {
                        foreach (var switchCommand in existingPath.SwitchCommands) { ClearLock(switchCommand); }
                        _currentTrainPathCommands.Remove(existingPath);
                        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared locks for train route command {TrainPathCommand}", existingPath);
                    }
                }
                else
                {
                    foreach (var switchCommand in trainPathCommand.SwitchCommands) { ClearLock(switchCommand); }
                    var clearedCount = _currentTrainPathCommands.RemoveAll(tpc => tpc.ToSignal == trainPathCommand.ToSignal);
                    if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared locks for train route command {TrainPathCommand}", trainPathCommand);
                }
                break;
            case TrainRouteState.Cancel:
                foreach (var switchCommand in trainPathCommand.SwitchCommands) { ClearLock(switchCommand); }
                var canceledCount = _currentTrainPathCommands.RemoveAll(tpc => tpc.ToSignal == trainPathCommand.ToSignal);
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Canceled locks for train route command {TrainPathCommand}", trainPathCommand);
                break;
        }
    }
    public void CommitLocks(TrainRouteCommand trainPathCommand)
    {
        if (trainPathCommand.State == TrainRouteState.Set)
        {
            foreach (var switchCommand in trainPathCommand.SwitchCommands)
                CommitLock(switchCommand);
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Comitted locks for train route command {TrainPathCommand}", trainPathCommand);
        }
    }

    public void ClearAllLocks()
    {
        _switchLocks.Clear();
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Cleared all switch locks.");
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
        var existing = _switchLocks.FirstOrDefault(s => s.SwitchCommand.Number == command.Number);
        _switchLocks.Remove(existing);
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Cleared lock for switch command {SwitchCommand}", command);

    }
    public bool IsLocked(SwitchCommand command) => _switchLocks.Any(s => s.SwitchCommand.Number == command.Number && s.SwitchCommand.Direction != command.Direction);
    public bool IsUnchanged(SwitchCommand command) => _switchLocks.Any(s => s.SwitchCommand.Number == command.Number && s.SwitchCommand.Direction == command.Direction && s.Committed);

    public override string ToString() => string.Join(", ", _switchLocks.Select(s => $"{s.SwitchCommand.Number} comitted={s.Committed}"));
}

internal class SwitchCommandEqualityComparer : IEqualityComparer<SwitchCommand>
{
    public bool Equals(SwitchCommand? x, SwitchCommand? y) => x?.Number == y?.Number;
    public int GetHashCode([DisallowNull] SwitchCommand obj) => obj.Number.GetHashCode();
}