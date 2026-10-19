using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using System.Globalization;

namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// Parses a unified Station.txt file containing all station configuration:
/// tracks, points (with hardware addresses), signals (with addresses),
/// routes, labels, gaps, turntable, translations, and settings.
/// </summary>
public partial class UnifiedStationParser
{
    private readonly ILogger _logger;

    public UnifiedStationParser(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
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

    // A plain crossing: coord, then one parenthesised way per track through it. Each way spells
    // the point position that lays it, a '|', and what must be held while it is laid. The comma
    // between the ways is optional, as is the space.
    [GeneratedRegex(@"^\d+\.\d+\s*(?:\(\s*[^()|]+\|[^()|]*\)\s*,?\s*)+$")]
    private static partial Regex CrossingPattern();

    // One way of a crossing: (governing | condition[,condition...])
    [GeneratedRegex(@"\(\s*([^()|]+?)\s*\|\s*([^()|]*?)\s*\)")]
    private static partial Regex CrossingWayPattern();

    // Label pattern: coord[text]coord
    [GeneratedRegex(@"(\d+\.\d+)\[([^\]]+)\](\d+\.\d+)")]
    private static partial Regex LabelPattern();

    // Signal pattern: coord:name[label]>: with optional type suffix and @address.
    // The colon before the type marker is optional, so both 2.5:<21:i and 7.1:<47s parse.
    [GeneratedRegex(@"(\d+\.\d+):([<]?)([A-Za-z]?\d+)(?:\[([^\]]+)\])?([>]?):?([a-z])?")]
    private static partial Regex SignalPattern();

    // Gap patterns: coord| or coord|coord
    [GeneratedRegex(@"(\d+\.\d+)\|(\d+\.\d+)?")]
    private static partial Regex GapPattern();

    // Occupancy section pattern: number:coord,coord, where a ! on the number marks a stretch
    // an operator frees by hand, spelled as the forced-necessary marker in [Tracks] is.
    [GeneratedRegex(@"^(\d+)(!?):(\d+\.\d+),(\d+\.\d+)$")]
    private static partial Regex OccupancySectionPattern();

    // Platform pattern: coord,coord:side, where side is u (under) or o (over)
    [GeneratedRegex(@"^(\d+\.\d+),(\d+\.\d+):([uoUO])$")]
    private static partial Regex PlatformPattern();

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
        var platforms = new List<PlatformDefinition>();
        var forcedNecessary = new HashSet<GridCoordinate>();
        var points = new List<Point>();
        var crossings = new List<CrossingDefinition>();
        var turntableTracks = new List<TurntableTrack>();
        var signalAddresses = new List<SignalHardware>();
        var shuntingAreas = new List<ShuntingArea>();
        var buttonLines = new List<string>();
        var occupancySections = new List<OccupancySection>();
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
                if (currentSection == "TRANSLATIONS" && translationLines.Count > 0)
                {
                    translations = ParseTranslationLines(translationLines);
                }

                currentSection = sectionMatch.Groups[1].Value.ToUpperInvariant();
                continue;
            }

