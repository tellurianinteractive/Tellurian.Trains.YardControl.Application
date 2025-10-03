using System.Diagnostics.CodeAnalysis;

namespace Tellurian.Trains.YardController;

internal class SwitchLockings
{
    private readonly List<SwitchCommand> _lockedSwitches = [];

    public IEnumerable<SwitchCommand> LockedSwitches => _lockedSwitches.AsReadOnly();
    public IEnumerable<SwitchCommand> LockedSwitchesFor(TrainPathCommand trainPathCommand) =>
        _lockedSwitches.Intersect(trainPathCommand.Switches, new SwitchCommandAddressEqualityComparer());

    public bool CanSetTrainPath(TrainPathCommand trainPathCommand)
    {
        if (trainPathCommand.IsUndefined) return false;
        if (trainPathCommand.State == TrainPathState.Set && trainPathCommand.Switches.Any(s => IsLocked(s))) return false;
        UpdateLock(trainPathCommand);
        return true;
    }
    private void UpdateLock(TrainPathCommand trainPathCommand)
    {
        if (trainPathCommand.State == TrainPathState.Set)
            foreach (var switchCommand in trainPathCommand.Switches) { Lock(switchCommand); }
        else if (trainPathCommand.State == TrainPathState.Clear)
            foreach (var switchCommand in trainPathCommand.Switches) { Unlock(switchCommand); }
        else if (trainPathCommand.State == TrainPathState.Cancel)
            foreach (var switchCommand in trainPathCommand.Switches) { Unlock(switchCommand); }
    }

    public void ClearLocks()

    {
        _lockedSwitches.Clear();
    }

    private void Lock(SwitchCommand command)
    {
        if (!IsLocked(command)) _lockedSwitches.Add(command);
    }
    private void Unlock(SwitchCommand command)
    {
        var existing = _lockedSwitches.FirstOrDefault(s => s.Address == command.Address);
        if (existing.Address != 0) _lockedSwitches.Remove(existing);
    }
    public bool IsLocked(SwitchCommand command) => _lockedSwitches.Any(s => s.Address == command.Address);

    public override string ToString() => string.Join(", ", _lockedSwitches.Select(s => $"{s.Address}"));
}

internal class SwitchCommandAddressEqualityComparer : IEqualityComparer<SwitchCommand>
{
    public bool Equals(SwitchCommand x, SwitchCommand y) => x.Address == y.Address;
    public int GetHashCode([DisallowNull] SwitchCommand obj) => obj.Address.GetHashCode();
}