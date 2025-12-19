namespace Tellurian.Trains.YardController;

public enum TrainRouteState
{
    Undefined = 0,
    Set = 1,
    Clear = 2,
    Cancel = 3
}

public record TrainRouteCommand(int FromSignal, int ToSignal, TrainRouteState State, IEnumerable<SwitchCommand> SwitchCommands)
{
    public override string ToString() =>
        this.IsUndefined
        ? "Undefined"
        : FromSignal == 0 ? $"-{ToSignal}:{State}"
        : $"{FromSignal}-{ToSignal}: [{string.Join(", ", SwitchCommands.Select(s => s.ToString()))}]";
};

public static class TrainPathCommandExtensions
{
    extension(TrainRouteCommand command)
    {
        public bool IsUndefined => command.State switch
        {
            TrainRouteState.Set => command.FromSignal == 0 || command.ToSignal == 0 || !command.SwitchCommands.Any() || command.SwitchCommands.All(s => s.IsUndefined),
            _ => command.ToSignal == 0,
        };

        public bool IsInConflictWith(TrainRouteCommand other) =>
            command.SwitchCommands.Any(s => other.SwitchCommands.Any(os => os.Number == s.Number && os.Direction != s.Direction));

    }

    extension(IEnumerable<TrainRouteCommand> commands)
    {
        internal IEnumerable<TrainRouteCommand> UpdateCommandsWithSwitchAdresses(IEnumerable<Switch> switches)
        {
            foreach (var command in commands)
            {
                foreach (var switchCommand in command.SwitchCommands)
                {
                    switchCommand.AddAddresses(switches.AddressesFor(switchCommand.Number));
                }
                yield return command;
            }
        }
    }
    extension(Dictionary<int, int[]> switchAddresses)
    {
        public int[] AddressesFrom(int switchNumber) => switchAddresses.TryGetValue(switchNumber, out var adresses) ? adresses : [];
    }
}
