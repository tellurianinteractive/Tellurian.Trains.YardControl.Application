using Tellurian.Trains.YardController.Model;

namespace YardController.Tests;

[TestClass]
public class TopologyAnalysisTests
{
    private static readonly string TopologyPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.Web", "Data", "Topology.txt");

    [TestMethod]
    public async Task FindSignalsAtMissingCoordinates()
    {
        var parser = new TopologyParser();
        var topology = await parser.ParseFileAsync(Path.GetFullPath(TopologyPath));

        Console.WriteLine($"Topology '{topology.Name}'");
        Console.WriteLine($"Total track nodes: {topology.Graph.Nodes.Count}");
        Console.WriteLine($"Total signals: {topology.Signals.Count}");
        Console.WriteLine();

        var missingSignals = new List<(SignalDefinition Signal, string Issue)>();

        foreach (var signal in topology.Signals)
        {
            var node = topology.Graph.GetNode(signal.Coordinate);
            if (node == null)
            {
                missingSignals.Add((signal, "Coordinate not in track graph"));
            }
        }

        if (missingSignals.Count > 0)
        {
            Console.WriteLine($"=== SIGNALS AT MISSING COORDINATES ({missingSignals.Count}) ===");
            foreach (var (signal, issue) in missingSignals.OrderBy(s => s.Signal.Coordinate.Row).ThenBy(s => s.Signal.Coordinate.Column))
            {
                // Find nearby coordinates that DO exist
                var nearby = FindNearbyExistingCoordinates(topology.Graph, signal.Coordinate);
                Console.WriteLine($"  Signal {signal.Name} at {signal.Coordinate} - {issue}");
                if (nearby.Count > 0)
                {
                    Console.WriteLine($"    Nearby existing nodes: {string.Join(", ", nearby)}");
                }
            }
        }
        else
        {
            Console.WriteLine("All signals are at valid track coordinates.");
        }

        Console.WriteLine();
        Console.WriteLine($"=== SIGNALS AT VALID COORDINATES ({topology.Signals.Count - missingSignals.Count}) ===");
        foreach (var signal in topology.Signals.Where(s => topology.Graph.GetNode(s.Coordinate) != null)
            .OrderBy(s => s.Coordinate.Row).ThenBy(s => s.Coordinate.Column))
        {
            Console.WriteLine($"  Signal {signal.Name} at {signal.Coordinate} - OK");
        }
    }

    [TestMethod]
    public async Task FindPointsAtMissingCoordinates()
    {
        var parser = new TopologyParser();
        var topology = await parser.ParseFileAsync(Path.GetFullPath(TopologyPath));

        Console.WriteLine($"Topology '{topology.Name}'");
        Console.WriteLine($"Total track nodes: {topology.Graph.Nodes.Count}");
        Console.WriteLine($"Total points: {topology.Points.Count}");
        Console.WriteLine();

        var missingPoints = new List<(PointDefinition Point, string Issue)>();

        foreach (var point in topology.Points)
        {
            var switchNode = topology.Graph.GetNode(point.SwitchPoint);
            var divergingNode = topology.Graph.GetNode(point.ExplicitEnd);

            if (switchNode == null)
            {
                missingPoints.Add((point, $"SwitchPoint {point.SwitchPoint} not in track graph"));
            }
            if (divergingNode == null)
            {
                missingPoints.Add((point, $"ExplicitEnd {point.ExplicitEnd} not in track graph"));
            }
            if (switchNode != null && divergingNode != null)
            {
                // Check if there's actually a link between them
                var link = topology.Graph.GetLink(point.SwitchPoint, point.ExplicitEnd);
                if (link == null)
                {
                    missingPoints.Add((point, $"No link between {point.SwitchPoint} and {point.ExplicitEnd}"));
                }
            }
        }

        if (missingPoints.Count > 0)
        {
            Console.WriteLine($"=== POINTS WITH ISSUES ({missingPoints.Count}) ===");
            foreach (var (point, issue) in missingPoints.OrderBy(p => p.Point.Label))
            {
                Console.WriteLine($"  Point {point.Label}: {issue}");
            }
        }
        else
        {
            Console.WriteLine("All points are at valid track coordinates with valid links.");
        }
    }

