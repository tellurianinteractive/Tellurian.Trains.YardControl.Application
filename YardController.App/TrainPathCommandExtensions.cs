namespace Tellurian.Trains.YardController;

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
