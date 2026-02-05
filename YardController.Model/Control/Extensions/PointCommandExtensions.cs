namespace Tellurian.Trains.YardController.Model.Control.Extensions;

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
            var cmd = new PointCommand(number, position, lockAddressOffset, isOnRoute);
            cmd.AddAddresses(addresses);
            return cmd;
        }

        public static bool Equals(PointCommand one, PointCommand another) =>
            one.Number == another.Number &&
            one.Position == another.Position &&
            one.Addresses.SequenceEqual(another.Addresses);
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
