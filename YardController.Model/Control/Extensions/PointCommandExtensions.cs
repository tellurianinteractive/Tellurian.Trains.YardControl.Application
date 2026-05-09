namespace Tellurian.Trains.YardController.Model.Control.Extensions;

/// <summary>
/// Thrown when expanding a point command's slave cascade leads to a contradictory
/// position request on the same point (e.g. 10- forces 8-, but 8+ was already requested).
/// </summary>
public sealed class InvalidPointCascadeException(string message, int pointNumber, PointPosition existing, PointPosition requested) : Exception(message)
{
    public int PointNumber { get; } = pointNumber;
    public PointPosition Existing { get; } = existing;
    public PointPosition Requested { get; } = requested;
}

public static class PointCommandExtensions
{
    extension(PointCommand command)
    {
        public static PointCommand Undefined => new(0, PointPosition.Undefined);

        public bool IsUndefined => command.Position == PointPosition.Undefined;

        internal PointLock ToPointLock => new(command, false);

        public PointCommand AsLockOrUnlockCommand => Create(command.Number, command.Position, [.. command.LockAddresses]);

        public static PointCommand Create(int number, PointPosition position, int[] addresses, int? lockAddressOffset = null, bool isOnRoute = true)
        {
            var result = new PointCommand(number, position, lockAddressOffset, isOnRoute);
            result.AddAddresses(addresses);
            return result;
        }

        public static bool Equals(PointCommand one, PointCommand another) =>
            one.Number == another.Number &&
            one.Position == another.Position &&
            one.Addresses.SequenceEqual(another.Addresses);

        /// <summary>
        /// Returns the master command followed by every transitively-cascaded slave command.
        /// Slaves are looked up via <paramref name="points"/>; their addresses, lock offset and
        /// message kinds are populated from the slave Point. The master command's <c>IsOnRoute</c>
        /// is inherited by all slaves.
        /// Idempotent: cascading to a point already pinned at the same position is a no-op.
        /// Throws <see cref="InvalidPointCascadeException"/> on a same-point opposite-position conflict.
        /// </summary>
        public IReadOnlyList<PointCommand> ExpandWithSlaves(IDictionary<int, Point> points)
        {
            var visited = new Dictionary<int, PointPosition> { [command.Number] = command.Position };
            var result = new List<PointCommand> { command };
            var queue = new Queue<(int Number, PointPosition Position)>();
            queue.Enqueue((command.Number, command.Position));

            while (queue.Count > 0)
            {
                var (num, pos) = queue.Dequeue();
                if (!points.TryGetValue(num, out var point) || point.Slaves is null) continue;
                foreach (var slave in point.Slaves.Where(s => s.WhenMaster == pos))
                {
                    if (visited.TryGetValue(slave.Number, out var existing))
                    {
                        if (existing != slave.Position)
                            throw new InvalidPointCascadeException(
                                $"Point {slave.Number} would be pinned at {slave.Position} via {num}{pos.Char} but is already at {existing}",
                                slave.Number, existing, slave.Position);
                        continue;
                    }
                    if (!points.TryGetValue(slave.Number, out var slavePoint))
                        throw new InvalidPointCascadeException(
                            $"Slave point {slave.Number} (referenced from point {num}) not in points configuration",
                            slave.Number, PointPosition.Undefined, slave.Position);

                    visited[slave.Number] = slave.Position;
                    var addresses = slave.Position == PointPosition.Straight
                        ? slavePoint.StraightAddresses
                        : slavePoint.DivergingAddresses;
                    int? slaveLockOffset = command.LockAddressOffset is not null && slavePoint.LockAddressOffset > 0
                        ? slavePoint.LockAddressOffset
                        : null;
                    var slaveCommand = Create(slave.Number, slave.Position, addresses, slaveLockOffset, command.IsOnRoute);
                    slaveCommand.MessageKinds = slavePoint.MessageKinds;
                    result.Add(slaveCommand);
                    queue.Enqueue((slave.Number, slave.Position));
                }
            }
            return result;
        }
    }

    extension(string? commandText)
    {
        public PointCommand ToPointCommand()
        {
            if (commandText is null || commandText.Length < 2) return PointCommand.Undefined;

            // Check for 'x' prefix (off-route point for flank protection)
            var isOnRoute = true;
            var text = commandText;
            if (text.StartsWith('x') || text.StartsWith('X'))
            {
                isOnRoute = false;
                text = text[1..];
            }

            if (text.Length < 2) return PointCommand.Undefined;
            var number = text[0..^1].ToIntOrZero;
            var position = text[^1].ToPointPosition;
            return new PointCommand(number, position, null, isOnRoute);
        }

        private PointPosition PointPositionFromText
        {
            get
            {
                if (commandText is null or { Length: < 2 }) return PointPosition.Undefined;
                return commandText[^1].ToPointPosition;
            }
        }
    }
}
