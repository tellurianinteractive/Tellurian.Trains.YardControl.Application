namespace Tellurian.Trains.YardController;

public interface ITrainRouteDataSource
{
    Task<IEnumerable<TrainRouteCommand>> GetTrainRouteCommandsAsync(CancellationToken cancellationToken);
}
