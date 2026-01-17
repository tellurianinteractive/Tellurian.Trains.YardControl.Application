namespace Tellurian.Trains.YardController;

public interface IYardController
{
    Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken);
    Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken);
    Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken);
}
