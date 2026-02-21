using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// Converts from the legacy multi-file station format to the unified Station.txt format.
/// Reads Topology.txt, Points.txt, Signals.txt, TrainRoutes.txt, and optional LabelTranslations.csv.
/// </summary>
public class StationFileConverter
{
    /// <summary>
    /// Converts legacy multi-file station data to a unified Station.txt string.
    /// </summary>
    public static async Task<string> ConvertToUnifiedAsync(
        string topologyPath,
        string pointsPath,
        string trainRoutesPath,
        string? signalsPath = null,
        string? labelTranslationsPath = null)
    {
        var parser = new TopologyParser();
        var topology = await parser.ParseFileAsync(topologyPath);

        var (points, turntableTracks, lockAddressOffset) = await LoadPointsAsync(pointsPath);
        var (trainRoutes, lockReleaseDelaySeconds) = await LoadTrainRoutesAsync(trainRoutesPath);
        var signalAddresses = signalsPath is not null ? await LoadSignalAddressesAsync(signalsPath) : [];
        var translations = labelTranslationsPath is not null ? await LoadTranslationsAsync(labelTranslationsPath) : null;

        var data = new UnifiedStationData(
            topology.Name,
            topology,
            points,
            turntableTracks,
            trainRoutes,
            signalAddresses,
            translations,
            lockAddressOffset,
            lockReleaseDelaySeconds);

        var writer = new UnifiedStationWriter();
        return writer.Write(data);
    }

    private static async Task<(IReadOnlyList<Point>, IReadOnlyList<TurntableTrack>, int)> LoadPointsAsync(string path)
    {
        if (!File.Exists(path)) return ([], [], 0);

        var lines = await File.ReadAllLinesAsync(path);
        var points = new List<Point>();
        var turntableTracks = new List<TurntableTrack>();
        int lockAddressOffset = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('\''))
                continue;

            var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            if (parts[0].Equals("LockOffset", StringComparison.OrdinalIgnoreCase))
            {
                lockAddressOffset = int.TryParse(parts[1], out var offset) ? offset : 0;
            }
            else if (parts[0].Equals("Turntable", StringComparison.OrdinalIgnoreCase))
            {
                var configParts = line.Split([':', '-', ';']);
                if (configParts.Length == 4 &&
                    int.TryParse(configParts[1], out var startNumber) &&
                    int.TryParse(configParts[2], out var endNumber) &&
                    int.TryParse(configParts[3], out var addressOffset))
                {
                    for (int number = startNumber; number <= endNumber; number++)
                        turntableTracks.Add(new TurntableTrack(number, number + addressOffset));
                }
            }
            else if (int.TryParse(parts[0], out var number))
            {
                var addressPart = parts[1];
                if (addressPart.Contains('('))
                {
                    var (straightAddresses, straightSubPoints) = ParseGroupedAddressesWithSubPoints(addressPart, '+');
                    var (divergingAddresses, divergingSubPoints) = ParseGroupedAddressesWithSubPoints(addressPart, '-');
                    if (straightAddresses.Length > 0 || divergingAddresses.Length > 0)
                    {
                        var subPointMap = BuildSubPointMap(straightSubPoints.Concat(divergingSubPoints));
                        points.Add(new Point(number, straightAddresses, divergingAddresses, lockAddressOffset, subPointMap));
                    }
                }
                else
                {
                    var parsed = addressPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(a => a.Trim().ToAddressWithSubPoint())
                        .Where(p => p.Address != 0)
                        .ToArray();
                    var addresses = parsed.Select(p => p.Address).ToArray();
                    if (addresses.Length > 0)
                    {
                        var subPointMap = BuildSubPointMap(parsed);
                        points.Add(new Point(number, addresses, addresses, lockAddressOffset, subPointMap));
                    }
                }
            }
        }

        return (points, turntableTracks, lockAddressOffset);
    }

    private static async Task<(IReadOnlyList<TrainRouteCommand>, int)> LoadTrainRoutesAsync(string path)
    {
        if (!File.Exists(path)) return ([], 0);

        var lines = await File.ReadAllLinesAsync(path);
        var commands = new List<TrainRouteCommand>();
        int lockReleaseDelaySeconds = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('\''))
                continue;

            var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;

            if (parts[0].Equals("LockReleaseDelay", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(parts[1], out var delay) && delay >= 0)
                    lockReleaseDelaySeconds = delay;
                continue;
            }

            var signals = parts[0].Split('-', StringSplitOptions.TrimEntries);
            if (signals.Length != 2) continue;
            if (!int.TryParse(signals[0], out var fromSignal) || !int.TryParse(signals[1], out var toSignal))
                continue;

            if (parts[1].Contains('.'))
            {
                var route = parts[1].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (route.Length >= 2)
                {
                    var pointCommands = new List<PointCommand>();
                    for (var i = 0; i < route.Length - 1; i++)
                    {
                        if (int.TryParse(route[i], out var from) && int.TryParse(route[i + 1], out var to))
                        {
                            var baseRoute = commands.FirstOrDefault(c => c.FromSignal == from && c.ToSignal == to);
                            if (baseRoute != null)
                                pointCommands.AddRange(baseRoute.PointCommands);
                        }
                    }
                    if (pointCommands.Count > 0)
                    {
                        var intermediateSignals = route.Skip(1).Take(route.Length - 2)
                            .Where(s => int.TryParse(s, out _))
                            .Select(int.Parse)
                            .ToArray();
                        commands.Add(new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, pointCommands.Distinct())
                        {
                            IntermediateSignals = intermediateSignals
                        });
                    }
                }
            }
            else
            {
                var pointPositions = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var pointCommands = pointPositions.Select(pp => pp.ToPointCommand()).ToList();
                commands.Add(new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, pointCommands));
            }
        }

        return (commands, lockReleaseDelaySeconds);
    }

    private static async Task<IReadOnlyList<SignalHardware>> LoadSignalAddressesAsync(string path)
    {
        if (!File.Exists(path)) return [];

        var lines = await File.ReadAllLinesAsync(path);
        var result = new List<SignalHardware>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('\''))
                continue;

            var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;

            var signalName = parts[0];
            var addressPart = parts[1];

            int address;
            int? feedbackAddress = null;

            if (addressPart.Contains(';'))
            {
                var addressParts = addressPart.Split(';', StringSplitOptions.TrimEntries);
                if (!int.TryParse(addressParts[0], out address)) continue;
                if (addressParts.Length > 1 && int.TryParse(addressParts[1], out var fb))
                    feedbackAddress = fb;
            }
            else
            {
                if (!int.TryParse(addressPart, out address)) continue;
            }

            result.Add(new SignalHardware(signalName, address, feedbackAddress));
        }

        return result;
    }

    private static async Task<LabelTranslationData?> LoadTranslationsAsync(string path)
    {
        if (!File.Exists(path)) return null;

        var content = await File.ReadAllTextAsync(path);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;

        var languages = lines[0].Trim().Split(';').Select(s => s.Trim()).ToArray();
        var rows = new List<string[]>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            rows.Add(line.Split(';').Select(s => s.Trim()).ToArray());
        }

        return new LabelTranslationData(languages, rows);
    }

    // Reused parsing helpers (same as YardDataService)
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
