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
                    pointCommand.MessageKinds ??= points.MessageKindsFor(pointCommand.Number);
                }
                yield return command;
            }
        }
    }

    extension(TrainRouteCommand route)
    {
        /// <summary>
        /// Returns a copy of the route with each PointCommand expanded into its full slave cascade.
        /// Cross-PointCommand conflicts (same point pinned at two different positions across the
        /// route's masters and their cascaded slaves) throw <see cref="InvalidPointCascadeException"/>.
        /// </summary>
        public TrainRouteCommand WithSlavesExpanded(IDictionary<int, Point> points)
        {
            var visited = new Dictionary<int, PointPosition>();
            var expanded = new List<PointCommand>();
            foreach (var pc in route.PointCommands)
            {
                foreach (var c in pc.ExpandWithSlaves(points))
                {
                    if (visited.TryGetValue(c.Number, out var existing))
                    {
                        if (existing != c.Position)
                            throw new InvalidPointCascadeException(
                                $"Route {route.FromSignal}-{route.ToSignal}: point {c.Number} requested at {c.Position} but already pinned at {existing}",
                                c.Number, existing, c.Position);
                        continue;
                    }
                    visited[c.Number] = c.Position;
                    expanded.Add(c);
                }
            }
            return route with { PointCommands = expanded };
        }
    }

    extension(Dictionary<int, int[]> pointAddresses)
    {
        public int[] AddressesFrom(int pointNumber) => pointAddresses.TryGetValue(pointNumber, out var adresses) ? adresses : [];
    }
}
