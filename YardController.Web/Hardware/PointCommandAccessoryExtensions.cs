using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Hardware;

public static class PointCommandAccessoryExtensions
{
    extension(PointCommand command)
    {
        /// <summary>
        /// Maps a <see cref="PointCommand"/> into protocol-agnostic (address, accessory-command) pairs.
        /// LocoNet-specific "notification only" addresses (parsed from the <c>n</c> suffix) are emitted
        /// with <see cref="MotorState.Off"/> so adapters that honour that distinction can treat them as
        /// output-status reports rather than motor drives.
        /// </summary>
        public IEnumerable<(Address Address, AccessoryCommand Command)> ToAccessoryCommands()
        {
            foreach (var address in command.Addresses)
            {
                if (command.IsUndefined) continue;
                var position = command.Position.WithAddressSignConsidered((short)address).AccessoryPosition;
                var accessoryAddress = address.ToAccessoryAddress;
                var messageKind = command.GetMessageKind(address);
                var motorState = messageKind.HasFlag(AccessoryMessageKind.Command)
                    ? MotorState.On
                    : MotorState.Off;
                yield return (accessoryAddress, AccessoryCommand.Set(position, motorState));
            }
        }

        public IEnumerable<(Address Address, AccessoryCommand Command)> ToLockAccessoryCommands()
        {
            foreach (var address in command.LockAddresses)
            {
                if (command.IsUndefined) continue;
                yield return (address.ToAccessoryAddress, AccessoryCommand.Close());
            }
        }

        public IEnumerable<(Address Address, AccessoryCommand Command)> ToUnlockAccessoryCommands()
        {
            foreach (var address in command.LockAddresses)
            {
                if (command.IsUndefined) continue;
                yield return (address.ToAccessoryAddress, AccessoryCommand.Throw());
            }
        }
    }

    extension(int address)
    {
        internal Address ToAccessoryAddress => Address.From((short)Math.Abs(address));
    }
}
