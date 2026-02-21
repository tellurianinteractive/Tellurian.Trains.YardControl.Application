namespace Tellurian.Trains.YardController.Model.Control;

public interface IYardController
{
    Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken);
    Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken);
    Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken);
    Task SendPointStateRequestAsync(int address, CancellationToken cancellationToken);
    Task SendSignalCommandAsync(SignalCommand command, CancellationToken cancellationToken);
    Task SendRouteCommandAsync(TrainRouteCommand command, CancellationToken cancellationToken);
}