            switch (currentSection)
            {
                case "TRACKS":
                    ParseTrackLine(line, graph, forcedNecessary);
                    break;

                case "POINTS":
                    ParsePointLine(line, graph, pointDefinitions, points, crossings, ref lockAddressOffset);
                    break;

                case "SIGNALS":
                    ParseSignalLine(line, signalDefinitions, signalAddresses);
                    break;

                case "LABELS":
                    ParseLabelLine(line, labels);
                    break;

                case "GAPS":
                    ParseGapLine(line, graph, gaps);
                    break;

                case "PLATFORMS":
                    ParsePlatformLine(line, platforms);
                    break;

                case "SETTINGS":
                    ParseSettingsLine(line, ref lockAddressOffset, ref lockReleaseDelaySeconds);
                    break;

                case "TRANSLATIONS":
                    translationLines.Add(line);
                    break;

                case "TURNTABLE":
                    ParseTurntableLine(line, ref turntableStartTrack, ref turntableEndTrack, ref turntableOffset, ref hasTurntableTracks);
                    break;


                case "HIDDENPOINTS":
                    ParseHiddenPointLine(line, points, ref lockAddressOffset);
                    break;

                case "BUTTONS":
                    buttonLines.Add(line);
                    break;

                case "SHUNTINGAREAS":
                    ParseShuntingAreaLine(line, shuntingAreas);
                    break;

                case "OCCUPANCYSECTIONS":
                    ParseOccupancySectionLine(line, occupancySections);
                    break;

                case "ROUTES":
                    ParseRouteLine(line, trainRoutes, graph, pointDefinitions, signalDefinitions);
                    break;
            }
        }

        // Handle translations if we're still in that section at EOF
        if (currentSection == "TRANSLATIONS" && translationLines.Count > 0 && translations is null)
        {
            translations = ParseTranslationLines(translationLines);
        }

        // Create turntable tracks if both Tracks and Offset were specified
        if (hasTurntableTracks && turntableOffset > 0)
        {
            for (int number = turntableStartTrack; number <= turntableEndTrack; number++)
                turntableTracks.Add(new TurntableTrack(number, number + turntableOffset));
        }

        ValidateBufferStops(graph, signalDefinitions);
        ValidatePlatforms(graph, platforms);

        var topology = new YardTopology(name, graph, pointDefinitions, signalDefinitions, labels, gaps, platforms, forcedNecessary, crossings);

        // Wants the topology rather than the graph alone: whether a stretch has its track to
        // itself is only known once every stretch has been resolved to the links it covers.
        ValidateOccupancySections(topology, occupancySections);
        ValidateCrossings(topology);

        // A crossing's conditions are its flank protection, so they belong to every route that
        // lays a way over it. Added here rather than written into each route by hand, which is
        // what declaring the crossing is for.
        ApplyCrossingConditions(topology, trainRoutes);

        var buttons = ParseButtonLines(buttonLines);

        return new UnifiedStationData(
            name,
            topology,
            points,
            turntableTracks,
            trainRoutes,
            signalAddresses,
            shuntingAreas,
            occupancySections,
            buttons,
            translations,
            lockAddressOffset,
            lockReleaseDelaySeconds);
    }

    public async Task<UnifiedStationData> ParseFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Station file not found", filePath);

        var content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return Parse(content);
    }

    #region Track Parsing

    private void ParseTrackLine(string line, TrackGraph graph, HashSet<GridCoordinate> forcedNecessary)
    {
        var coordMatches = CoordinateWithMarkerPattern().Matches(line);
        GridCoordinate? previousCoord = null;
        var previousGroup = 0;

        foreach (Match match in coordMatches)
        {
            var coord = GridCoordinate.Parse(match.Groups[1].Value);

            // Coordinates inside one pair of parentheses belong to the same uncontrolled run;
            // a link is uncontrolled when both its ends are in the same run.
            var group = ParenthesisGroupAt(line, match.Index);
            var uncontrolled = group > 0 && group == previousGroup;
            previousGroup = group;
            var hasForcedMarker = match.Groups[2].Success && match.Groups[2].Value == "!";

            if (hasForcedMarker)
            {
                forcedNecessary.Add(coord);
            }

            if (previousCoord.HasValue)
            {
                if (previousCoord.Value.Column > coord.Column)
                {
                    LogDecreasingTrackLink(previousCoord.Value, coord);
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
                        AddTrackLink(graph,
                            new GridCoordinate(row, col),
                            new GridCoordinate(row, nextCol), uncontrolled);
                    }
                }
                else
                {
                    // Different rows = diagonal/bend, single link
                    AddTrackLink(graph, previousCoord.Value, coord, uncontrolled);
                }
            }

            previousCoord = coord;
        }
    }

    private static void AddTrackLink(TrackGraph graph, GridCoordinate from, GridCoordinate to, bool uncontrolled)
    {
        graph.TryAddLink(from, to);
        if (uncontrolled && graph.GetLink(from, to) is { } link)
            link.IsUncontrolled = true;
    }

    /// <summary>
    /// Returns a number identifying the parenthesised run the position lies in, or 0 outside any.
    /// </summary>
    private static int ParenthesisGroupAt(string line, int index)
    {
        var group = 0;
        var open = false;
        for (var i = 0; i < index && i < line.Length; i++)
        {
            if (line[i] == '(') { group++; open = true; }
            else if (line[i] == ')') open = false;
        }
        return open ? group : 0;
    }

    #endregion

    #region Point Parsing

    private void ParsePointLine(
        string line,
        TrackGraph graph,
        List<PointDefinition> pointDefinitions,
        List<Point> points,
        List<CrossingDefinition> crossings,
        ref int lockAddressOffset)
    {
        // Split topology part from @address part
        var atIndex = line.IndexOf('@');
        var topologyPart = atIndex >= 0 ? line[..atIndex].Trim() : line;
        var addressPart = atIndex >= 0 ? line[(atIndex + 1)..].Trim() : null;

        // A crossing carries no address and no arms of its own; the '|' is what tells it apart
        // from a point, and it is read before the point patterns so a way like (8-|29+) is never
        // mistaken for a label.
        if (topologyPart.Contains('|'))
        {
            ParseCrossingLine(topologyPart, crossings);
            return;
        }

        // Peel off any trailing slave clauses (&<pos>:<n><pos>[,...]) from the address portion
        IReadOnlyList<SlaveCommand>? slaves = null;
        if (addressPart is not null)
        {
            (addressPart, slaves) = SplitAddressAndSlaves(addressPart);
        }

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
                    if (point is not null) points.Add(slaves is null ? point : point with { Slaves = slaves });
                }
                else if (number1 > 0 && number2 > 0)
                {
                    // Different point numbers - split addresses (comma-separated, alternating)
                    var addrParts = addressPart.Split(',', StringSplitOptions.TrimEntries);
                    if (addrParts.Length >= 2)
                    {
                        var point1 = ParsePointAddresses(number1, addrParts[0], lockAddressOffset);
                        var point2 = ParsePointAddresses(number2, addrParts[1], lockAddressOffset);
                        // Slaves on a paired-points line with distinct numbers attach to the first point
                        if (point1 is not null) points.Add(slaves is null ? point1 : point1 with { Slaves = slaves });
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
                    if (point is not null) points.Add(slaves is null ? point : point with { Slaves = slaves });
                }
            }

            return;
        }
    }

    private static Point? ParsePointAddresses(int number, string addressPart, int lockAddressOffset, bool isHidden = false)
    {
        if (string.IsNullOrWhiteSpace(addressPart)) return null;

        if (addressPart.Contains('('))
        {
            var (straightAddresses, straightSubPoints, straightMsgKinds) = ParseGroupedAddressesWithSubPoints(addressPart, '+');
            var (divergingAddresses, divergingSubPoints, divergingMsgKinds) = ParseGroupedAddressesWithSubPoints(addressPart, '-');
            if (straightAddresses.Length > 0 || divergingAddresses.Length > 0)
            {
                var subPointMap = BuildSubPointMap(straightSubPoints.Concat(divergingSubPoints));
                var messageKinds = BuildMessageKindMap(straightMsgKinds.Concat(divergingMsgKinds));
                return new Point(number, straightAddresses, divergingAddresses, lockAddressOffset, subPointMap, IsHidden: isHidden, MessageKinds: messageKinds);
            }
        }
        else
        {
            var parsed = addressPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => a.Trim().ToAddressWithSubPointAndKind())
                .Where(p => p.Address != 0)
                .ToArray();
            var addresses = parsed.Select(p => p.Address).ToArray();
            if (addresses.Length > 0)
            {
                var subPointMap = BuildSubPointMap(parsed.Select(p => (p.Address, p.SubPoint)));
                var messageKinds = BuildMessageKindMap(parsed.Select(p => (p.Address, p.MessageKind)));
                return new Point(number, addresses, addresses, lockAddressOffset, subPointMap, IsHidden: isHidden, MessageKinds: messageKinds);
            }
        }

        return null;
    }

    private static (int[] Addresses, IEnumerable<(int Address, char? SubPoint)> SubPoints, IEnumerable<(int Address, AccessoryMessageKind MessageKind)> MessageKinds) ParseGroupedAddressesWithSubPoints(string addressPart, char positionSuffix)
    {
        var addresses = new List<int>();
        var subPoints = new List<(int Address, char? SubPoint)>();
        var messageKinds = new List<(int Address, AccessoryMessageKind MessageKind)>();
        var searchSuffix = ")" + positionSuffix;
        var suffixIndex = addressPart.IndexOf(searchSuffix, StringComparison.Ordinal);

        while (suffixIndex >= 0)
        {
            var openIndex = addressPart.LastIndexOf('(', suffixIndex);
            if (openIndex >= 0 && openIndex < suffixIndex)
            {
                var addressesStr = addressPart.Substring(openIndex + 1, suffixIndex - openIndex - 1);
                var parsed = addressesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(a => a.ToAddressWithSubPointAndKind())
                    .Where(p => p.Address != 0);
                foreach (var p in parsed)
                {
                    addresses.Add(p.Address);
                    subPoints.Add((p.Address, p.SubPoint));
                    messageKinds.Add((p.Address, p.MessageKind));
                }
            }
            suffixIndex = addressPart.IndexOf(searchSuffix, suffixIndex + 2, StringComparison.Ordinal);
        }

        return ([.. addresses], subPoints, messageKinds);
    }

    private static IReadOnlyDictionary<int, char>? BuildSubPointMap(
        IEnumerable<(int Address, char? SubPoint)> parsed)
    {
        var map = new Dictionary<int, char>();
        foreach (var (address, subPoint) in parsed)
            if (subPoint.HasValue) map[Math.Abs(address)] = subPoint.Value;
        return map.Count > 0 ? map : null;
    }

    private static IReadOnlyDictionary<int, AccessoryMessageKind>? BuildMessageKindMap(
        IEnumerable<(int Address, AccessoryMessageKind MessageKind)> parsed)
    {
        var map = new Dictionary<int, AccessoryMessageKind>();
        foreach (var (address, messageKind) in parsed)
            if (messageKind != AccessoryMessageKind.Command)
                map[Math.Abs(address)] = messageKind;
        return map.Count > 0 ? map : null;
    }

    private static int ExtractPointNumber(string label)
    {
        var digits = new string(label.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : 0;
    }

    private static void ParseHiddenPointLine(
        string line,
        List<Point> points,
        ref int lockAddressOffset)
    {
        // Check for LockOffset setting
        var settingParts = line.Split(':', StringSplitOptions.TrimEntries);
        if (settingParts.Length == 2 && settingParts[0].Equals("LockOffset", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(settingParts[1], out var offset))
                lockAddressOffset = offset;
            return;
        }

        // Format: pointNumber  @addresses [&<pos>:<slaves>...]
        var atIndex = line.IndexOf('@');
        if (atIndex < 0) return;

        var numberPart = line[..atIndex].Trim();
        var addressPart = line[(atIndex + 1)..].Trim();
        var (cleanedAddresses, slaves) = SplitAddressAndSlaves(addressPart);

        if (!int.TryParse(numberPart, out var pointNumber) || pointNumber <= 0) return;

        var point = ParsePointAddresses(pointNumber, cleanedAddresses, lockAddressOffset, isHidden: true);
        if (point is not null) points.Add(slaves is null ? point : point with { Slaves = slaves });
    }

    /// <summary>
    /// Splits an address portion into the address grammar and any trailing slave clauses.
    /// Slave clauses are whitespace-separated tokens of the form &lt;+|-&gt;:&lt;n&gt;&lt;+|-&gt;[,&lt;n&gt;&lt;+|-&gt;...].
    /// Returns the cleaned address string and the parsed slaves (null if none).
    /// </summary>
    private static (string Addresses, IReadOnlyList<SlaveCommand>? Slaves) SplitAddressAndSlaves(string addressPart)
    {
        // Find the first whitespace-prefixed '&' — that marks the start of slave clauses.
        // We require whitespace before & to avoid colliding with any future address grammar.
        var slaveIndex = -1;
        for (int i = 1; i < addressPart.Length; i++)
        {
            if (addressPart[i] == '&' && char.IsWhiteSpace(addressPart[i - 1]))
            {
                slaveIndex = i;
                break;
            }
        }
        if (slaveIndex < 0) return (addressPart, null);

        var addresses = addressPart[..slaveIndex].TrimEnd();
        var slavesPart = addressPart[slaveIndex..];

        var slaves = new List<SlaveCommand>();
        var seenMasterPositions = new HashSet<PointPosition>();
        foreach (var raw in slavesPart.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colonIdx = raw.IndexOf(':');
            if (colonIdx < 1) throw new FormatException($"Malformed slave clause: '&{raw}'");
            var posChar = raw[..colonIdx].Trim();
            if (posChar.Length != 1) throw new FormatException($"Malformed slave clause master position: '&{raw}'");
            var masterPos = posChar[0] switch
            {
                '+' => PointPosition.Straight,
                '-' => PointPosition.Diverging,
                _ => PointPosition.Undefined,
            };
            if (masterPos == PointPosition.Undefined)
                throw new FormatException($"Invalid master position in slave clause: '&{raw}'");
            if (!seenMasterPositions.Add(masterPos))
                throw new FormatException($"Duplicate slave clause for master position '{posChar}'");

            foreach (var spec in raw[(colonIdx + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (spec.Length < 2)
                    throw new FormatException($"Malformed slave spec: '{spec}'");
                var slavePos = spec[^1] switch
                {
                    '+' => PointPosition.Straight,
                    '-' => PointPosition.Diverging,
                    _ => PointPosition.Undefined,
                };
                if (slavePos == PointPosition.Undefined)
                    throw new FormatException($"Invalid slave position in slave spec: '{spec}'");
                if (!int.TryParse(spec[..^1], out var slaveNum) || slaveNum <= 0)
                    throw new FormatException($"Invalid slave point number in slave spec: '{spec}'");
                slaves.Add(new SlaveCommand(masterPos, slaveNum, slavePos));
            }
        }
        return (addresses, slaves.Count > 0 ? slaves : null);
    }

    #endregion

    #region Crossing Parsing

    /// <summary>
    /// Reads a crossing line: <c>5.8(8-|29+)(29-|8+)</c>. The coordinate is where the tracks
    /// cross; each parenthesised group is one way through it, spelling first the point position
    /// that lays that way and then, after the <c>|</c>, the positions that must hold while it is
    /// laid. Whitespace and a comma between the groups are allowed and ignored.
    /// </summary>
    private void ParseCrossingLine(string line, List<CrossingDefinition> crossings)
    {
        if (!CrossingPattern().IsMatch(line))
        {
            LogMalformedCrossingLine(line);
            return;
        }

        var coordinate = GridCoordinate.Parse(CoordinateWithMarkerPattern().Match(line).Groups[1].Value);
        var ways = new List<CrossingWay>();

        foreach (Match match in CrossingWayPattern().Matches(line))
        {
            if (ParseCrossingPosition(match.Groups[1].Value, isOnRoute: true) is not { } governing)
            {
                LogInvalidCrossingPosition(match.Groups[1].Value, coordinate);
                return;
            }

            var conditions = new List<PointCommand>();
            foreach (var token in match.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // A condition holds a point clear of the crossing rather than carrying the
                // movement over it, which is what flank protection is.
                if (ParseCrossingPosition(token, isOnRoute: false) is not { } condition)
                {
                    LogInvalidCrossingPosition(token, coordinate);
                    return;
                }
                conditions.Add(condition);
            }

            ways.Add(new CrossingWay(governing, conditions));
        }

        if (ways.Count == 0)
        {
            LogMalformedCrossingLine(line);
            return;
        }

        crossings.Add(new CrossingDefinition(coordinate, ways));
    }

    /// <summary>A point number with a <c>+</c> or <c>-</c> suffix, as a crossing spells one.</summary>
    private static PointCommand? ParseCrossingPosition(string token, bool isOnRoute)
    {
        token = token.Trim();
        if (token.Length < 2) return null;
        var position = token[^1].ToPointPosition;
        if (position == PointPosition.Undefined) return null;
        if (!int.TryParse(token[..^1], out var number) || number <= 0) return null;
        return new PointCommand(number, position, null, isOnRoute);
    }

    /// <summary>
    /// A crossing must sit on track, must be crossed by exactly two ways, and each way must reach
    /// the crossing by two arms — one on either side. Anything else describes something that is
    /// not a plain crossing, and the diagram would draw it wrong rather than not at all.
    /// </summary>
    private void ValidateCrossings(YardTopology topology)
    {
        var numbers = topology.Points.Select(point => point.Label.PointNumber()).ToHashSet();

        foreach (var crossing in topology.Crossings)
        {
            var node = topology.Graph.GetNode(crossing.Coordinate);
            if (node is null)
            {
                LogCrossingNotOnTrack(crossing.Coordinate);
                continue;
            }
            if (node.Degree != 4) LogCrossingWithoutFourArms(crossing.Coordinate, node.Degree);
            if (crossing.Ways.Count != 2) LogCrossingWayCount(crossing.Coordinate, crossing.Ways.Count);

            var claimed = new Dictionary<TrackLink, int>();
            foreach (var way in crossing.Ways)
            {
                foreach (var point in way.Conditions.Append(way.Governing).Where(point => !numbers.Contains(point.Number)))
                    LogCrossingPointNotFound(point.Number, crossing.Coordinate);

                var arms = topology.ArmsOf(crossing, way);
                if (arms.Count != 2) LogCrossingWayArmCount(way.Governing.Number, crossing.Coordinate, arms.Count);
                foreach (var arm in arms)
                {
                    if (claimed.TryGetValue(arm, out var other)) LogCrossingArmClaimedTwice(arm, crossing.Coordinate, other, way.Governing.Number);
                    else claimed[arm] = way.Governing.Number;
                }
            }

            foreach (var link in topology.Graph.GetLinksAt(crossing.Coordinate).Where(link => !claimed.ContainsKey(link)))
                LogCrossingArmWithoutWay(link, crossing.Coordinate);
        }
    }

    /// <summary>
    /// Gives every route that lays a way over a crossing that way's conditions, as flank
    /// protection. A route already naming the point at the position the crossing wants keeps its
    /// own command; one naming the opposite position contradicts the crossing and is left alone,
    /// with a warning, since nothing can be added that would make it safe.
    /// </summary>
    private void ApplyCrossingConditions(YardTopology topology, List<TrainRouteCommand> routes)
    {
        if (topology.Crossings.Count == 0) return;

        for (var i = 0; i < routes.Count; i++)
        {
            var route = routes[i];
            List<PointCommand>? added = null;

            foreach (var (crossing, way) in topology.WaysLaidBy(route.PointCommands))
            {
                foreach (var condition in way.Conditions)
                {
                    var existing = route.PointCommands.FirstOrDefault(pc => pc.Number == condition.Number)
                        ?? added?.FirstOrDefault(pc => pc.Number == condition.Number);
                    if (existing is not null)
                    {
                        if (existing.Position != condition.Position)
                            LogRouteContradictsCrossing(route.FromSignal, route.ToSignal, condition.Number, crossing.Coordinate);
                        continue;
                    }
                    (added ??= []).Add(new PointCommand(condition.Number, condition.Position, null, IsOnRoute: false));
                }
            }

            if (added is not null) routes[i] = route with { PointCommands = [.. route.PointCommands, .. added] };
        }
    }

    #endregion


    #region Signal Parsing

    /// <summary>
    /// A buffer stop stands where the track physically ends, so its coordinate must be a node
    /// with a single link, and it must not be at the edge of the layout, where the track leaves
    /// the station rather than ends. The declared direction must be the direction of the track
    /// leading up to it, because that is the way the symbol is drawn.
    /// </summary>
    private void ValidateBufferStops(TrackGraph graph, List<SignalDefinition> signalDefinitions)
    {
        foreach (var signal in signalDefinitions.Where(s => s.Type.IsBufferStop))
        {
            var node = graph.GetNode(signal.Coordinate);
            if (node is null || node.Degree != 1)
            {
                LogBufferStopNotAtTrackEnd(signal.Name, signal.Coordinate);
            }
            else if (signal.Coordinate.Column == 0 || signal.Coordinate.Column == graph.MaxColumn)
            {
                LogBufferStopAtLayoutEdge(signal.Name, signal.Coordinate);
            }
            else if (signal.DrivesRight != (node.IncomingLinks.Count == 1))
            {
                LogBufferStopFacesAwayFromTrack(signal.Name, signal.Coordinate);
            }
        }
    }


    private static void ParseSignalLine(
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

    #region Platform Parsing

    /// <summary>
    /// Parses one line of the [Platforms] section: <c>from,to:side</c>, where side is
    /// <c>u</c> for a platform under the track and <c>o</c> for one over it. The pair of
    /// coordinates names the stretch of track the platform runs alongside, not track of its
    /// own, so it is spelled column-first like every other coordinate pair.
    /// </summary>
    private void ParsePlatformLine(string line, List<PlatformDefinition> platforms)
    {
        var match = PlatformPattern().Match(line);
        if (!match.Success)
        {
            LogMalformedPlatformLine(line);
            return;
        }

        var from = GridCoordinate.Parse(match.Groups[1].Value);
        var to = GridCoordinate.Parse(match.Groups[2].Value);
        var side = char.ToLowerInvariant(match.Groups[3].Value[0]) == 'o'
            ? PlatformSide.Over
            : PlatformSide.Under;

        platforms.Add(new PlatformDefinition(from, to, side));
    }

    /// <summary>
    /// A platform serves a stretch of track, so both its ends must be coordinates on the track
    /// and they must not be the same one. Passengers board along one track, so the stretch runs
    /// within a single row; a platform spanning rows would be drawn as a slanting band and is
    /// almost certainly a typo.
    /// </summary>
    private void ValidatePlatforms(TrackGraph graph, List<PlatformDefinition> platforms)
    {
        foreach (var platform in platforms)
        {
            if (graph.GetNode(platform.From) is null)
                LogPlatformCoordinateNotInGraph(platform.From);
            if (graph.GetNode(platform.To) is null)
                LogPlatformCoordinateNotInGraph(platform.To);

            if (platform.From == platform.To)
                LogPlatformWithoutLength(platform.From);
            else if (platform.From.Row != platform.To.Row)
                LogPlatformSpansRows(platform.From, platform.To);
        }
    }

    #endregion

    #region Shunting Area Parsing

    /// <summary>
    /// Parses one line of the [ShuntingAreas] section: <c>number:name:point[,point…]</c>.
    /// A point given as a bare number is one the shunter operates while the area is released;
    /// a point given with a <c>+</c> or <c>-</c> suffix is flank protection, held in that
    /// position for as long as the area is released.
    /// </summary>
    private void ParseShuntingAreaLine(string line, List<ShuntingArea> areas)
    {
        var parts = line.Split(':', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            LogMalformedShuntingAreaLine(line);
            return;
        }
        if (!int.TryParse(parts[0], out var number) || number <= 0)
        {
            LogInvalidShuntingAreaNumber(line);
            return;
        }

        var points = new List<PointCommand>();
        foreach (var token in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var position = token[^1].ToPointPosition;
            var numberText = position == PointPosition.Undefined ? token : token[..^1];
            var pointNumber = numberText.ToIntOrZero;
            if (pointNumber <= 0)
            {
                LogInvalidShuntingAreaPoint(token, number);
                continue;
            }
            points.Add(new PointCommand(pointNumber, position, null, IsOnRoute: false));
        }

        if (points.Count == 0)
        {
            LogShuntingAreaWithoutPoints(number);
            return;
        }
        areas.Add(new ShuntingArea(number, parts[1], points));
    }

    #endregion

    #region Occupancy Section Parsing

    /// <summary>
    /// Parses one line of the [OccupancySections] section: <c>number:from,to  @address</c>.
    /// The address is the detector's feedback address and may be left out while the stretch
    /// is not wired to anything. A <c>!</c> after the number marks a stretch that holds its
    /// occupied indication until an operator frees it by hand.
    /// </summary>
    private void ParseOccupancySectionLine(string line, List<OccupancySection> sections)
    {
        var atIndex = line.IndexOf('@');
        var sectionPart = (atIndex >= 0 ? line[..atIndex] : line).Trim();
        var addressPart = atIndex >= 0 ? line[(atIndex + 1)..].Trim() : null;

        var match = OccupancySectionPattern().Match(sectionPart);
        if (!match.Success)
        {
            LogMalformedOccupancySectionLine(line);
            return;
        }

        if (!int.TryParse(match.Groups[1].Value, out var number) || number <= 0)
        {
            LogInvalidOccupancySectionNumber(line);
            return;
        }

        var requiresManualRelease = match.Groups[2].Value.Length > 0;
        var from = GridCoordinate.Parse(match.Groups[3].Value);
        var to = GridCoordinate.Parse(match.Groups[4].Value);

        int? address = null;
        if (!string.IsNullOrEmpty(addressPart))
        {
            if (int.TryParse(addressPart, out var parsed) && parsed > 0) address = parsed;
            else LogInvalidOccupancySectionAddress(addressPart, number);
        }

        // A hold answers a detector that has fallen silent. With no detector there is nothing to
        // answer, and the stretch would latch the moment anything reported it free, never to let
        // go — so the flag is dropped rather than kept as a trap.
        if (requiresManualRelease && address is null)
        {
            LogOccupancySectionHoldWithoutAddress(number);
            requiresManualRelease = false;
        }

        sections.Add(new OccupancySection(number, from, to, address, requiresManualRelease));
    }

    /// <summary>
    /// A detection stretch must run between two coordinates that exist in the graph, and no two
    /// stretches may carry the same number or the same feedback address, since the number
    /// identifies the section and the address is what the hardware reports it on.
    /// <para>
    /// A stretch an operator frees by hand must also have its track to itself. Stock is left
    /// standing on the tracks trains stand on, not in a throat — and a throat is shared by every
    /// stretch running over it, so holding one of them would hold rails a movement is still using.
    /// </para>
    /// </summary>
    private void ValidateOccupancySections(YardTopology topology, List<OccupancySection> sections)
    {
        var graph = topology.Graph;
        var numbers = new HashSet<int>();
        var addresses = new HashSet<int>();
        var tracks = sections.Any(section => section.HoldsOccupancy)
            ? topology.MapOccupancySectionTracks(sections)
            : null;

        foreach (var section in sections)
        {
            if (!numbers.Add(section.Number))
                LogDuplicateOccupancySectionNumber(section.Number);

            if (section.HasAddress && !addresses.Add(section.Address!.Value))
                LogDuplicateOccupancySectionAddress(section.Address!.Value, section.Number);

            if (graph.GetNode(section.From) is null)
                LogOccupancySectionCoordinateNotInGraph(section.Number, section.From);
            if (graph.GetNode(section.To) is null)
                LogOccupancySectionCoordinateNotInGraph(section.Number, section.To);

            if (section.HoldsOccupancy
                && tracks!.TryGetValue(section.Number, out var track)
                && !track.IsExclusive)
                LogOccupancySectionHoldOnSharedTrack(section.Number);
        }

        // A signal every stretch runs past is a signal nothing can say a train stands at, and the
        // usual reason is a list written before the signal was added or moved: the stretches are
        // deduced once and then kept, so the file goes on describing the yard as it used to be.
        if (sections.Count > 0)
        {
            var ends = sections.SelectMany(section => new[] { section.From, section.To }).ToHashSet();
            foreach (var signal in topology.Signals)
            {
                if (graph.GetNode(signal.Coordinate) is null) continue;
                if (!ends.Contains(signal.Coordinate))
                    LogSignalWithoutOccupancySection(signal.Name, signal.Coordinate);
            }
        }
    }

    #endregion

    #region Settings Parsing

    private static void ParseSettingsLine(
        string line,
        ref int lockAddressOffset,
        ref int lockReleaseDelaySeconds)
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
                LogRouteNotDerived(fromSignal, toSignal);
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
                    LogRouteNotDerivedUsingFlankOnly(fromSignal, toSignal);
                    SetSwitchCoordinatesFromPath(flankPoints, fromSignal, toSignal, graph, pointDefinitions, signalDefinitions);
                    commands.Add(new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, flankPoints) { Address = routeAddress });
                }
            }
            else
            {
                // Fully manual (backward compatible)
                var pointCommands = pointPositions.Select(pp => pp.ToPointCommand()).ToList();
                SetSwitchCoordinatesFromPath(pointCommands, fromSignal, toSignal, graph, pointDefinitions, signalDefinitions);
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

    /// <summary>
    /// Computes the route path and sets the correct SwitchCoordinate on each point command.
    /// For paired points (e.g., 1a/1b), the path determines which sub-point the route passes through.
    /// </summary>
    private static void SetSwitchCoordinatesFromPath(
        List<PointCommand> pointCommands,
        int fromSignalNumber,
        int toSignalNumber,
        TrackGraph graph,
        IReadOnlyList<PointDefinition> pointDefinitions,
        IReadOnlyList<SignalDefinition> signalDefinitions)
    {
        var fromSignal = signalDefinitions.FirstOrDefault(s => s.Name == fromSignalNumber.ToString(CultureInfo.InvariantCulture));
        var toSignal = signalDefinitions.FirstOrDefault(s => s.Name == toSignalNumber.ToString(CultureInfo.InvariantCulture));
        if (fromSignal is null || toSignal is null) return;

        var onRoutePoints = pointCommands.Where(pc => pc.IsOnRoute).ToList();
        var path = graph.FindRoutePath(fromSignal.Coordinate, toSignal.Coordinate, fromSignal.DrivesRight, pointDefinitions, onRoutePoints);
        if (path.Count == 0) return;

        var pathCoords = new HashSet<GridCoordinate>();
        foreach (var link in path)
        {
            pathCoords.Add(link.FromNode.Coordinate);
            pathCoords.Add(link.ToNode.Coordinate);
        }

        var pointsByNumber = TrackGraphExtensions.BuildPointNumberMapping(pointDefinitions);

        for (var i = 0; i < pointCommands.Count; i++)
        {
            var cmd = pointCommands[i];
            if (!pointsByNumber.TryGetValue(cmd.Number, out var defs)) continue;

            // Find the definition whose switch coordinate is on the path
            var matchingDef = defs.FirstOrDefault(d => pathCoords.Contains(d.SwitchPoint));
            if (matchingDef is not null)
                pointCommands[i] = cmd with { SwitchCoordinate = matchingDef.SwitchPoint };
            else if (defs.Count == 1)
                pointCommands[i] = cmd with { SwitchCoordinate = defs[0].SwitchPoint };
        }
    }

    private IReadOnlyList<PointCommand>? DeriveRoutePoints(
        int fromSignalNumber,
        int toSignalNumber,
        TrackGraph graph,
        IReadOnlyList<PointDefinition> pointDefinitions,
        IReadOnlyList<SignalDefinition> signalDefinitions)
    {
        var fromSignal = signalDefinitions.FirstOrDefault(s => s.Name == fromSignalNumber.ToString(CultureInfo.InvariantCulture));
        var toSignal = signalDefinitions.FirstOrDefault(s => s.Name == toSignalNumber.ToString(CultureInfo.InvariantCulture));

        if (fromSignal is null || toSignal is null)
        {
            LogRouteSignalNotFound(fromSignalNumber, toSignalNumber);
            return null;
        }

        var path = graph.FindRoutePath(fromSignal.Coordinate, toSignal.Coordinate, fromSignal.DrivesRight, pointDefinitions);
        if (path.Count == 0)
        {
            LogNoPathForRoute(fromSignalNumber, toSignalNumber);
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
        "s" => SignalType.BufferStop,
        "f" => SignalType.Fictitious,
        "g" => SignalType.FictitiousShunting,
        _ => SignalType.Default,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Track link {From}-{To} has decreasing column values.")]
    private partial void LogDecreasingTrackLink(GridCoordinate from, GridCoordinate to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed shunting area line: {Line}")]
    private partial void LogMalformedShuntingAreaLine(string line);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid shunting area number in line: {Line}")]
    private partial void LogInvalidShuntingAreaNumber(string line);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid point '{Token}' in shunting area {Area}")]
    private partial void LogInvalidShuntingAreaPoint(string token, int area);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shunting area {Area} has no points")]
    private partial void LogShuntingAreaWithoutPoints(int area);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed crossing line: {Line}")]
    private partial void LogMalformedCrossingLine(string line);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid point position '{Token}' in crossing at {Coordinate}")]
    private partial void LogInvalidCrossingPosition(string token, GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crossing at {Coordinate} is not on any track")]
    private partial void LogCrossingNotOnTrack(GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crossing at {Coordinate} has {Degree} arms, not four")]
    private partial void LogCrossingWithoutFourArms(GridCoordinate coordinate, int degree);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crossing at {Coordinate} declares {Count} ways through it, not two")]
    private partial void LogCrossingWayCount(GridCoordinate coordinate, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Point {Point} of the crossing at {Coordinate} is not a point of this station")]
    private partial void LogCrossingPointNotFound(int point, GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Way {Point} of the crossing at {Coordinate} reaches it by {Count} arms, not two")]
    private partial void LogCrossingWayArmCount(int point, GridCoordinate coordinate, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Arm {Arm} of the crossing at {Coordinate} is claimed by both way {First} and way {Second}")]
    private partial void LogCrossingArmClaimedTwice(TrackLink arm, GridCoordinate coordinate, int first, int second);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Arm {Arm} of the crossing at {Coordinate} belongs to no declared way through it")]
    private partial void LogCrossingArmWithoutWay(TrackLink arm, GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Route {From}-{To} sets point {Point} against the crossing at {Coordinate}")]
    private partial void LogRouteContradictsCrossing(int from, int to, int point, GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed platform line: {Line}")]
    private partial void LogMalformedPlatformLine(string line);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Platform ends at {Coordinate}, which is not on any track")]
    private partial void LogPlatformCoordinateNotInGraph(GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Platform at {Coordinate} starts and ends at the same coordinate")]
    private partial void LogPlatformWithoutLength(GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Platform {From}-{To} spans more than one row")]
    private partial void LogPlatformSpansRows(GridCoordinate from, GridCoordinate to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed occupancy section line: {Line}")]
    private partial void LogMalformedOccupancySectionLine(string line);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid occupancy section number in line: {Line}")]
    private partial void LogInvalidOccupancySectionNumber(string line);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid feedback address '{Address}' for occupancy section {Section}")]
    private partial void LogInvalidOccupancySectionAddress(string address, int section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Occupancy section number {Section} is declared more than once")]
    private partial void LogDuplicateOccupancySectionNumber(int section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feedback address {Address} is used by more than one occupancy section, last is {Section}")]
    private partial void LogDuplicateOccupancySectionAddress(int address, int section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Signal '{Signal}' at {Coordinate} has no occupancy section ending there, so nothing can say where a train standing at it is; the [OccupancySections] no longer match the signals")]
    private partial void LogSignalWithoutOccupancySection(string signal, GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Occupancy section {Section} ends at {Coordinate}, which is not on any track")]
    private partial void LogOccupancySectionCoordinateNotInGraph(int section, GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Occupancy section {Section} is marked for manual release but has no feedback address; the mark is ignored because a stretch with no detector never reports free on its own")]
    private partial void LogOccupancySectionHoldWithoutAddress(int section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Occupancy section {Section} is marked for manual release but shares its track with another stretch; stock is left standing on the tracks trains stand on, not in a throat")]
    private partial void LogOccupancySectionHoldOnSharedTrack(int section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not auto-derive route {From}-{To}")]
    private partial void LogRouteNotDerived(int from, int to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not auto-derive route {From}-{To}, using flank points only")]
    private partial void LogRouteNotDerivedUsingFlankOnly(int from, int to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot auto-derive route: signal {From} or {To} not found in topology")]
    private partial void LogRouteSignalNotFound(int from, int to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No path found for auto-derived route {From}-{To}")]
    private partial void LogNoPathForRoute(int from, int to);


    /// <summary>
    /// The command pad's two sets of buttons, from the [Buttons] section. A set header names the
    /// set and the four rows after it are its keys, left to right, four to a row, spelled by the
    /// icon each carries:
    /// <code>
    /// [Buttons]
    /// Main:
    /// point-straight, point-diverging, signal-stop, signal-go
    /// route-train-set, route-train-cancel, route-shunting-set, route-shunting-release
    /// train-approved, train-advance, -, -
    /// area-lock, area-release, -, buttons-alternate
    /// Alternate:
    /// point-lock, point-unlock, signal-lock-stop, signal-unlock-stop
    /// -, -, -, buttons-main
    /// </code>
    /// A dash is a key with nothing on it, and a set may stop short — the keys not named are
    /// blank. A section with nothing in it at all gives the built-in arrangement of §5.1 and §5.2,
    /// so a panel works at a station whose file has never mentioned one.
    /// </summary>
    private static ButtonLayout ParseButtonLines(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return ButtonLayout.Default;

        List<string> main = [];
        List<string> alternate = [];
        var target = main;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            if (trimmed.EndsWith(':'))
            {
                var set = trimmed[..^1].Trim();
                target = set.Equals("alternate", StringComparison.OrdinalIgnoreCase) ? alternate : main;
                continue;
            }

            target.AddRange(trimmed.Split(',', StringSplitOptions.TrimEntries));
        }

        return main.Count == 0 && alternate.Count == 0
            ? ButtonLayout.Default
            : ButtonLayout.FromIconNames(main, alternate);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Buffer stop '{Name}' at {Coordinate} is not at the end of a track.")]
    private partial void LogBufferStopNotAtTrackEnd(string name, GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Buffer stop '{Name}' at {Coordinate} is at the edge of the layout, where the track leaves the station.")]
    private partial void LogBufferStopAtLayoutEdge(string name, GridCoordinate coordinate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Buffer stop '{Name}' at {Coordinate} faces away from the track leading up to it.")]
    private partial void LogBufferStopFacesAwayFromTrack(string name, GridCoordinate coordinate);
}
