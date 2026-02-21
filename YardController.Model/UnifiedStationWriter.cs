using System.Text;
using Tellurian.Trains.YardController.Model.Control;

namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// Serializes a UnifiedStationData to the unified Station.txt format.
/// Produces human-readable text with simplified track coordinates.
/// </summary>
public class UnifiedStationWriter
{
    public string Write(UnifiedStationData data)
    {
        var sb = new StringBuilder();

        sb.AppendLine(data.Name);
        sb.AppendLine();

        WriteTranslations(sb, data.Translations);
        WriteSettings(sb, data.LockAddressOffset, data.LockReleaseDelaySeconds);
        WriteTracks(sb, data.Topology.Graph, data.Topology.ForcedNecessaryCoordinates);
        WritePoints(sb, data.Topology.Points, data.Points);
        WriteSignals(sb, data.Topology.Signals, data.SignalAddresses);
        WriteLabels(sb, data.Topology.Labels);
        WriteGaps(sb, data.Topology.Gaps);
        WriteTurntable(sb, data.TurntableTracks);
        WriteRoutes(sb, data.TrainRoutes);

        return sb.ToString();
    }

    private static void WriteTranslations(StringBuilder sb, LabelTranslationData? translations)
    {
        if (translations is null || translations.Languages.Length == 0) return;

        sb.AppendLine("[Translations]");
        sb.AppendLine(string.Join(';', translations.Languages));
        foreach (var row in translations.Rows)
            sb.AppendLine(string.Join(';', row));
        sb.AppendLine();
    }

    private static void WriteSettings(StringBuilder sb, int lockAddressOffset, int lockReleaseDelaySeconds)
    {
        if (lockAddressOffset == 0 && lockReleaseDelaySeconds == 0) return;

        sb.AppendLine("[Settings]");
        if (lockAddressOffset > 0)
            sb.AppendLine($"LockOffset:{lockAddressOffset}");
        if (lockReleaseDelaySeconds > 0)
            sb.AppendLine($"LockReleaseDelay:{lockReleaseDelaySeconds}");
        sb.AppendLine();
    }

