using Tellurian.Trains.YardController.Model;

namespace YardController.Tests;

[TestClass]
public class TopologyAnalysisTests
{
    private static readonly string TopologyPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.App", "Data", "Topology.txt");

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
            var divergingNode = topology.Graph.GetNode(point.DivergingEnd);

            if (switchNode == null)
            {
                missingPoints.Add((point, $"SwitchPoint {point.SwitchPoint} not in track graph"));
            }
            if (divergingNode == null)
            {
                missingPoints.Add((point, $"DivergingEnd {point.DivergingEnd} not in track graph"));
            }
            if (switchNode != null && divergingNode != null)
            {
                // Check if there's actually a link between them
                var link = topology.Graph.GetLink(point.SwitchPoint, point.DivergingEnd);
                if (link == null)
                {
                    missingPoints.Add((point, $"No link between {point.SwitchPoint} and {point.DivergingEnd}"));
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
        Assert.IsTrue(hiddenSignals.Any(s => s.Name == "10" && s.Coordinate == new GridCoordinate(6, 9) && s.IsHidden),
            "Signal 10 at 6.9 should be hidden");
        Assert.IsTrue(hiddenSignals.Any(s => s.Name == "11" && s.Coordinate == new GridCoordinate(7, 20) && s.IsHidden),
            "Signal 11 at 7.20 should be hidden");
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
}
