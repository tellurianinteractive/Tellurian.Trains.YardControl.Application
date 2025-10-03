namespace Tellurian.Trains.YardController;

public enum TrainPathState
{
    Set = 0,
    Clear = 1,
    Cancel = 2
}

public record TrainPathCommand(int FromSignal, int ToSignal, TrainPathState State, IEnumerable<SwitchCommand> Switches)
{
    public override string ToString() =>
        this.IsUndefined
        ? "Undefined"
        : $"{FromSignal}-{ToSignal}:{State} [{string.Join(", ", Switches.Select(s => $"{s.Address}:{s.State}"))}]";
};

public static class TrainPathCommandExtensions
{
    extension(TrainPathCommand command)
    {
        public bool IsUndefined => command.FromSignal == 0 || command.ToSignal == 0 || !command.Switches.Any() || command.Switches.All(s => s.IsUndefined);
   
        public bool IsInConflictWith(TrainPathCommand other) =>
            command.Switches.Any(s => other.Switches.Any(os => os.Address == s.Address && !s.IsUndefined && !os.IsUndefined));
    }

    extension (Dictionary<int, int> switchAddresses) 
    {
        public int AddressFrom(string switchNumber) => switchAddresses.TryGetValue(switchNumber.ToIntOrZero, out var address) ? address : 0;
    }
}
