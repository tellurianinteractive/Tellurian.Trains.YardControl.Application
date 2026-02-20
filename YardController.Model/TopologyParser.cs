using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Tellurian.Trains.YardController.Model;

public partial class TopologyParser
{
    private readonly ILogger? _logger;

    public TopologyParser(ILogger? logger = null)
    {
        _logger = logger;
    }
    // Regex patterns for parsing topology elements
    [GeneratedRegex(@"(\d+\.\d+)")]
    private static partial Regex CoordinatePattern();

    // Single point pattern: coord(label>)-coord or coord(<label)-coord, with optional + suffix
    // Supports both orders: label then direction, or direction then label
    [GeneratedRegex(@"(\d+\.\d+)\(([<>]?)([A-Za-z0-9]+)([<>]?)\)-(\d+\.\d+)(\+)?(?!\()")]
    private static partial Regex SinglePointPattern();

    // Paired points pattern: coord(label>)-coord(<label) - crossover with shared diverging link, optional + suffix
    // Supports both orders for each point
    [GeneratedRegex(@"(\d+\.\d+)\(([<>]?)([A-Za-z0-9]+)([<>]?)\)-(\d+\.\d+)\(([<>]?)([A-Za-z0-9]+)([<>]?)\)(\+)?")]
    private static partial Regex PairedPointPattern();

    // Label pattern: coord[text]coord
    [GeneratedRegex(@"(\d+\.\d+)\[([^\]]+)\](\d+\.\d+)")]
    private static partial Regex LabelPattern();

    // Signal pattern: coord:name>: or coord:<name: with optional type suffix (x=hidden, u=outbound, i=inbound, h=main dwarf, d=shunting dwarf)
    [GeneratedRegex(@"(\d+\.\d+):([<]?)([A-Za-z]?\d+)([>]?):([a-z])?")]
    private static partial Regex SignalPattern();

    // Gap patterns: coord| (node gap) or coord|coord (link gap)
    [GeneratedRegex(@"(\d+\.\d+)\|(\d+\.\d+)?")]
    private static partial Regex GapPattern();

    // Section header pattern
    [GeneratedRegex(@"^\[(\w+)\]$")]
    private static partial Regex SectionPattern();

    public YardTopology Parse(string content)
    {
        var graph = new TrackGraph();
        var points = new List<PointDefinition>();
        var signals = new List<SignalDefinition>();
        var labels = new List<LabelDefinition>();
        var gaps = new List<GapDefinition>();
        var forcedNecessary = new HashSet<GridCoordinate>();

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var currentSection = "";
        var name = "";
        var isFirstLine = true;

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
                currentSection = sectionMatch.Groups[1].Value.ToLowerInvariant();
                continue;
            }

