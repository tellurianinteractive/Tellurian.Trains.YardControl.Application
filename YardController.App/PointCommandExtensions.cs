using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet.Commands;
using Tellurian.Trains.YardController.Extensions;

namespace Tellurian.Trains.YardController;

public static class PointCommandExtensions
{
    extension(PointCommand command)
    {
        public static PointCommand Undefined => new(0, PointPosition.Undefined);

        public bool IsUndefined => command.Position == PointPosition.Undefined;

        internal PointLock ToPointLock => new(command, false);

        public PointCommand AsLockOrUnlockCommand => Create(command.Number, command.Position, [.. command.LockAddresses]);

        public static PointCommand Create(int number, PointPosition position, int[] addresses, int? lockAddressOffset = null)
        {
            var cmd = new PointCommand(number, position, lockAddressOffset);
            cmd.AddAddresses(addresses);
            return cmd;
        }

        public static bool Equals(PointCommand one, PointCommand another) =>
            one.Number == another.Number &&
            one.Position == another.Position &&
            one.Addresses.SequenceEqual(another.Addresses);

        public IEnumerable<SetTurnoutCommand> ToLocoNetCommands(MotorState motorState = MotorState.On)
        {
            foreach (var address in command.Addresses)
            {
                if (command.IsUndefined) continue;
                var locoNetPosition = command.Position.WithAddressSignConsidered((short)address).LocoNetPosition;
                yield return new SetTurnoutCommand(Address.From(Math.Abs((short)address)), locoNetPosition, motorState);
            }
        }

        public IEnumerable<SetTurnoutCommand> ToLocoNetLockCommands()
        {
            foreach (var address in command.LockAddresses)
            {
                if (command.IsUndefined) continue;
                yield return SetTurnoutCommand.Close(Address.From(Math.Abs((short)address)));
            }
        }

        public IEnumerable<SetTurnoutCommand> ToLocoNetUnlockCommands()
        {
            foreach (var address in command.LockAddresses)
            {
                if (command.IsUndefined) continue;
                yield return SetTurnoutCommand.Throw(Address.From(Math.Abs((short)address)));
            }
        }
    }

    extension(string? commandText)
    {
        public PointCommand ToPointCommand()
        {
            if (commandText is null || commandText.Length < 2) return PointCommand.Undefined;
            var number = commandText[0..^1].ToIntOrZero;
            return new PointCommand(number, commandText.PointPositionFromText);
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
