using Tellurian.Trains.YardController;
using Tellurian.Trains.YardController.Extensions;

namespace Tellurian.Trains.YardController.Data;

public class TextFilePointDataSource(ILogger<IPointDataSource> logger, string filePath) : IPointDataSource
{
    private readonly ILogger _logger = logger;
    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    public async Task<IEnumerable<Point>> GetPointsAsync(CancellationToken cancellationToken)
    {
        if (IsFileIsMissing()) return [];
        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
        if (IsFileEmpty(lines)) return [];
        var points = new List<Point>(lines.Length * 2);

        int lockAddressOffset = 0;
        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;
            if (line.IsWhiteSpace()) continue;
            if (line.Contains('\''))
            {
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Comment on line {LineNumber}: {Comment}", lineNumber, line);
                continue;
            }
            var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Invalid point data on line {LineNumber}: '{Line}'", lineNumber, line);
            }
            else if (lockAddressOffset == 0 && parts[0].Equals("LockOffset", StringComparison.OrdinalIgnoreCase))
            {
                lockAddressOffset = int.TryParse(parts[1], out var offset) ? offset : 0;
            }
            else if (parts[0].Equals("Adresses", StringComparison.OrdinalIgnoreCase))
            {
                var items = parts[1].Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var startAddress = items[0].ToIntOrZero;
                var endAddress = items[1].ToIntOrZero;
                if (startAddress == 0 || endAddress == 0)
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Invalid adress interval on line {LineNumber}: '{Line}'", lineNumber, line);
                    continue;
                }
                for (var address = startAddress; address <= endAddress; address++)
                {
                    var point = new Point(address, [address], lockAddressOffset);
                    if (point.IsUndefined) continue;
                    points.Add(point);
                }
            }
            else if (parts[0].Equals("Turntable", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            else
            {
                var number = parts[0].ToIntOrZero;
                var addresses = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(a => a.ToIntOrZero).ToArray();
                if (number != 0 && addresses.All(a => a != 0))
                    points.Add(new Point(number, addresses, lockAddressOffset));
            }

        }
        var adresses = points.SelectMany(p => p.Addresses).ToArray();
        if (adresses.IsAdressesAndLockAdressesOverlaping(lockAddressOffset))
        {
            var min = adresses.Min();
            var max = adresses.Max();
            if (_logger.IsEnabled(LogLevel.Error)) _logger.LogError("Point adresses and lock adresses {MinAddress}-{MaxAddress} overlaps lock adresses {MinLockAddress}-{MaxLockAddress}", min, max, min + lockAddressOffset, max + lockAddressOffset);
            return [];
        }
        return points;



    }
    public async Task<IEnumerable<TurntableTrack>> GetTurntableTracksAsync(CancellationToken cancellationToken)
    {
        if (IsFileIsMissing()) return [];
        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
        if (IsFileEmpty(lines)) return [];
        var turtableTracks = new List<TurntableTrack>(50);

        var lineNumber = 0;

        foreach (var line in lines)
        {
            lineNumber++;
            if (line.StartsWith("Turntable", StringComparison.OrdinalIgnoreCase))
            {
                var config = line.Split([':', '-', ';']);
                if (config.Length != 4)
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Invalid turntable configuration on line {LineNumber}: '{Line}'", lineNumber, line);
                    continue;
                }
                var startNumber = config[1].ToIntOrZero;
                var endNumber = config[2].ToIntOrZero;
                var addressOffset = config[3].ToIntOrZero;
                if (startNumber == 0 || endNumber == 0)
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Invalid turntable track numbers on line {LineNumber}: '{Line}'", lineNumber, line);
                    continue;
                }
                for (int number = startNumber; number <= endNumber; number++)
                {
                    var track = new TurntableTrack(number, number + addressOffset);
                    turtableTracks.Add(track);
                }
            }
        }
        return turtableTracks;
    }

    private bool IsFileIsMissing()
    {
        if (!File.Exists(_filePath))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Point data file '{FilePath}' not found.", _filePath);
                return true;
            }
        }
        return false;
    }

    private bool IsFileEmpty(string[] lines)
    {
        if (lines.Length == 0)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Point data file '{FilePath}' is empty.", _filePath);
                return true;
            }
        }
        return false;
    }


}
