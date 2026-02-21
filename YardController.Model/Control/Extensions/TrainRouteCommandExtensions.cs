namespace Tellurian.Trains.YardController.Model.Control.Extensions;

public static class TrainRouteCommandExtensions
{
    extension(TrainRouteCommand command)
    {
        public bool IsSet =>
            command.State.IsSet;

        public bool IsClear =>
            command.State.IsClear;

        public bool IsTeardown =>
            command.State.IsTeardown;

        public bool IsUndefined =>
            ((command.State == TrainRouteState.SetMain || command.State == TrainRouteState.SetShunting) && command.FromSignal == 0) ||
            command.ToSignal == 0 ||
            (command.IsSet && (!command.PointCommands.Any() || command.PointCommands.All(p => p.IsUndefined)));

        public bool IsInConflictWith(TrainRouteCommand other) =>
            command.PointCommands.Any(p => other.PointCommands.Any(op => op.Number == p.Number && op.Position != p.Position));

    }

    extension(IEnumerable<TrainRouteCommand> commands)
    {
        public IEnumerable<TrainRouteCommand> UpdateCommandsWithPointAddresses(IDictionary<int, Point> points)
        {
            foreach (var command in commands)
            {
                foreach (var pointCommand in command.PointCommands)
                {
                    pointCommand.AddAddresses(points.AddressesFor(pointCommand.Number, pointCommand.Position));
                }
                yield return command;
            }
        }
    }

    extension(Dictionary<int, int[]> pointAddresses)
    {
        public int[] AddressesFrom(int pointNumber) => pointAddresses.TryGetValue(pointNumber, out var adresses) ? adresses : [];
    }
}
