namespace Tellurian.Trains.YardController;

public interface IYardController
{
    Task SendPointCommandAsync(PointCommand command, CancellationToken cancellationToken);
}
