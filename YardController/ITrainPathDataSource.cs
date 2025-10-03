namespace Tellurian.Trains.YardController;

public interface ITrainPathDataSource
{
    Task<IEnumerable<TrainPathCommand>> GetTrainPathCommandsAsync(Dictionary<int,int> switchAdresses, CancellationToken cancellationToken);
    Task<Dictionary<int, int>> GetSwitchAddressesAsync(CancellationToken cancellationToken);
}

public class TextFileTrainPathDataSource(string directoryPath) : ITrainPathDataSource
{
    private readonly string _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));

    public async Task<Dictionary<int, int>> GetSwitchAddressesAsync(CancellationToken cancellationToken)
    {
        var signals = new Dictionary<int, int>();
        var path = Path.Combine(_directoryPath, "SwitchAdresses.txt");
        if (!File.Exists(path)) return signals;
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        foreach (var line in lines)
        {
            var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            var number = parts[0].ToIntOrZero;
            var address = parts[1].ToIntOrZero;
            if (number != 0 && address != 0)
                signals.Add(number, address);
        }
        return signals;
    }

    public async Task<IEnumerable<TrainPathCommand>> GetTrainPathCommandsAsync(Dictionary<int, int> switchAdresses, CancellationToken cancellationToken)
    {
        var commands = new List<TrainPathCommand>();
        var path = Path.Combine(_directoryPath, "TrainPaths.txt");
        if (!File.Exists(path)) return commands;
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        foreach (var line in lines)
        {
            var commandParts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (commandParts.Length != 2) continue;
            var signals = commandParts[0].Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (signals.Length < 2) continue;
            var switches = commandParts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (switches.Length < 1) continue;
            var trainPathCommand = new TrainPathCommand(signals[0].ToIntOrZero, signals[1].ToIntOrZero, TrainPathState.Set, [.. switches.Select(sw => sw.ToSwitchCommand(switchAdresses))]);
            commands.Add(trainPathCommand);
        }
        return commands;
    }
}
