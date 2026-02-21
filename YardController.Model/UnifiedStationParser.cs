using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// Parses a unified Station.txt file containing all station configuration:
/// tracks, points (with hardware addresses), signals (with addresses),
/// routes, labels, gaps, turntable, translations, and settings.
/// </summary>
public partial class UnifiedStationParser
{
    private readonly ILogger? _logger;

    public UnifiedStationParser(ILogger? logger = null)
    {
        _logger = logger;
    }

    // Reuse regex patterns from TopologyParser
    [GeneratedRegex(@"(\d+\.\d+)(!)?")]
    private static partial Regex CoordinateWithMarkerPattern();

    // Point with optional @addresses: topology_part  @address_part
    // Single point pattern: coord(label>)-coord or coord(<label)-coord, with optional + suffix
    [GeneratedRegex(@"(\d+\.\d+)\(([<>]?)([A-Za-z0-9]+)([<>]?)\)-(\d+\.\d+)(\+)?(?!\()")]
    private static partial Regex SinglePointPattern();

    // Paired points pattern: coord(label>)-coord(<label)
    [GeneratedRegex(@"(\d+\.\d+)\(([<>]?)([A-Za-z0-9]+)([<>]?)\)-(\d+\.\d+)\(([<>]?)([A-Za-z0-9]+)([<>]?)\)(\+)?")]
    private static partial Regex PairedPointPattern();

    // Label pattern: coord[text]coord
    [GeneratedRegex(@"(\d+\.\d+)\[([^\]]+)\](\d+\.\d+)")]
    private static partial Regex LabelPattern();

    // Signal pattern: coord:name[label]>: with optional type suffix and @address
    [GeneratedRegex(@"(\d+\.\d+):([<]?)([A-Za-z]?\d+)(?:\[([^\]]+)\])?([>]?):([a-z])?")]
    private static partial Regex SignalPattern();

    // Gap patterns: coord| or coord|coord
    [GeneratedRegex(@"(\d+\.\d+)\|(\d+\.\d+)?")]
    private static partial Regex GapPattern();

    // Section header pattern
    [GeneratedRegex(@"^\[(\w+)\]$")]
    private static partial Regex SectionPattern();

    public UnifiedStationData Parse(string content)
    {
        var graph = new TrackGraph();
        var pointDefinitions = new List<PointDefinition>();
        var signalDefinitions = new List<SignalDefinition>();
        var labels = new List<LabelDefinition>();
        var gaps = new List<GapDefinition>();
        var forcedNecessary = new HashSet<GridCoordinate>();

        var points = new List<Point>();
        var turntableTracks = new List<TurntableTrack>();
        var signalAddresses = new List<SignalHardware>();
        var trainRoutes = new List<TrainRouteCommand>();
        LabelTranslationData? translations = null;

        int lockAddressOffset = 0;
        int lockReleaseDelaySeconds = 0;

        var lines = content.Split('\n');
        var currentSection = "";
        var name = "";
        var isFirstLine = true;

        // Collect translation lines for deferred parsing
        var translationLines = new List<string>();

        // Turntable state (needs both Tracks and Offset before creating)
        int turntableStartTrack = 0, turntableEndTrack = 0, turntableOffset = 0;
        bool hasTurntableTracks = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith('\'')) continue;

            // First non-comment line is the station name
            if (isFirstLine)
            {
                name = line;
                isFirstLine = false;
                continue;
            }

            // Check for section header
            var sectionMatch = SectionPattern().Match(line);
            if (sectionMatch.Success)
            {
                // If leaving translations section, parse collected lines
                if (currentSection == "translations" && translationLines.Count > 0)
                {
                    translations = ParseTranslationLines(translationLines);
                }

                currentSection = sectionMatch.Groups[1].Value.ToLowerInvariant();
                continue;
            }

