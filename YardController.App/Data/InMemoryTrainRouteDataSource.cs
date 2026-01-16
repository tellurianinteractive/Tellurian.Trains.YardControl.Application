using Tellurian.Trains.YardController;

namespace Tellurian.Trains.YardController.Data;

public class InMemoryTrainRouteDataSource : ITrainRouteDataSource
{
    private readonly List<TrainRouteCommand> _trainRouteCommands = [];
    public void AddTrainRouteCommand(TrainRouteCommand command) => _trainRouteCommands.Add(command);
    public Task<IEnumerable<TrainRouteCommand>> GetTrainRouteCommandsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_trainRouteCommands.AsEnumerable());
}
