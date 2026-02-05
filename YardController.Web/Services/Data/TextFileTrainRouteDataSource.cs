using Microsoft.Extensions.Options;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services.Data;

public class TextFileTrainRouteDataSource(ILogger<ITrainRouteDataSource> logger, IOptions<TrainRouteDataSourceSettings> settings) : ITrainRouteDataSource
{
    private readonly ILogger _logger = logger;
    private readonly string _filePath = settings.Value.Path ?? throw new ArgumentNullException(nameof(settings));

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
        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;
            if (line.IsWhiteSpace()) continue;
            if (line.IsWhiteSpace() || line.Contains('\''))
            {
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Comment on {LineNumber}: {Comment} ", lineNumber, line);
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var commandParts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (commandParts.Length != 2) goto invalidCommand;
            var signals = commandParts[0].Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (signals.Length < 2) goto invalidCommand;
            var pointPositions = commandParts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pointPositions.Length < 1 || line.Contains('.'))
            {
                var routeStartAndEndSignalNumbers = commandParts[0].Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (routeStartAndEndSignalNumbers.Length != 2) goto invalidCommand;
                var trainRoute = commandParts[1].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (trainRoute.Length < 2) goto invalidCommand;
                List<PointCommand> pointCommands = [];
                int fromSignal = routeStartAndEndSignalNumbers[0].ToIntOrZero;
                int toSignal = routeStartAndEndSignalNumbers[1].ToIntOrZero;
                for (var i = 0; i < trainRoute.Length - 1; i++)
                {
                    var from = trainRoute[i].ToIntOrZero;
                    var to = trainRoute[i + 1].ToIntOrZero;
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
                _logger.LogWarning("Invalid train route command line on {LineNumber}: '{CommandLine}'", lineNumber, line);
            }
        }
        return commands;
    }
}
