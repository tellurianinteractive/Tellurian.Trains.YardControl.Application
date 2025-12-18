namespace Tellurian.Trains.YardController;

public interface ITrainPathDataSource
{
    Task<IEnumerable<TrainPathCommand>> GetTrainPathCommandsAsync(CancellationToken cancellationToken);
}

public class TextFileTrainPathDataSource(ILogger<ITrainPathDataSource> logger, string filePath) : ITrainPathDataSource
{
    private readonly ILogger _logger = logger;
    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    public async Task<IEnumerable<TrainPathCommand>> GetTrainPathCommandsAsync(CancellationToken cancellationToken)
    {
        var commands = new List<TrainPathCommand>();
        if (!File.Exists(_filePath))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Train path commands file '{filePath}' not found.", _filePath);
            }
            return commands;
        }
        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var commandParts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (commandParts.Length != 2) goto invalidCommand;
            var signals = commandParts[0].Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (signals.Length < 2) goto invalidCommand;
            var switchDirections = commandParts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (switchDirections.Length < 1) goto invalidCommand;
            var trainPathCommand = new TrainPathCommand(signals[0].ToIntOrZero, signals[1].ToIntOrZero, TrainPathState.Set, switchDirections.Select(sd => sd.ToSwitchCommand()));
            if (trainPathCommand.IsUndefined) goto invalidCommand;
            commands.Add(trainPathCommand);
            continue;

        invalidCommand:
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Invalid train path command line: '{line}'", line);
            }
        }
        return commands;
    }
}

public class InMemoryTrainPathDataSource : ITrainPathDataSource
{
    private readonly List<TrainPathCommand> _trainPathCommands = [];
    public void AddTrainPathCommand(TrainPathCommand command) => _trainPathCommands.Add(command);
    public Task<IEnumerable<TrainPathCommand>> GetTrainPathCommandsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_trainPathCommands.AsEnumerable());
}
