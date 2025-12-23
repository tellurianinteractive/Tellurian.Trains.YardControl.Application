namespace Tellurian.Trains.YardController;

public interface ITrainPathDataSource
{
    Task<IEnumerable<TrainRouteCommand>> GetTrainPathCommandsAsync(CancellationToken cancellationToken);
}

public class TextFileTrainPathDataSource(ILogger<ITrainPathDataSource> logger, string filePath) : ITrainPathDataSource
{
    private readonly ILogger _logger = logger;
    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    public async Task<IEnumerable<TrainRouteCommand>> GetTrainPathCommandsAsync(CancellationToken cancellationToken)
    {
        var commands = new List<TrainRouteCommand>();
        if (!File.Exists(_filePath))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Train path commands file '{FilePath}' not found.", _filePath);
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
            if (switchDirections.Length < 1 || line.Contains('.'))
            {
                var trainPath = commandParts[1].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (trainPath.Length < 2) goto invalidCommand;
                List<SwitchCommand> switchCommands = [];
                int fromSignal = 0;
                int toSignal = 0;
                for (var i = 0; i < trainPath.Length - 1; i++)
                {
                    var from = trainPath[i].ToIntOrZero;
                    if (i == 0) fromSignal = from;
                    var to = trainPath[i + 1].ToIntOrZero;
                    toSignal = to;
                    var command = commands.SingleOrDefault(c => c.FromSignal == from && c.ToSignal == to);
                    if (command is null) goto invalidCommand;
                    switchCommands.AddRange(command.SwitchCommands);
                }
                var trainPathCommand = new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, switchCommands.Distinct());
                if (trainPathCommand.IsUndefined) goto invalidCommand;
                commands.Add(trainPathCommand);
            }
            else
            {
                var switchCommands = switchDirections.Select(sd => sd.ToSwitchCommand()).ToList();
                var trainPathCommand = new TrainRouteCommand(signals[0].ToIntOrZero, signals[1].ToIntOrZero, TrainRouteState.Unset, switchCommands);
                if (trainPathCommand.IsUndefined) goto invalidCommand;
                commands.Add(trainPathCommand);
            }
            continue;

        invalidCommand:
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Invalid train path command line: '{CommandLine}'", line);
            }
        }
        return commands;
    }
}

public class InMemoryTrainPathDataSource : ITrainPathDataSource
{
    private readonly List<TrainRouteCommand> _trainPathCommands = [];
    public void AddTrainPathCommand(TrainRouteCommand command) => _trainPathCommands.Add(command);
    public Task<IEnumerable<TrainRouteCommand>> GetTrainPathCommandsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_trainPathCommands.AsEnumerable());
}
