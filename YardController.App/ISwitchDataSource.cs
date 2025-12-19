namespace Tellurian.Trains.YardController;

public interface ISwitchDataSource
{
    Task<IEnumerable<Switch>> GetSwitchesAsync(CancellationToken cancellationToken);
}

public class TextFileSwitchDataSource(ILogger<ISwitchDataSource> logger, string filePath) : ISwitchDataSource
{
    private readonly ILogger _logger = logger;
    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    public async Task<IEnumerable<Switch>> GetSwitchesAsync(CancellationToken cancellationToken)
    {
        var switches = new List<Switch>();
        if (!File.Exists(_filePath)) { 
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Switch data file '{FilePath}' not found.", _filePath);
            }
            return switches; 
        }
        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
        foreach (var line in lines)
        {
            var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;
            var number = parts[0].ToIntOrZero;
            var addresses = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(a => a.ToIntOrZero).ToArray();
            if (number != 0 && addresses.All(a => a > 0))
                switches.Add(new Switch(number, addresses));
            else 
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("Invalid switch data in line: '{Line}'", line);
                }
            }
        }
        return switches;
    }
}

public class InMemorySwitchDataSource() : ISwitchDataSource
{
    private readonly List<Switch> _switches = new(100);
    public void AddSwitch(Switch sw)
    {
        if (_switches.Any(existing => sw.Number == existing.Number)) throw new InvalidOperationException($"Switch number {sw.Number} already exists.");
        _switches.Add(sw);
    }

    public void AddSwitch(int number, int[] addresses) => AddSwitch(new Switch(number, addresses));

    public Task<IEnumerable<Switch>> GetSwitchesAsync(CancellationToken cancellationToken) => Task.FromResult(_switches.AsEnumerable());
} 
