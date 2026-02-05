namespace Tellurian.Trains.YardController.Model.Control;

public interface ITrainRouteDataSource
{
    Task<IEnumerable<TrainRouteCommand>> GetTrainRouteCommandsAsync(CancellationToken cancellationToken);
}
