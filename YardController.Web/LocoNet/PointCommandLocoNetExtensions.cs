using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet.Commands;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.LocoNet;

public static class PointCommandLocoNetExtensions
{
    extension(PointCommand command)
    {
        public IEnumerable<SetTurnoutCommand> ToLocoNetCommands(MotorState motorState = MotorState.On)
        {
            foreach (var address in command.Addresses)
            {
                if (command.IsUndefined) continue;
                var locoNetPosition = command.Position.WithAddressSignConsidered((short)address).LocoNetPosition;
                yield return new SetTurnoutCommand(address.ToAccessoryAddress, locoNetPosition, motorState);
            }
        }

        public IEnumerable<SetTurnoutCommand> ToLocoNetLockCommands()
        {
            foreach (var address in command.LockAddresses)
            {
                if (command.IsUndefined) continue;
                yield return SetTurnoutCommand.Close(address.ToAccessoryAddress);
            }
        }

        public IEnumerable<SetTurnoutCommand> ToLocoNetUnlockCommands()
        {
            foreach (var address in command.LockAddresses)
            {
                if (command.IsUndefined) continue;
                yield return SetTurnoutCommand.Throw(address.ToAccessoryAddress);
            }
        }
    }

    extension(int address)
    {
        internal Address ToAccessoryAddress => Address.From((short)Math.Abs(address));
    }
}
