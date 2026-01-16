namespace Tellurian.Trains.YardController;

public interface ITrainPathDataSource
{
    Task<IEnumerable<TrainRouteCommand>> GetTrainPathCommandsAsync(CancellationToken cancellationToken);
}