            // Parse based on current section
            switch (currentSection)
            {
                case "tracks":
                    ParseTrackLine(line, graph, forcedNecessary);
                    break;
                case "features":
                    ParseFeatureLine(line, graph, points, signals, labels, gaps);
                    break;
                default:
                    // Backwards compatibility: treat lines without section as tracks
                    ParseTrackLine(line, graph, forcedNecessary);
                    break;
            }
        }

        ValidateFeatureCoordinates(graph, signals, points, gaps);

        return new YardTopology(name, graph, points, signals, labels, gaps, forcedNecessary);
    }

    private void ValidateFeatureCoordinates(
        TrackGraph graph,
        List<SignalDefinition> signals,
        List<PointDefinition> points,
        List<GapDefinition> gaps)
    {
        foreach (var signal in signals)
        {
            if (graph.GetNode(signal.Coordinate) is null)
                _logger?.LogWarning("Signal '{Name}' at {Coordinate} is not at a track node.", signal.Name, signal.Coordinate);
        }

        foreach (var point in points)
        {
            if (graph.GetNode(point.SwitchPoint) is null)
                _logger?.LogWarning("Point '{Label}' switch at {Coordinate} is not at a track node.", point.Label, point.SwitchPoint);
            if (graph.GetNode(point.ExplicitEnd) is null)
                _logger?.LogWarning("Point '{Label}' explicit end at {Coordinate} is not at a track node.", point.Label, point.ExplicitEnd);
        }

        foreach (var gap in gaps)
        {
            if (graph.GetNode(gap.Coordinate) is null)
                _logger?.LogWarning("Gap at {Coordinate} is not at a track node.", gap.Coordinate);
            if (gap.LinkEnd.HasValue && graph.GetNode(gap.LinkEnd.Value) is null)
                _logger?.LogWarning("Gap link end at {Coordinate} is not at a track node.", gap.LinkEnd.Value);
        }
    }

    // Coordinate with optional ! suffix pattern
    [GeneratedRegex(@"(\d+\.\d+)(!)?")]
    private static partial Regex CoordinateWithMarkerPattern();

    private void ParseTrackLine(string line, TrackGraph graph, HashSet<GridCoordinate> forcedNecessary)
    {
        // Track line format: coord-coord-coord-coord...
        // Example: 2.0-2.2-2.4-2.6-2.8
        // Coordinates can have ! suffix to mark as forced necessary: 6.24!
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
                        "Track link {From}-{To} has decreasing column values. All links should have increasing columns for correct forward direction.",
                        previousCoord.Value, coord);
                }
                graph.TryAddLink(previousCoord.Value, coord);
            }

            previousCoord = coord;
        }
    }

    private static void ParseFeatureLine(
        string line,
        TrackGraph graph,
        List<PointDefinition> points,
        List<SignalDefinition> signals,
        List<LabelDefinition> labels,
        List<GapDefinition> gaps)
    {
        // Try to parse as each feature type

        // Gaps: coord| or coord|coord
        var gapMatch = GapPattern().Match(line);
        if (gapMatch.Success && !line.Contains('(') && !line.Contains('[') && !line.Contains(':'))
        {
            var coord = GridCoordinate.Parse(gapMatch.Groups[1].Value);
            GridCoordinate? linkEnd = null;

            if (gapMatch.Groups[2].Success && !string.IsNullOrEmpty(gapMatch.Groups[2].Value))
            {
                linkEnd = GridCoordinate.Parse(gapMatch.Groups[2].Value);

                // Mark the link as having a gap
                var link = graph.GetLink(coord, linkEnd.Value);
                if (link != null)
                {
                    link.HasGap = true;
                }
            }

            gaps.Add(new GapDefinition(coord, linkEnd));
            return;
        }

        // Paired points (crossover): coord(label>)-coord(<label) - both share the diverging link
        var pairedMatch = PairedPointPattern().Match(line);
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

            // Direction is whichever group is non-empty
            var direction1 = !string.IsNullOrEmpty(dirBefore1) ? dirBefore1 : dirAfter1;
            var direction2 = !string.IsNullOrEmpty(dirBefore2) ? dirBefore2 : dirAfter2;

            // First point: at coord1, explicit end at coord2
            var dir1 = direction1 == ">" ? DivergeDirection.Forward : DivergeDirection.Backward;
            points.Add(new PointDefinition(label1, coord1, coord2, dir1, explicitEndIsStraight));

            // Second point: at coord2, explicit end at coord1
            var dir2 = direction2 == ">" ? DivergeDirection.Forward : DivergeDirection.Backward;
            points.Add(new PointDefinition(label2, coord2, coord1, dir2, explicitEndIsStraight));

            return;
        }

        // Single point: coord(label>)-coord or coord(<label)-coord
        var singlePointMatch = SinglePointPattern().Match(line);
        if (singlePointMatch.Success)
        {
            var switchPoint = GridCoordinate.Parse(singlePointMatch.Groups[1].Value);
            var dirBefore = singlePointMatch.Groups[2].Value;
            var label = singlePointMatch.Groups[3].Value;
            var dirAfter = singlePointMatch.Groups[4].Value;
            var explicitEnd = GridCoordinate.Parse(singlePointMatch.Groups[5].Value);
            var explicitEndIsStraight = singlePointMatch.Groups[6].Success;

            // Direction is whichever group is non-empty
            var direction = !string.IsNullOrEmpty(dirBefore) ? dirBefore : dirAfter;
            var dir = direction == ">" ? DivergeDirection.Forward : DivergeDirection.Backward;
            points.Add(new PointDefinition(label, switchPoint, explicitEnd, dir, explicitEndIsStraight));
            return;
        }

        // Labels: coord[text]coord
        var labelMatch = LabelPattern().Match(line);
        if (labelMatch.Success)
        {
            var start = GridCoordinate.Parse(labelMatch.Groups[1].Value);
            var text = labelMatch.Groups[2].Value;
            var end = GridCoordinate.Parse(labelMatch.Groups[3].Value);

            labels.Add(new LabelDefinition(text, start, end));
            return;
        }

        // Signals: coord:name>: or coord:<name: with optional :x suffix
        var signalMatch = SignalPattern().Match(line);
        if (signalMatch.Success)
        {
            var coord = GridCoordinate.Parse(signalMatch.Groups[1].Value);
            var leftArrow = signalMatch.Groups[2].Value;
            var name = signalMatch.Groups[3].Value;
            var rightArrow = signalMatch.Groups[4].Value;
            var typeMarker = signalMatch.Groups[5].Value;

            var drivesRight = !string.IsNullOrEmpty(rightArrow);
            var signalType = ParseSignalType(typeMarker);

            signals.Add(new SignalDefinition(name, coord, drivesRight, signalType));
        }
    }

    public async Task<YardTopology> ParseFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return YardTopology.Empty;
        }

        var content = await File.ReadAllTextAsync(filePath);
        return Parse(content);
    }

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
