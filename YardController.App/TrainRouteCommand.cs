namespace Tellurian.Trains.YardController;

public enum TrainRouteState
{
    Undefined = 0,
    Unset = 1,
    SetMain = 2,
    SetShunting = 3,
    Clear = 4,
    Cancel = 5
}

public static class TrainRouteStateExtensions
{
    extension(TrainRouteState state)
    {
        public bool IsSet => state == TrainRouteState.SetMain || state == TrainRouteState.SetShunting;
        public bool IsClear => state == TrainRouteState.Clear;
        public bool IsCancel => state == TrainRouteState.Cancel;
    }
}

public record TrainRouteCommand(int FromSignal, int ToSignal, TrainRouteState State, IEnumerable<SwitchCommand> SwitchCommands)
{
    public override string ToString() =>
        this.IsUndefined
        ? "Undefined"
        : FromSignal == 0 ? $"-{ToSignal}:{State}"
        : $"{FromSignal}-{ToSignal}: [{string.Join(" → ", SwitchCommands.Select(s => $"{s.Number}{s.Direction.Char}"))}]";
};

public static class TrainPathCommandExtensions
{
    extension(TrainRouteCommand command)
    {
        public bool IsSet =>
            command.State.IsSet;

        public bool IsClear =>
            command.State.IsClear;

        public bool IsUndefined =>
            ((command.State == TrainRouteState.SetMain || command.State == TrainRouteState.SetShunting) && command.FromSignal == 0) ||
            command.ToSignal == 0 ||
            (command.IsSet && (!command.SwitchCommands.Any() || command.SwitchCommands.All(s => s.IsUndefined)));

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
