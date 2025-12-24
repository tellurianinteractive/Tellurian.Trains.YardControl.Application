using Tellurian.Trains.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet.Commands;
using Tellurian.Trains.YardController.Extensions;

namespace Tellurian.Trains.YardController;

public static class SwitchCommandExtensions
{
    extension(SwitchCommand command)
    {
        public static SwitchCommand Undefined => new(0, SwitchDirection.Undefined);

        public bool IsUndefined => command.Direction == SwitchDirection.Undefined;

        internal SwitchLock ToSwitchLock => new(command, false);

        public static SwitchCommand Create(int number, SwitchDirection direction, int[] addresses)
        {
            var cmd = new SwitchCommand(number, direction);
            cmd.AddAddresses(addresses);
            return cmd;
        }

        public static bool Equals(SwitchCommand one, SwitchCommand another) =>
            one.Number == another.Number &&
            one.Direction == another.Direction &&
            one.Addresses.SequenceEqual(another.Addresses);

        public IEnumerable<SetTurnoutCommand> ToTurnoutCommands()
        {
            var position = command.Direction == SwitchDirection.Straight ? Position.ClosedOrGreen : Position.ThrownOrRed;
            foreach (var address in command.Addresses)
            {
                yield return new SetTurnoutCommand(Address.From((short)address), position, MotorState.On);
            }
        }
    }

    extension(string? commandText)
    {
        public SwitchCommand ToSwitchCommand()
        {
            if (commandText is null || commandText.Length < 2) return SwitchCommand.Undefined;
            var number = commandText[0..^1].ToIntOrZero;
            return new SwitchCommand(number, commandText.SwitchState);
        }

        private SwitchDirection SwitchState
        {
            get
            {
                if (commandText is null or { Length: < 2 }) return SwitchDirection.Undefined;
                return commandText[^1].SwitchDirection;
            }
        }
    }
}
