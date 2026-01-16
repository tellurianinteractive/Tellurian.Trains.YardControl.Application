using Tellurian.Trains.YardController;

namespace Tellurian.Trains.YardController.Data;

public class InMemoryTrainPathDataSource : ITrainPathDataSource
{
    private readonly List<TrainRouteCommand> _trainPathCommands = [];
    public void AddTrainPathCommand(TrainRouteCommand command) => _trainPathCommands.Add(command);
    public Task<IEnumerable<TrainRouteCommand>> GetTrainPathCommandsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_trainPathCommands.AsEnumerable());
}
