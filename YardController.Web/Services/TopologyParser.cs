using System.Text.RegularExpressions;
using YardController.Web.Models;

namespace YardController.Web.Services;

public partial class TopologyParser
{
    // Regex patterns for parsing topology elements
    [GeneratedRegex(@"(\d+\.\d+)")]
    private static partial Regex CoordinatePattern();

    // Single point pattern: coord(label>)-coord or coord(<label)-coord or coord(>label)-coord or coord(<label)-coord
    // Supports both orders: label then direction, or direction then label
    [GeneratedRegex(@"(\d+\.\d+)\(([<>]?)([A-Za-z0-9]+)([<>]?)\)-(\d+\.\d+)(?!\()")]
    private static partial Regex SinglePointPattern();

    // Paired points pattern: coord(label>)-coord(<label) - crossover with shared diverging link
    // Supports both orders for each point
    [GeneratedRegex(@"(\d+\.\d+)\(([<>]?)([A-Za-z0-9]+)([<>]?)\)-(\d+\.\d+)\(([<>]?)([A-Za-z0-9]+)([<>]?)\)")]
    private static partial Regex PairedPointPattern();

    // Label pattern: coord[text]coord
    [GeneratedRegex(@"(\d+\.\d+)\[([^\]]+)\](\d+\.\d+)")]
    private static partial Regex LabelPattern();

    // Signal pattern: coord:name>: or coord:<name:
    [GeneratedRegex(@"(\d+\.\d+):([<]?)([A-Za-z]?\d+)([>]?):")]
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
                    ParseTrackLine(line, graph);
                    break;
                case "features":
                    ParseFeatureLine(line, graph, points, signals, labels, gaps);
                    break;
                default:
                    // Backwards compatibility: treat lines without section as tracks
                    ParseTrackLine(line, graph);
                    break;
            }
        }

        return new YardTopology(name, graph, points, signals, labels, gaps);
    }

    private static void ParseTrackLine(string line, TrackGraph graph)
    {
        // Track line format: coord-coord-coord-coord...
        // Example: 2.0-2.2-2.4-2.6-2.8
        var coordMatches = CoordinatePattern().Matches(line);

        GridCoordinate? previousCoord = null;

        foreach (Match match in coordMatches)
        {
            var coord = GridCoordinate.Parse(match.Value);

            if (previousCoord.HasValue)
            {
                // Add link between previous and current coordinate
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

            // Direction is whichever group is non-empty
            var direction1 = !string.IsNullOrEmpty(dirBefore1) ? dirBefore1 : dirAfter1;
            var direction2 = !string.IsNullOrEmpty(dirBefore2) ? dirBefore2 : dirAfter2;

            // First point: at coord1, diverging to coord2
            var dir1 = direction1 == ">" ? DivergeDirection.Forward : DivergeDirection.Backward;
            points.Add(new PointDefinition(label1, coord1, coord2, dir1));

            // Second point: at coord2, diverging to coord1
            var dir2 = direction2 == ">" ? DivergeDirection.Forward : DivergeDirection.Backward;
            points.Add(new PointDefinition(label2, coord2, coord1, dir2));

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
            var divergingEnd = GridCoordinate.Parse(singlePointMatch.Groups[5].Value);

            // Direction is whichever group is non-empty
            var direction = !string.IsNullOrEmpty(dirBefore) ? dirBefore : dirAfter;
            var dir = direction == ">" ? DivergeDirection.Forward : DivergeDirection.Backward;
            points.Add(new PointDefinition(label, switchPoint, divergingEnd, dir));
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

        // Signals: coord:name>: or coord:<name:
        var signalMatch = SignalPattern().Match(line);
        if (signalMatch.Success)
        {
            var coord = GridCoordinate.Parse(signalMatch.Groups[1].Value);
            var leftArrow = signalMatch.Groups[2].Value;
            var name = signalMatch.Groups[3].Value;
            var rightArrow = signalMatch.Groups[4].Value;

            var drivesRight = !string.IsNullOrEmpty(rightArrow);

            signals.Add(new SignalDefinition(name, coord, drivesRight));
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
}