    [TestMethod]
    public async Task ListAllTrackNodes()
    {
        var parser = new TopologyParser();
        var topology = await parser.ParseFileAsync(Path.GetFullPath(TopologyPath));

        Console.WriteLine($"=== ALL TRACK NODES ({topology.Graph.Nodes.Count}) ===");

        var nodesByRow = topology.Graph.Nodes.Keys
            .GroupBy(c => c.Row)
            .OrderBy(g => g.Key);

        foreach (var rowGroup in nodesByRow)
        {
            var columns = string.Join(", ", rowGroup.OrderBy(c => c.Column).Select(c => c.Column));
            Console.WriteLine($"  Row {rowGroup.Key}: columns [{columns}]");
        }
    }

    [TestMethod]
    public async Task FindHiddenSignals()
    {
        var parser = new TopologyParser();
        var topology = await parser.ParseFileAsync(Path.GetFullPath(TopologyPath));

        var hiddenSignals = topology.Signals.Where(s => s.IsHidden).ToList();
        var visibleSignals = topology.Signals.Where(s => !s.IsHidden).ToList();

        Console.WriteLine($"Total signals: {topology.Signals.Count}");
        Console.WriteLine($"Hidden signals: {hiddenSignals.Count}");
        Console.WriteLine($"Visible signals: {visibleSignals.Count}");
        Console.WriteLine();

        if (hiddenSignals.Count > 0)
        {
            Console.WriteLine("=== HIDDEN SIGNALS ===");
            foreach (var signal in hiddenSignals.OrderBy(s => s.Name))
            {
                var direction = signal.DrivesRight ? ">" : "<";
                Console.WriteLine($"  Signal {signal.Name} at {signal.Coordinate} ({direction}) - HIDDEN");
            }
        }

        // Verify expected hidden signals are parsed correctly
        Assert.IsTrue(hiddenSignals.Any(s => s.Name == "10" && s.Coordinate == new GridCoordinate(7, 9) && s.IsHidden),
            "Signal 10 at 7.9 should be hidden");
        Assert.IsTrue(hiddenSignals.Any(s => s.Name == "11" && s.Coordinate == new GridCoordinate(7, 23) && s.IsHidden),
            "Signal 11 at 7.23 should be hidden");
    }

    [TestMethod]
    public void ParseSignal_HiddenMarker_SetsIsHidden()
    {
        var parser = new TopologyParser();

        // Test hidden signal with right direction
        var topology1 = parser.Parse("TestStation\n[Features]\n6.9:10>:x");
        Assert.AreEqual(1, topology1.Signals.Count);
        Assert.AreEqual("10", topology1.Signals[0].Name);
        Assert.AreEqual(new GridCoordinate(6, 9), topology1.Signals[0].Coordinate);
        Assert.IsTrue(topology1.Signals[0].DrivesRight);
        Assert.IsTrue(topology1.Signals[0].IsHidden);

        // Test hidden signal with left direction
        var topology2 = parser.Parse("TestStation\n[Features]\n7.20:<11:x");
        Assert.AreEqual(1, topology2.Signals.Count);
        Assert.AreEqual("11", topology2.Signals[0].Name);
        Assert.AreEqual(new GridCoordinate(7, 20), topology2.Signals[0].Coordinate);
        Assert.IsFalse(topology2.Signals[0].DrivesRight);
        Assert.IsTrue(topology2.Signals[0].IsHidden);

        // Test visible signal (no x marker)
        var topology3 = parser.Parse("TestStation\n[Features]\n1.11:<67:");
        Assert.AreEqual(1, topology3.Signals.Count);
        Assert.AreEqual("67", topology3.Signals[0].Name);
        Assert.IsFalse(topology3.Signals[0].IsHidden);
    }

