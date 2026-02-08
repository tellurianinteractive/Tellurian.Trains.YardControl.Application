namespace Tellurian.Trains.YardController.Model.Control;

public interface IYardController
{
    Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken);
    Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken);
    Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken);
    Task SendSwitchStateRequestAsync(int address, CancellationToken cancellationToken);
}
