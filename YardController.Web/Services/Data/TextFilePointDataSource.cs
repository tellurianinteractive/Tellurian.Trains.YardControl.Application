using Microsoft.Extensions.Options;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services.Data;

public class TextFilePointDataSource(ILogger<IPointDataSource> logger, IOptions<PointDataSourceSettings> settings) : IPointDataSource
{
    private readonly ILogger _logger = logger;
    private readonly string _filePath = settings.Value.Path ?? throw new ArgumentNullException(nameof(settings));
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
                    var point = new Point(address, [address], [address], lockAddressOffset, IsAddressOnly: true);
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
                var addressPart = parts[1];

                // Check for grouped format: (addresses)- and/or (addresses)+
                if (addressPart.Contains('('))
                {
                    var (straightAddresses, straightSubPoints) = ParseGroupedAddressesWithSubPoints(addressPart, '+');
                    var (divergingAddresses, divergingSubPoints) = ParseGroupedAddressesWithSubPoints(addressPart, '-');
                    if (number != 0 && (straightAddresses.Length > 0 || divergingAddresses.Length > 0))
                    {
                        var subPointMap = BuildSubPointMap(straightSubPoints.Concat(divergingSubPoints));
                        points.Add(new Point(number, straightAddresses, divergingAddresses, lockAddressOffset, subPointMap));
                    }
                }
                else
                {
                    // Backward compatible format: same addresses for both positions
                    var parsed = addressPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(a => a.ToAddressWithSubPoint()).ToArray();
                    var pointAddresses = parsed.Select(p => p.Address).ToArray();
                    if (number != 0 && pointAddresses.All(a => a != 0))
                    {
                        var subPointMap = BuildSubPointMap(parsed);
                        points.Add(new Point(number, pointAddresses, pointAddresses, lockAddressOffset, subPointMap));
                    }
                }
            }

        }
        var addresses = points.SelectMany(p => p.StraightAddresses.Concat(p.DivergingAddresses)).Distinct().ToArray();
        if (addresses.IsAdressesAndLockAdressesOverlaping(lockAddressOffset))
        {
            var min = addresses.Min();
            var max = addresses.Max();
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

    /// <summary>
    /// Parses grouped addresses from a format like "(816a,823b)-(823b)+"
    /// Extracts addresses and sub-point mappings for the specified position suffix ('+' for straight, '-' for diverging)
    /// </summary>
    private static (int[] Addresses, IEnumerable<(int Address, char? SubPoint)> SubPoints) ParseGroupedAddressesWithSubPoints(string addressPart, char positionSuffix)
    {
        var addresses = new List<int>();
        var subPoints = new List<(int Address, char? SubPoint)>();
        var searchSuffix = ")" + positionSuffix;
        var suffixIndex = addressPart.IndexOf(searchSuffix);

        while (suffixIndex >= 0)
        {
            var openIndex = addressPart.LastIndexOf('(', suffixIndex);
            if (openIndex >= 0 && openIndex < suffixIndex)
            {
                var addressesStr = addressPart.Substring(openIndex + 1, suffixIndex - openIndex - 1);
                var parsed = addressesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(a => a.ToAddressWithSubPoint())
                    .Where(p => p.Address != 0);
                foreach (var p in parsed)
                {
                    addresses.Add(p.Address);
                    subPoints.Add(p);
                }
            }

            suffixIndex = addressPart.IndexOf(searchSuffix, suffixIndex + 2);
        }

        return ([.. addresses], subPoints);
    }

    private static IReadOnlyDictionary<int, char>? BuildSubPointMap(
        IEnumerable<(int Address, char? SubPoint)> parsed)
    {
        var map = new Dictionary<int, char>();
        foreach (var (address, subPoint) in parsed)
            if (subPoint.HasValue) map[Math.Abs(address)] = subPoint.Value;
        return map.Count > 0 ? map : null;
    }
}
