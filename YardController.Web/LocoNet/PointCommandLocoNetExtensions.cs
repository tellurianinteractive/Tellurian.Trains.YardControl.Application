using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet.Commands;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.LocoNet;

public static class PointCommandLocoNetExtensions
{
    extension(PointCommand command)
    {
        public IEnumerable<Command> ToLocoNetCommands(MotorState motorState = MotorState.On)
        {
            foreach (var address in command.Addresses)
            {
                if (command.IsUndefined) continue;
                var locoNetPosition = command.Position.WithAddressSignConsidered((short)address).LocoNetPosition;
                var accessoryAddress = address.ToAccessoryAddress;
                var messageKind = command.GetMessageKind(address);

                if (messageKind.HasFlag(AccessoryMessageKind.Command))
                    yield return new SetAccessoryCommand(accessoryAddress, locoNetPosition, motorState);

                if (messageKind.HasFlag(AccessoryMessageKind.Notification))
                    yield return new AccessoryOutputStatusCommand(accessoryAddress,
                        closedOutputOn: locoNetPosition == Position.ClosedOrGreen,
                        thrownOutputOn: locoNetPosition == Position.ThrownOrRed);
            }
        }

        public IEnumerable<SetAccessoryCommand> ToLocoNetLockCommands()
        {
            foreach (var address in command.LockAddresses)
            {
                if (command.IsUndefined) continue;
                yield return SetAccessoryCommand.Close(address.ToAccessoryAddress);
            }
        }

        public IEnumerable<SetAccessoryCommand> ToLocoNetUnlockCommands()
        {
            foreach (var address in command.LockAddresses)
            {
                if (command.IsUndefined) continue;
                yield return SetAccessoryCommand.Throw(address.ToAccessoryAddress);
            }
        }
    }

    extension(int address)
    {
        internal Address ToAccessoryAddress => Address.From((short)Math.Abs(address));
    }
}