    [TestMethod]
    public async Task FindUnnecessaryCoordinates()
    {
        var parser = new TopologyParser();
        var topology = await parser.ParseFileAsync(Path.GetFullPath(TopologyPath));

        Console.WriteLine($"Topology '{topology.Name}'");
        Console.WriteLine($"Total nodes: {topology.Graph.Nodes.Count}");
        Console.WriteLine();

        // Collect all coordinates referenced by features
        var featureCoords = new HashSet<GridCoordinate>();

        // Points: switch point and diverging end
        foreach (var point in topology.Points)
        {
            featureCoords.Add(point.SwitchPoint);
            featureCoords.Add(point.ExplicitEnd);
        }

        // Signals
        foreach (var signal in topology.Signals)
        {
            featureCoords.Add(signal.Coordinate);
        }

        // Gaps
        foreach (var gap in topology.Gaps)
        {
            featureCoords.Add(gap.Coordinate);
            if (gap.LinkEnd.HasValue)
                featureCoords.Add(gap.LinkEnd.Value);
        }

        // Labels
        foreach (var label in topology.Labels)
        {
            featureCoords.Add(label.Start);
            featureCoords.Add(label.End);
        }

        Console.WriteLine($"Feature coordinates: {featureCoords.Count}");
        Console.WriteLine($"Forced necessary coordinates: {topology.ForcedNecessaryCoordinates.Count}");

        // Find necessary coordinates: ends (degree 1), junctions (degree > 2), have features, or forced necessary
        var necessaryCoords = new HashSet<GridCoordinate>();
        var unnecessaryCoords = new List<GridCoordinate>();

        foreach (var (coord, node) in topology.Graph.Nodes)
        {
            var isEnd = node.Degree == 1;
            var isJunction = node.Degree > 2;
            var hasFeature = featureCoords.Contains(coord);
            var isForcedNecessary = topology.ForcedNecessaryCoordinates.Contains(coord);

            if (isEnd || isJunction || hasFeature || isForcedNecessary)
            {
                necessaryCoords.Add(coord);
            }
            else
            {
                unnecessaryCoords.Add(coord);
            }
        }

        Console.WriteLine($"Necessary coordinates: {necessaryCoords.Count}");
        Console.WriteLine($"Unnecessary coordinates (can be removed): {unnecessaryCoords.Count}");
        Console.WriteLine();

        if (unnecessaryCoords.Count > 0)
        {
            Console.WriteLine("=== UNNECESSARY COORDINATES (degree 2, no features) ===");
            foreach (var coord in unnecessaryCoords.OrderBy(c => c.Row).ThenBy(c => c.Column))
            {
                var node = topology.Graph.GetNode(coord)!;
                var neighbors = topology.Graph.GetAdjacentCoordinates(coord).ToList();
                Console.WriteLine($"  {coord} (degree {node.Degree}) - neighbors: {string.Join(", ", neighbors)}");
            }
        }

        // Now read the original file and produce a simplified version
        Console.WriteLine();
        Console.WriteLine("=== SIMPLIFIED TOPOLOGY.TXT ===");
        Console.WriteLine();

        var originalContent = await File.ReadAllTextAsync(Path.GetFullPath(TopologyPath));
        var lines = originalContent.Split('\n');
        var inTracksSection = false;
        var inFeaturesSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Check for section headers
            if (line.Trim().StartsWith("[Tracks]", StringComparison.OrdinalIgnoreCase))
            {
                inTracksSection = true;
                inFeaturesSection = false;
                Console.WriteLine(line);
                continue;
            }
            if (line.Trim().StartsWith("[Features]", StringComparison.OrdinalIgnoreCase))
            {
                inTracksSection = false;
                inFeaturesSection = true;
                Console.WriteLine(line);
                continue;
            }
            if (line.Trim().StartsWith("["))
            {
                inTracksSection = false;
                inFeaturesSection = false;
                Console.WriteLine(line);
                continue;
            }

            // Pass through comments, empty lines, and non-track sections
            if (!inTracksSection || string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("'"))
            {
                Console.WriteLine(line);
                continue;
            }

            // For track lines, filter out unnecessary coordinates
            var coordPattern = new System.Text.RegularExpressions.Regex(@"\d+\.\d+");
            var matches = coordPattern.Matches(line);
            var filteredCoords = new List<string>();

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var coord = GridCoordinate.Parse(match.Value);
                if (necessaryCoords.Contains(coord))
                {
                    filteredCoords.Add(match.Value);
                }
            }