    private static void WriteTracks(StringBuilder sb, TrackGraph graph, IReadOnlySet<GridCoordinate> forcedNecessary)
    {
        sb.AppendLine("[Tracks]");

        // Find all track chains and write them with simplified coordinates
        var chains = FindTrackChains(graph);

        foreach (var chain in chains)
        {
            var simplified = SimplifyChain(chain, forcedNecessary);
            var parts = simplified.Select(c =>
                forcedNecessary.Contains(c) ? $"{c}!" : c.ToString());
            sb.AppendLine(string.Join('-', parts));
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Finds maximal chains of connected nodes in the graph.
    /// A chain starts at a node with degree != 2 (endpoint or junction) or a node that
    /// has already been visited by another chain.
    /// </summary>
    private static List<List<GridCoordinate>> FindTrackChains(TrackGraph graph)
    {
        var chains = new List<List<GridCoordinate>>();
        var visitedLinks = new HashSet<(GridCoordinate, GridCoordinate)>();

        // Process links sorted by coordinate order
        var allLinks = graph.Links
            .OrderBy(l => l.FromNode.Coordinate)
            .ToList();

        foreach (var link in allLinks)
        {
            var from = link.FromNode.Coordinate;
            var to = link.ToNode.Coordinate;
            var key = (from, to);
            var reverseKey = (to, from);

            if (visitedLinks.Contains(key) || visitedLinks.Contains(reverseKey))
                continue;

            // Start a new chain from this link
            var chain = new List<GridCoordinate> { from, to };
            visitedLinks.Add(key);

            // Extend forward from 'to'
            ExtendChain(chain, graph, visitedLinks, forward: true);

            // Extend backward from 'from'
            ExtendChainBackward(chain, graph, visitedLinks);

            chains.Add(chain);
        }

        return chains;
    }

    private static void ExtendChain(
        List<GridCoordinate> chain,
        TrackGraph graph,
        HashSet<(GridCoordinate, GridCoordinate)> visitedLinks,
        bool forward)
    {
        while (true)
        {
            var current = chain[^1];
            var node = graph.GetNode(current);
            if (node is null) break;

            // Find an unvisited outgoing link
            TrackLink? nextLink = null;
            foreach (var link in node.OutgoingLinks)
            {
                var key = (link.FromNode.Coordinate, link.ToNode.Coordinate);
                var reverseKey = (link.ToNode.Coordinate, link.FromNode.Coordinate);
                if (!visitedLinks.Contains(key) && !visitedLinks.Contains(reverseKey))
                {
                    // Only continue the chain if the current node has exactly one unvisited outgoing link
                    // and the next node doesn't create an ambiguous junction
                    if (nextLink is not null)
                    {
                        nextLink = null; // Multiple options - stop
                        break;
                    }
                    nextLink = link;
                }
            }

            if (nextLink is null) break;

            visitedLinks.Add((nextLink.FromNode.Coordinate, nextLink.ToNode.Coordinate));
            chain.Add(nextLink.ToNode.Coordinate);
        }
    }

    private static void ExtendChainBackward(
        List<GridCoordinate> chain,
        TrackGraph graph,
        HashSet<(GridCoordinate, GridCoordinate)> visitedLinks)
    {
        while (true)
        {
            var current = chain[0];
            var node = graph.GetNode(current);
            if (node is null) break;

            TrackLink? prevLink = null;
            foreach (var link in node.IncomingLinks)
            {
                var key = (link.FromNode.Coordinate, link.ToNode.Coordinate);
                var reverseKey = (link.ToNode.Coordinate, link.FromNode.Coordinate);
                if (!visitedLinks.Contains(key) && !visitedLinks.Contains(reverseKey))
                {
                    if (prevLink is not null)
                    {
                        prevLink = null;
                        break;
                    }
                    prevLink = link;
                }
            }

            if (prevLink is null) break;

            visitedLinks.Add((prevLink.FromNode.Coordinate, prevLink.ToNode.Coordinate));
            chain.Insert(0, prevLink.FromNode.Coordinate);
        }
    }

    /// <summary>
    /// Simplifies a chain by removing intermediate same-row coordinates that can be auto-filled.
    /// Keeps: endpoints, bend points (row changes), forced necessary coordinates, and coordinates
    /// used by features (points, signals, etc.) - but since we don't know features here,
    /// we only skip coordinates on straight same-row runs.
    /// </summary>
    private static List<GridCoordinate> SimplifyChain(
        List<GridCoordinate> chain,
        IReadOnlySet<GridCoordinate> forcedNecessary)
    {
        if (chain.Count <= 2) return chain;

        var result = new List<GridCoordinate> { chain[0] };

        for (int i = 1; i < chain.Count - 1; i++)
        {
            var prev = chain[i - 1];
            var curr = chain[i];
            var next = chain[i + 1];

            // Keep if forced necessary
            if (forcedNecessary.Contains(curr))
            {
                result.Add(curr);
                continue;
            }

            // Keep if row changes (bend point)
            if (prev.Row != curr.Row || curr.Row != next.Row)
            {
                result.Add(curr);
                continue;
            }

            // Same row before and after - can be auto-filled, skip it
            // But only if columns are sequential (no gaps)
            var prevToCurrentStep = curr.Column - prev.Column;
            var currentToNextStep = next.Column - curr.Column;
            if (Math.Abs(prevToCurrentStep) != 1 || Math.Abs(currentToNextStep) != 1)
            {
                // Non-sequential columns - keep this coordinate
                result.Add(curr);
                continue;
            }

            // Can be auto-filled - skip
        }

        result.Add(chain[^1]);
        return result;
    }

    private static void WritePoints(
        StringBuilder sb,
        IReadOnlyList<PointDefinition> pointDefs,
        IReadOnlyList<Point> points)
    {
        if (pointDefs.Count == 0) return;

        sb.AppendLine("[Points]");

        var pointsByNumber = points
            .Where(p => !p.IsAddressOnly)
            .ToDictionary(p => p.Number);

        // Group paired points
        var written = new HashSet<string>();

        foreach (var def in pointDefs)
        {
            if (written.Contains(def.Label)) continue;

            // Check if this is part of a pair
            var pair = pointDefs.FirstOrDefault(d =>
                d != def &&
                d.SwitchPoint == def.ExplicitEnd &&
                d.ExplicitEnd == def.SwitchPoint);

            if (pair is not null && !written.Contains(pair.Label))
            {
                // Paired points
                written.Add(def.Label);
                written.Add(pair.Label);

                var line = FormatPointDef(def) + "-" + FormatPointCoord(pair);
                if (pair.ExplicitEndIsStraight)
                    line += "+";

                var number = ExtractPointNumber(def.Label);
                if (pointsByNumber.TryGetValue(number, out var point))
                    line += $"  @{FormatAddresses(point)}";

                sb.AppendLine(line);
            }
            else
            {
                // Single point
                written.Add(def.Label);

                var line = FormatPointDef(def) + $"-{def.ExplicitEnd}";
                if (def.ExplicitEndIsStraight)
                    line += "+";

                var number = ExtractPointNumber(def.Label);
                if (pointsByNumber.TryGetValue(number, out var point))
                    line += $"  @{FormatAddresses(point)}";

                sb.AppendLine(line);
            }
        }

        sb.AppendLine();
    }

    private static string FormatPointDef(PointDefinition def)
    {
        var dirChar = def.Direction == DivergeDirection.Forward ? ">" : "<";
        if (def.Direction == DivergeDirection.Backward)
            return $"{def.SwitchPoint}({dirChar}{def.Label})";
        return $"{def.SwitchPoint}({def.Label}{dirChar})";
    }

    private static string FormatPointCoord(PointDefinition def)
    {
        var dirChar = def.Direction == DivergeDirection.Forward ? ">" : "<";
        if (def.Direction == DivergeDirection.Backward)
            return $"{def.SwitchPoint}({dirChar}{def.Label})";
        return $"{def.SwitchPoint}({def.Label}{dirChar})";
    }

    private static string FormatAddresses(Point point)
    {
        if (point.SubPointMap is not null && point.SubPointMap.Count > 0)
        {
            // Has sub-points - check if grouped
            if (!point.StraightAddresses.SequenceEqual(point.DivergingAddresses))
            {
                var straight = FormatAddressesWithSubPoints(point.StraightAddresses, point.SubPointMap);
                var diverging = FormatAddressesWithSubPoints(point.DivergingAddresses, point.SubPointMap);
                return $"({straight})+({diverging})-";
            }
            return FormatAddressesWithSubPoints(point.StraightAddresses, point.SubPointMap);
        }

        if (!point.StraightAddresses.SequenceEqual(point.DivergingAddresses))
        {
            var straight = string.Join(',', point.StraightAddresses);
            var diverging = string.Join(',', point.DivergingAddresses);
            return $"({straight})+({diverging})-";
        }

        return string.Join(',', point.StraightAddresses);
    }

    private static string FormatAddressesWithSubPoints(int[] addresses, IReadOnlyDictionary<int, char> subPointMap)
    {
        return string.Join(',', addresses.Select(a =>
        {
            var abs = Math.Abs(a);
            var prefix = a < 0 ? "-" : "";
            var suffix = subPointMap.TryGetValue(abs, out var sp) ? sp.ToString() : "";
            return $"{prefix}{abs}{suffix}";
        }));
    }

    private static void WriteSignals(
        StringBuilder sb,
        IReadOnlyList<SignalDefinition> signalDefs,
        IReadOnlyList<SignalHardware> signalAddresses)
    {
        if (signalDefs.Count == 0) return;

        sb.AppendLine("[Signals]");

        var addressMap = signalAddresses.ToDictionary(s => s.SignalName);

        foreach (var def in signalDefs)
        {
            var dirChar = def.DrivesRight ? ">" : "<";
            var typeChar = def.Type switch
            {
                SignalType.Hidden => "x",
                SignalType.OutboundMain => "u",
                SignalType.InboundMain => "i",
                SignalType.MainDwarf => "h",
                SignalType.ShuntingDwarf => "d",
                _ => ""
            };

            string line;
            if (def.DrivesRight)
            {
                var labelPart = def.Label is not null ? $"[{def.Label}]" : "";
                line = $"{def.Coordinate}:{def.Name}{labelPart}{dirChar}:{typeChar}";
            }
            else
            {
                var labelPart = def.Label is not null ? $"[{def.Label}]" : "";
                line = $"{def.Coordinate}:{dirChar}{def.Name}{labelPart}:{typeChar}";
            }

            if (addressMap.TryGetValue(def.Name, out var hw))
            {
                line += $"  @{hw.Address}";
                if (hw.FeedbackAddress.HasValue)
                    line += $";{hw.FeedbackAddress.Value}";
            }

            sb.AppendLine(line);
        }

        sb.AppendLine();
    }

    private static void WriteLabels(StringBuilder sb, IReadOnlyList<LabelDefinition> labels)
    {
        if (labels.Count == 0) return;

        sb.AppendLine("[Labels]");
        foreach (var label in labels)
            sb.AppendLine($"{label.Start}[{label.Text}]{label.End}");
        sb.AppendLine();
    }

    private static void WriteGaps(StringBuilder sb, IReadOnlyList<GapDefinition> gaps)
    {
        if (gaps.Count == 0) return;

        sb.AppendLine("[Gaps]");
        foreach (var gap in gaps)
        {
            if (gap.LinkEnd.HasValue)
                sb.AppendLine($"{gap.Coordinate}|{gap.LinkEnd.Value}");
            else
                sb.AppendLine($"{gap.Coordinate}|");
        }
        sb.AppendLine();
    }

    private static void WriteTurntable(StringBuilder sb, IReadOnlyList<TurntableTrack> turntableTracks)
    {
        if (turntableTracks.Count == 0) return;

        sb.AppendLine("[Turntable]");

        var first = turntableTracks.OrderBy(t => t.Number).First();
        var last = turntableTracks.OrderBy(t => t.Number).Last();
        var offset = first.Address - first.Number;

        sb.AppendLine($"Tracks:{first.Number}-{last.Number}");
        sb.AppendLine($"Offset:{offset}");
        sb.AppendLine();
    }


    private static void WriteRoutes(StringBuilder sb, IReadOnlyList<TrainRouteCommand> routes)
    {
        if (routes.Count == 0) return;

        sb.AppendLine("[Routes]");

        foreach (var route in routes)
        {
            var addressSuffix = route.HasAddress ? $"  @{route.Address}" : "";
            if (route.IntermediateSignals.Count > 0)
            {
                // Composite route
                var via = new List<int> { route.FromSignal };
                via.AddRange(route.IntermediateSignals);
                via.Add(route.ToSignal);
                sb.AppendLine($"{route.FromSignal}-{route.ToSignal}:{string.Join('.', via)}{addressSuffix}");
            }
            else
            {
                var pointParts = route.PointCommands.Select(p =>
                {
                    var prefix = p.IsOnRoute ? "" : "x";
                    var posChar = p.Position == PointPosition.Straight ? "+" : "-";
                    return $"{prefix}{p.Number}{posChar}";
                });
                sb.AppendLine($"{route.FromSignal}-{route.ToSignal}:{string.Join(',', pointParts)}{addressSuffix}");
            }
        }
    }

    private static int ExtractPointNumber(string label)
    {
        var digits = new string(label.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : 0;
    }
}