            switch (currentSection)
            {
                case "tracks":
                    ParseTrackLine(line, graph, forcedNecessary);
                    break;

                case "points":
                    ParsePointLine(line, graph, pointDefinitions, points, ref lockAddressOffset);
                    break;

                case "signals":
                    ParseSignalLine(line, signalDefinitions, signalAddresses);
                    break;

                case "labels":
                    ParseLabelLine(line, labels);
                    break;

                case "gaps":
                    ParseGapLine(line, graph, gaps);
                    break;

                case "settings":
                    ParseSettingsLine(line, ref lockAddressOffset, ref lockReleaseDelaySeconds);
                    break;

                case "translations":
                    translationLines.Add(line);
                    break;

                case "turntable":
                    ParseTurntableLine(line, ref turntableStartTrack, ref turntableEndTrack, ref turntableOffset, ref hasTurntableTracks);
                    break;


                case "routes":
                    ParseRouteLine(line, trainRoutes, graph, pointDefinitions, signalDefinitions);
                    break;
            }
        }

        // Handle translations if we're still in that section at EOF
        if (currentSection == "translations" && translationLines.Count > 0 && translations is null)
        {
            translations = ParseTranslationLines(translationLines);
        }

        // Create turntable tracks if both Tracks and Offset were specified
        if (hasTurntableTracks && turntableOffset > 0)
        {
            for (int number = turntableStartTrack; number <= turntableEndTrack; number++)
                turntableTracks.Add(new TurntableTrack(number, number + turntableOffset));
        }

        var topology = new YardTopology(name, graph, pointDefinitions, signalDefinitions, labels, gaps, forcedNecessary);

        return new UnifiedStationData(
            name,
            topology,
            points,
            turntableTracks,
            trainRoutes,
            signalAddresses,
            translations,
            lockAddressOffset,
            lockReleaseDelaySeconds);
    }

    public async Task<UnifiedStationData> ParseFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Station file not found", filePath);

        var content = await File.ReadAllTextAsync(filePath);
        return Parse(content);
    }

    #region Track Parsing

    private void ParseTrackLine(string line, TrackGraph graph, HashSet<GridCoordinate> forcedNecessary)
    {
        var coordMatches = CoordinateWithMarkerPattern().Matches(line);
        GridCoordinate? previousCoord = null;

        foreach (Match match in coordMatches)
        {
            var coord = GridCoordinate.Parse(match.Groups[1].Value);
            var hasForcedMarker = match.Groups[2].Success && match.Groups[2].Value == "!";

            if (hasForcedMarker)
            {
                forcedNecessary.Add(coord);
            }

            if (previousCoord.HasValue)
            {
                if (previousCoord.Value.Column > coord.Column)
                {
                    _logger?.LogWarning(
                        "Track link {From}-{To} has decreasing column values.",
                        previousCoord.Value, coord);
                }

                // Auto-fill: if same row, generate all intermediate coordinates
                if (previousCoord.Value.Row == coord.Row)
                {
                    var fromCol = previousCoord.Value.Column;
                    var toCol = coord.Column;
                    var step = fromCol < toCol ? 1 : -1;
                    var row = previousCoord.Value.Row;

                    for (var col = fromCol; col != toCol; col += step)
                    {
                        var nextCol = col + step;
                        graph.TryAddLink(
                            new GridCoordinate(row, col),
                            new GridCoordinate(row, nextCol));
                    }
                }
                else
                {
                    // Different rows = diagonal/bend, single link
                    graph.TryAddLink(previousCoord.Value, coord);
                }
            }

            previousCoord = coord;
        }
    }

    #endregion

    #region Point Parsing

    private void ParsePointLine(
        string line,
        TrackGraph graph,
        List<PointDefinition> pointDefinitions,
        List<Point> points,
        ref int lockAddressOffset)
    {
        // Split topology part from @address part
        var atIndex = line.IndexOf('@');
        var topologyPart = atIndex >= 0 ? line[..atIndex].Trim() : line;
        var addressPart = atIndex >= 0 ? line[(atIndex + 1)..].Trim() : null;

        // Check for LockOffset setting (legacy support in points section)
        var parts = topologyPart.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && parts[0].Equals("LockOffset", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(parts[1], out var offset))
                lockAddressOffset = offset;
            return;
        }

        // Try paired points first
        var pairedMatch = PairedPointPattern().Match(topologyPart);
        if (pairedMatch.Success)
        {
            var coord1 = GridCoordinate.Parse(pairedMatch.Groups[1].Value);
            var dirBefore1 = pairedMatch.Groups[2].Value;
            var label1 = pairedMatch.Groups[3].Value;
            var dirAfter1 = pairedMatch.Groups[4].Value;
            var coord2 = GridCoordinate.Parse(pairedMatch.Groups[5].Value);
            var dirBefore2 = pairedMatch.Groups[6].Value;
            var label2 = pairedMatch.Groups[7].Value;
            var dirAfter2 = pairedMatch.Groups[8].Value;
            var explicitEndIsStraight = pairedMatch.Groups[9].Success;

            var direction1 = !string.IsNullOrEmpty(dirBefore1) ? dirBefore1 : dirAfter1;
            var direction2 = !string.IsNullOrEmpty(dirBefore2) ? dirBefore2 : dirAfter2;

            var dir1 = direction1 == ">" ? DivergeDirection.Forward : DivergeDirection.Backward;
            pointDefinitions.Add(new PointDefinition(label1, coord1, coord2, dir1, explicitEndIsStraight));

            var dir2 = direction2 == ">" ? DivergeDirection.Forward : DivergeDirection.Backward;
            pointDefinitions.Add(new PointDefinition(label2, coord2, coord1, dir2, explicitEndIsStraight));

            // Parse addresses if present - may contain addresses for both sub-points
            if (addressPart is not null)
            {
                var number1 = ExtractPointNumber(label1);
                var number2 = ExtractPointNumber(label2);

                if (number1 == number2 && number1 > 0)
                {
                    // Same point number (e.g., 1a/1b) - single point with sub-point addresses
                    var point = ParsePointAddresses(number1, addressPart, lockAddressOffset);
                    if (point is not null) points.Add(point);
                }
                else if (number1 > 0 && number2 > 0)
                {
                    // Different point numbers - split addresses (comma-separated, alternating)
                    var addrParts = addressPart.Split(',', StringSplitOptions.TrimEntries);
                    if (addrParts.Length >= 2)
                    {
                        var point1 = ParsePointAddresses(number1, addrParts[0], lockAddressOffset);
                        var point2 = ParsePointAddresses(number2, addrParts[1], lockAddressOffset);
                        if (point1 is not null) points.Add(point1);
                        if (point2 is not null) points.Add(point2);
                    }
                }
            }

            return;
        }

        // Try single point
        var singleMatch = SinglePointPattern().Match(topologyPart);
        if (singleMatch.Success)
        {
            var switchPoint = GridCoordinate.Parse(singleMatch.Groups[1].Value);
            var dirBefore = singleMatch.Groups[2].Value;
            var label = singleMatch.Groups[3].Value;
            var dirAfter = singleMatch.Groups[4].Value;
            var explicitEnd = GridCoordinate.Parse(singleMatch.Groups[5].Value);
            var explicitEndIsStraight = singleMatch.Groups[6].Success;

            var direction = !string.IsNullOrEmpty(dirBefore) ? dirBefore : dirAfter;
            var dir = direction == ">" ? DivergeDirection.Forward : DivergeDirection.Backward;
            pointDefinitions.Add(new PointDefinition(label, switchPoint, explicitEnd, dir, explicitEndIsStraight));

            // Parse address
            if (addressPart is not null)
            {
                var number = ExtractPointNumber(label);
                if (number > 0)
                {
                    var point = ParsePointAddresses(number, addressPart, lockAddressOffset);
                    if (point is not null) points.Add(point);
                }
            }

            return;
        }
    }

    private static Point? ParsePointAddresses(int number, string addressPart, int lockAddressOffset)
    {
        if (string.IsNullOrWhiteSpace(addressPart)) return null;

        if (addressPart.Contains('('))
        {
            var (straightAddresses, straightSubPoints) = ParseGroupedAddressesWithSubPoints(addressPart, '+');
            var (divergingAddresses, divergingSubPoints) = ParseGroupedAddressesWithSubPoints(addressPart, '-');
            if (straightAddresses.Length > 0 || divergingAddresses.Length > 0)
            {
                var subPointMap = BuildSubPointMap(straightSubPoints.Concat(divergingSubPoints));
                return new Point(number, straightAddresses, divergingAddresses, lockAddressOffset, subPointMap);
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
                return new Point(number, addresses, addresses, lockAddressOffset, subPointMap);
            }
        }

        return null;
    }

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

    private static int ExtractPointNumber(string label)
    {
        var digits = new string(label.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : 0;
    }

    #endregion

    #region Signal Parsing

    private void ParseSignalLine(
        string line,
        List<SignalDefinition> signalDefinitions,
        List<SignalHardware> signalAddresses)
    {
        // Split signal definition from @address
        var atIndex = line.IndexOf('@');
        var signalPart = atIndex >= 0 ? line[..atIndex].Trim() : line;
        var addressPart = atIndex >= 0 ? line[(atIndex + 1)..].Trim() : null;

        var signalMatch = SignalPattern().Match(signalPart);
        if (!signalMatch.Success) return;

        var coord = GridCoordinate.Parse(signalMatch.Groups[1].Value);
        var leftArrow = signalMatch.Groups[2].Value;
        var name = signalMatch.Groups[3].Value;
        var label = signalMatch.Groups[4].Success && signalMatch.Groups[4].Length > 0 ? signalMatch.Groups[4].Value : null;
        var rightArrow = signalMatch.Groups[5].Value;
        var typeMarker = signalMatch.Groups[6].Value;

        var drivesRight = !string.IsNullOrEmpty(rightArrow);
        var signalType = ParseSignalType(typeMarker);

        signalDefinitions.Add(new SignalDefinition(name, coord, drivesRight, signalType, label));

        // Parse hardware address if present
        if (addressPart is not null)
        {
            int address;
            int? feedbackAddress = null;

            if (addressPart.Contains(';'))
            {
                var parts = addressPart.Split(';', StringSplitOptions.TrimEntries);
                if (!int.TryParse(parts[0], out address)) return;
                if (parts.Length > 1 && int.TryParse(parts[1], out var fb))
                    feedbackAddress = fb;
            }
            else
            {
                if (!int.TryParse(addressPart, out address)) return;
            }

            signalAddresses.Add(new SignalHardware(name, address, feedbackAddress));
        }
    }

    #endregion

    #region Label Parsing

    private static void ParseLabelLine(string line, List<LabelDefinition> labels)
    {
        var labelMatch = LabelPattern().Match(line);
        if (labelMatch.Success)
        {
            var start = GridCoordinate.Parse(labelMatch.Groups[1].Value);
            var text = labelMatch.Groups[2].Value;
            var end = GridCoordinate.Parse(labelMatch.Groups[3].Value);
            labels.Add(new LabelDefinition(text, start, end));
        }
    }

    #endregion

    #region Gap Parsing

    private static void ParseGapLine(string line, TrackGraph graph, List<GapDefinition> gaps)
    {
        var gapMatch = GapPattern().Match(line);
        if (!gapMatch.Success) return;

        var coord = GridCoordinate.Parse(gapMatch.Groups[1].Value);
        GridCoordinate? linkEnd = null;

        if (gapMatch.Groups[2].Success && !string.IsNullOrEmpty(gapMatch.Groups[2].Value))
        {
            linkEnd = GridCoordinate.Parse(gapMatch.Groups[2].Value);
            var link = graph.GetLink(coord, linkEnd.Value);
            if (link != null)
                link.HasGap = true;
        }

        gaps.Add(new GapDefinition(coord, linkEnd));
    }

    #endregion

    #region Settings Parsing

    private static void ParseSettingsLine(string line, ref int lockAddressOffset, ref int lockReleaseDelaySeconds)
    {
        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return;

        if (parts[0].Equals("LockOffset", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(parts[1], out var offset))
                lockAddressOffset = offset;
        }
        else if (parts[0].Equals("LockReleaseDelay", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(parts[1], out var delay) && delay >= 0)
                lockReleaseDelaySeconds = delay;
        }
    }

    #endregion

    #region Translations Parsing

    private static LabelTranslationData ParseTranslationLines(List<string> lines)
    {
        if (lines.Count == 0) return new LabelTranslationData([], []);

        var languages = lines[0].Split(';').Select(s => s.Trim()).ToArray();
        var rows = new List<string[]>();

        for (var i = 1; i < lines.Count; i++)
        {
            var row = lines[i].Split(';').Select(s => s.Trim()).ToArray();
            rows.Add(row);
        }

        return new LabelTranslationData(languages, rows);
    }

    #endregion

    #region Turntable Parsing

    private static void ParseTurntableLine(string line, ref int startTrack, ref int endTrack, ref int offset, ref bool hasTracks)
    {
        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return;

        if (parts[0].Equals("Tracks", StringComparison.OrdinalIgnoreCase))
        {
            var range = parts[1].Split('-', StringSplitOptions.TrimEntries);
            if (range.Length == 2 && int.TryParse(range[0], out var start) && int.TryParse(range[1], out var end))
            {
                startTrack = start;
                endTrack = end;
                hasTracks = true;
            }
        }
        else if (parts[0].Equals("Offset", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(parts[1], out var o))
                offset = o;
        }
    }

    #endregion

    #region Route Parsing

    private void ParseRouteLine(
        string line,
        List<TrainRouteCommand> commands,
        TrackGraph graph,
        IReadOnlyList<PointDefinition> pointDefinitions,
        IReadOnlyList<SignalDefinition> signalDefinitions)
    {
        // Extract optional @address
        int? routeAddress = null;
        var atIndex = line.IndexOf('@');
        var routeLine = line;
        if (atIndex >= 0)
        {
            var addressStr = line[(atIndex + 1)..].Trim();
            if (int.TryParse(addressStr, out var addr))
                routeAddress = addr;
            routeLine = line[..atIndex].Trim();
        }

        // Split on first ':' only
        var colonIndex = routeLine.IndexOf(':');
        var signalPart = colonIndex >= 0 ? routeLine[..colonIndex].Trim() : routeLine.Trim();
        var payloadPart = colonIndex >= 0 ? routeLine[(colonIndex + 1)..].Trim() : null;

        var signals = signalPart.Split('-', StringSplitOptions.TrimEntries);
        if (signals.Length != 2) return;
        if (!int.TryParse(signals[0], out var fromSignal) || !int.TryParse(signals[1], out var toSignal))
            return;

        // Determine route type
        if (payloadPart is not null && payloadPart.Contains('.'))
        {
            // Composite route: from-to:via1.via2.via3
            ParseCompositeRoute(fromSignal, toSignal, payloadPart, commands, routeAddress);
        }
        else if (payloadPart is null)
        {
            // Fully auto-derived: from-to (no colon)
            var pointCommands = DeriveRoutePoints(fromSignal, toSignal, graph, pointDefinitions, signalDefinitions);
            if (pointCommands is not null)
                commands.Add(new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, pointCommands) { Address = routeAddress });
            else
                _logger?.LogWarning("Could not auto-derive route {From}-{To}", fromSignal, toSignal);
        }
        else
        {
            // Has payload - check if it's all x-prefixed (auto-derive + flank) or manual
            var pointPositions = payloadPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var allFlankProtection = pointPositions.All(pp => pp.StartsWith('x') || pp.StartsWith('X'));

            if (allFlankProtection)
            {
                // Auto-derive on-route points, add manual flank protection
                var onRoutePoints = DeriveRoutePoints(fromSignal, toSignal, graph, pointDefinitions, signalDefinitions);
                var flankPoints = pointPositions.Select(pp => pp.ToPointCommand()).ToList();

                if (onRoutePoints is not null)
                {
                    var allPoints = onRoutePoints.Concat(flankPoints).ToList();
                    commands.Add(new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, allPoints) { Address = routeAddress });
                }
                else
                {
                    _logger?.LogWarning("Could not auto-derive route {From}-{To}, using flank points only", fromSignal, toSignal);
                    commands.Add(new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, flankPoints) { Address = routeAddress });
                }
            }
            else
            {
                // Fully manual (backward compatible)
                var pointCommands = pointPositions.Select(pp => pp.ToPointCommand()).ToList();
                commands.Add(new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, pointCommands) { Address = routeAddress });
            }
        }
    }

    private static void ParseCompositeRoute(
        int fromSignal,
        int toSignal,
        string payload,
        List<TrainRouteCommand> commands,
        int? routeAddress)
    {
        var route = payload.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (route.Length < 2) return;

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
                IntermediateSignals = intermediateSignals,
                Address = routeAddress
            });
        }
    }

    private List<PointCommand>? DeriveRoutePoints(
        int fromSignalNumber,
        int toSignalNumber,
        TrackGraph graph,
        IReadOnlyList<PointDefinition> pointDefinitions,
        IReadOnlyList<SignalDefinition> signalDefinitions)
    {
        var fromSignal = signalDefinitions.FirstOrDefault(s => s.Name == fromSignalNumber.ToString());
        var toSignal = signalDefinitions.FirstOrDefault(s => s.Name == toSignalNumber.ToString());

        if (fromSignal is null || toSignal is null)
        {
            _logger?.LogWarning("Cannot auto-derive route: signal {From} or {To} not found in topology",
                fromSignalNumber, toSignalNumber);
            return null;
        }

        var path = graph.FindRoutePath(fromSignal.Coordinate, toSignal.Coordinate, fromSignal.DrivesRight);
        if (path.Count == 0)
        {
            _logger?.LogWarning("No path found for auto-derived route {From}-{To}", fromSignalNumber, toSignalNumber);
            return null;
        }

        return graph.DeriveRoutePoints(path, pointDefinitions);
    }

    #endregion

    private static SignalType ParseSignalType(string marker) => marker switch
    {
        "x" => SignalType.Hidden,
        "u" => SignalType.OutboundMain,
        "i" => SignalType.InboundMain,
        "h" => SignalType.MainDwarf,
        "d" => SignalType.ShuntingDwarf,
        _ => SignalType.Default,
    };
}