            if (filteredCoords.Count >= 2)
            {
                Console.WriteLine(string.Join("-", filteredCoords));
            }
            else if (filteredCoords.Count == 1)
            {
                // Single coordinate after filtering - still output for context
                Console.WriteLine(string.Join("-", filteredCoords));
            }
            // Skip if no coordinates left
        }
    }

    private static List<GridCoordinate> TracePath(
        TrackGraph graph,
        GridCoordinate start,
        GridCoordinate nextCoord,
        HashSet<GridCoordinate> necessaryCoords,
        HashSet<(GridCoordinate, GridCoordinate)> processedLinks)
    {
        var path = new List<GridCoordinate> { start };
        var current = nextCoord;
        var previous = start;

        while (true)
        {
            // Mark this link as processed
            var linkKey = previous < current ? (previous, current) : (current, previous);
            processedLinks.Add(linkKey);

            path.Add(current);

            // If we've reached a necessary coordinate, stop
            if (necessaryCoords.Contains(current))
            {
                break;
            }

            // Find the next coordinate (not the one we came from)
            var currentNode = graph.GetNode(current);
            if (currentNode == null) break;

            GridCoordinate? nextNext = null;
            foreach (var link in currentNode.OutgoingLinks)
            {
                if (link.ToNode.Coordinate != previous)
                {
                    nextNext = link.ToNode.Coordinate;
                    break;
                }
            }
            if (nextNext == null)
            {
                foreach (var link in currentNode.IncomingLinks)
                {
                    if (link.FromNode.Coordinate != previous)
                    {
                        nextNext = link.FromNode.Coordinate;
                        break;
                    }
                }
            }

            if (nextNext == null) break;

            previous = current;
            current = nextNext.Value;
        }

        return path;
    }

    [TestMethod]
    [DataRow(92, 64)]
    [DataRow(92, 84)]
    [DataRow(92, 80)]
    [DataRow(90, 80)]
    [DataRow(22, 84)]
    [DataRow(22, 10)]
    [DataRow(92, 10)]
    public async Task VerifySignalReachability(int from, int to)
    {
        var parser = new TopologyParser();
        var topology = await parser.ParseFileAsync(Path.GetFullPath(TopologyPath));

        var fromSignal = topology.Signals.First(s => s.Name == from.ToString());
        var toSignal = topology.Signals.First(s => s.Name == to.ToString());
        var path = topology.Graph.FindRoutePath(fromSignal.Coordinate, toSignal.Coordinate, fromSignal.DrivesRight);

        Assert.IsTrue(path.Count > 0,
            $"Signal {from} at {fromSignal.Coordinate} cannot reach signal {to} at {toSignal.Coordinate} via directed BFS (drivesForward={fromSignal.DrivesRight})");
    }

    private static List<GridCoordinate> FindNearbyExistingCoordinates(TrackGraph graph, GridCoordinate target)
    {
        var nearby = new List<GridCoordinate>();

        // Check coordinates within 1 step
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                var candidate = new GridCoordinate(target.Row + dr, target.Column + dc);
                if (graph.GetNode(candidate) != null)
                {
                    nearby.Add(candidate);
                }
            }
        }

        return nearby;
    }

    [TestMethod]
    public void ParseSinglePoint_WithPlusSuffix_SetsExplicitEndIsStraight()
    {
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n4.24-4.26-4.28\n7.23-4.26\n[Features]\n4.26(<11a)-7.23+");

        Assert.AreEqual(1, topology.Points.Count);
        var point = topology.Points[0];
        Assert.AreEqual("11a", point.Label);
        Assert.AreEqual(new GridCoordinate(4, 26), point.SwitchPoint);
        Assert.AreEqual(new GridCoordinate(7, 23), point.ExplicitEnd);
        Assert.IsTrue(point.ExplicitEndIsStraight);
        Assert.AreEqual(DivergeDirection.Backward, point.Direction);
    }

    [TestMethod]
    public void ParseSinglePoint_WithoutPlusSuffix_ExplicitEndIsStraightIsFalse()
    {
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n4.24-4.26-4.28\n7.23-4.26\n[Features]\n4.26(<11a)-7.23");

        Assert.AreEqual(1, topology.Points.Count);
        Assert.IsFalse(topology.Points[0].ExplicitEndIsStraight);
    }

    [TestMethod]
    public void ParsePairedPoints_WithPlusSuffix_SetsBothExplicitEndIsStraight()
    {
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n2.0-2.2-2.4\n4.0-4.2-4.4\n2.2-4.2\n[Features]\n2.2(1a>)-4.2(<1b)+");

        Assert.AreEqual(2, topology.Points.Count);
        Assert.IsTrue(topology.Points[0].ExplicitEndIsStraight);
        Assert.IsTrue(topology.Points[1].ExplicitEndIsStraight);
    }

    [TestMethod]
    public void DeduceStraightArm_WhenExplicitEndIsStraight_ReturnsExplicitEnd()
    {
        var parser = new TopologyParser();
        // Build a small topology: switch at 2.2 with links to 2.0 (left), 2.4 (right), and 4.2 (diagonal)
        // Mark 4.2 as straight (+), so DeduceStraightArm should return 4.2
        var topology = parser.Parse("Test\n[Tracks]\n2.0-2.2-2.4\n4.2-2.2\n[Features]\n2.2(1>)-4.2+");

        var point = topology.Points[0];
        Assert.IsTrue(point.ExplicitEndIsStraight);

        var straight = topology.Graph.DeduceStraightArm(point);
        Assert.AreEqual(new GridCoordinate(4, 2), straight);
    }

    [TestMethod]
    public void DeduceDivergingEnd_WhenExplicitEndIsStraight_DeducesFromGraph()
    {
        var parser = new TopologyParser();
        // Switch at 2.2 with links to 2.0, 2.4, and 4.2
        // Explicit end is 4.2 marked as straight (+)
        // So diverging end should be deduced from the graph (2.0 or 2.4)
        var topology = parser.Parse("Test\n[Tracks]\n2.0-2.2-2.4\n4.2-2.2\n[Features]\n2.2(1>)-4.2+");

        var point = topology.Points[0];
        var diverging = topology.Graph.DeduceDivergingEnd(point);

        // Should not be the explicit end (4.2)
        Assert.AreNotEqual(new GridCoordinate(4, 2), diverging);
        // Should be one of the other connected nodes
        Assert.IsTrue(
            diverging == new GridCoordinate(2, 0) || diverging == new GridCoordinate(2, 4),
            $"Expected 2.0 or 2.4 but got {diverging}");
    }

    [TestMethod]
    public void DeduceDivergingEnd_WhenExplicitEndIsNotStraight_ReturnsExplicitEnd()
    {
        var parser = new TopologyParser();
        var topology = parser.Parse("Test\n[Tracks]\n2.0-2.2-2.4\n4.2-2.2\n[Features]\n2.2(1>)-4.2");

        var point = topology.Points[0];
        Assert.IsFalse(point.ExplicitEndIsStraight);

        var diverging = topology.Graph.DeduceDivergingEnd(point);
        Assert.AreEqual(new GridCoordinate(4, 2), diverging);
    }
}
