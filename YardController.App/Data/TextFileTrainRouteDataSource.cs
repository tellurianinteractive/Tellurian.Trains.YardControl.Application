using Tellurian.Trains.YardController;
using Tellurian.Trains.YardController.Extensions;

namespace Tellurian.Trains.YardController.Data;

public class TextFileTrainRouteDataSource(ILogger<ITrainRouteDataSource> logger, string filePath) : ITrainRouteDataSource
{
    private readonly ILogger _logger = logger;
    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    public async Task<IEnumerable<TrainRouteCommand>> GetTrainRouteCommandsAsync(CancellationToken cancellationToken)
    {
        var commands = new List<TrainRouteCommand>();
        if (!File.Exists(_filePath))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Train route commands file '{FilePath}' not found.", _filePath);
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
            var pointPositions = commandParts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pointPositions.Length < 1 || line.Contains('.'))
            {
                var trainRoute = commandParts[1].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (trainRoute.Length < 2) goto invalidCommand;
                List<PointCommand> pointCommands = [];
                int fromSignal = 0;
                int toSignal = 0;
                for (var i = 0; i < trainRoute.Length - 1; i++)
                {
                    var from = trainRoute[i].ToIntOrZero;
                    if (i == 0) fromSignal = from;
                    var to = trainRoute[i + 1].ToIntOrZero;
                    toSignal = to;
                    var command = commands.SingleOrDefault(c => c.FromSignal == from && c.ToSignal == to);
                    if (command is null) goto invalidCommand;
                    pointCommands.AddRange(command.PointCommands);
                }
                var trainRouteCommand = new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, pointCommands.Distinct());
                if (trainRouteCommand.IsUndefined) goto invalidCommand;
                commands.Add(trainRouteCommand);
            }
            else
            {
                var pointCommands = pointPositions.Select(pp => pp.ToPointCommand()).ToList();
                var trainRouteCommand = new TrainRouteCommand(signals[0].ToIntOrZero, signals[1].ToIntOrZero, TrainRouteState.Unset, pointCommands);
                if (trainRouteCommand.IsUndefined) goto invalidCommand;
                commands.Add(trainRouteCommand);
            }
            continue;

        invalidCommand:
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Invalid train route command line: '{CommandLine}'", line);
            }
        }
        return commands;
    }
}
