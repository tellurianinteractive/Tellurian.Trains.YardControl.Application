using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Tests;

[TestClass]
public class UnifiedStationParserTests
{
    private readonly UnifiedStationParser _parser = new();

    #region Track Auto-Fill Tests

    [TestMethod]
    public void ParseTracks_SameRow_AutoFillsIntermediateCoordinates()
    {
        var content = """
            TestStation

            [Tracks]
            2.5-2.10
            """;

        var data = _parser.Parse(content);
        var graph = data.Topology.Graph;

        // Should create 6 nodes: 2.5, 2.6, 2.7, 2.8, 2.9, 2.10
        for (int col = 5; col <= 10; col++)
        {
            Assert.IsNotNull(graph.GetNode(new GridCoordinate(2, col)),
                $"Node at 2.{col} should exist");
        }

        // Should create 5 links
        for (int col = 5; col < 10; col++)
        {
            var link = graph.GetLink(new GridCoordinate(2, col), new GridCoordinate(2, col + 1));
            Assert.IsNotNull(link, $"Link from 2.{col} to 2.{col + 1} should exist");
        }
    }

    [TestMethod]
    public void ParseTracks_DifferentRows_CreatesSingleLink()
    {
        var content = """
            TestStation

            [Tracks]
            2.5-1.6
            """;

        var data = _parser.Parse(content);
        var graph = data.Topology.Graph;

        Assert.IsNotNull(graph.GetNode(new GridCoordinate(2, 5)));
        Assert.IsNotNull(graph.GetNode(new GridCoordinate(1, 6)));
        Assert.IsNotNull(graph.GetLink(new GridCoordinate(2, 5), new GridCoordinate(1, 6)));

        // Should NOT auto-fill intermediate coordinates
        Assert.IsNull(graph.GetNode(new GridCoordinate(2, 6)));
    }

    [TestMethod]
    public void ParseTracks_MixedRowsAndSameRow_HandledCorrectly()
    {
        var content = """
            TestStation

            [Tracks]
            2.5-1.6-1.10
            """;

        var data = _parser.Parse(content);
        var graph = data.Topology.Graph;

        // 2.5 to 1.6 = diagonal, single link
        Assert.IsNotNull(graph.GetLink(new GridCoordinate(2, 5), new GridCoordinate(1, 6)));

        // 1.6 to 1.10 = same row, auto-fill
        for (int col = 6; col <= 10; col++)
        {
            Assert.IsNotNull(graph.GetNode(new GridCoordinate(1, col)),
                $"Node at 1.{col} should exist");
        }
    }

    [TestMethod]
    public void ParseTracks_ForcedNecessaryCoordinate()
    {
        var content = """
            TestStation

            [Tracks]
            2.5-2.10!
            """;

        var data = _parser.Parse(content);
        Assert.IsTrue(data.Topology.ForcedNecessaryCoordinates.Contains(new GridCoordinate(2, 10)));
    }

    #endregion

    #region Settings Tests

    [TestMethod]
    public void ParseSettings_LockOffsetAndReleaseDelay()
    {
        var content = """
            TestStation

            [Settings]
            LockOffset:1000
            LockReleaseDelay:30
            """;

        var data = _parser.Parse(content);
        Assert.AreEqual(1000, data.LockAddressOffset);
        Assert.AreEqual(30, data.LockReleaseDelaySeconds);
    }

    #endregion

    #region Point Tests

    [TestMethod]
    public void ParsePoints_SinglePointWithAddress()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.5
            3.0-3.5
            2.5-3.4

            [Points]
            2.5(<1)-3.4  @842
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.Topology.Points.Count);
        var pointDef = data.Topology.Points[0];
        Assert.AreEqual("1", pointDef.Label);
        Assert.AreEqual(new GridCoordinate(2, 5), pointDef.SwitchPoint);

        Assert.AreEqual(1, data.Points.Count);
        var point = data.Points[0];
        Assert.AreEqual(1, point.Number);
        Assert.AreEqual(842, point.StraightAddresses[0]);
    }

    [TestMethod]
    public void ParsePoints_PairedPointsWithAddresses()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.5
            3.0-3.5
            2.5-3.4
            4.0-4.5
            3.4-4.3

            [Points]
            2.5(<1a)-3.4(1b>)  @840a,843b
            """;

        var data = _parser.Parse(content);

        // Should create 2 point definitions (paired crossover)
        Assert.AreEqual(2, data.Topology.Points.Count);
        Assert.AreEqual("1a", data.Topology.Points[0].Label);
        Assert.AreEqual("1b", data.Topology.Points[1].Label);

        // But only 1 Point (same number)
        Assert.AreEqual(1, data.Points.Count);
        Assert.AreEqual(1, data.Points[0].Number);
    }

    [TestMethod]
    public void ParsePoints_GroupedAddresses()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.10
            3.0-3.10
            2.5-3.4
            1.0-1.10
            2.5-1.6

            [Points]
            2.5(<9a)-1.6(9b>)  @(830a,-836b)+(830a,-836b,835,837)-
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.Points.Count);
        var point = data.Points[0];
        Assert.AreEqual(9, point.Number);
        Assert.IsTrue(point.StraightAddresses.Length > 0);
        Assert.IsTrue(point.DivergingAddresses.Length > 0);
    }

    #endregion

    #region Signal Tests

    [TestMethod]
    public void ParseSignals_WithAndWithoutAddress()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.10

            [Signals]
            2.5:<21:i  @1050
            2.8:32>:h
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(2, data.Topology.Signals.Count);

        var sig21 = data.Topology.Signals.First(s => s.Name == "21");
        Assert.IsFalse(sig21.DrivesRight);
        Assert.AreEqual(SignalType.InboundMain, sig21.Type);

        var sig32 = data.Topology.Signals.First(s => s.Name == "32");
        Assert.IsTrue(sig32.DrivesRight);
        Assert.AreEqual(SignalType.MainDwarf, sig32.Type);

        // Only signal 21 has an address
        Assert.AreEqual(1, data.SignalAddresses.Count);
        Assert.AreEqual("21", data.SignalAddresses[0].SignalName);
        Assert.AreEqual(1050, data.SignalAddresses[0].Address);
    }

    [TestMethod]
    public void ParseSignals_WithFeedbackAddress()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.20

            [Signals]
            2.15:32>:h  @1051;1060
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.SignalAddresses.Count);
        Assert.AreEqual(1051, data.SignalAddresses[0].Address);
        Assert.AreEqual(1060, data.SignalAddresses[0].FeedbackAddress);
    }

    [TestMethod]
    public void ParseSignals_HiddenSignal()
    {
        var content = """
            TestStation

            [Tracks]
            7.0-7.25

            [Signals]
            7.23:<11:x
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.Topology.Signals.Count);
        Assert.IsTrue(data.Topology.Signals[0].IsHidden);
    }

    #endregion

    #region Label Tests

    [TestMethod]
    public void ParseLabels()
    {
        var content = """
            TestStation

            [Tracks]
            1.20-1.25

            [Labels]
            1.21[Spar 1a]1.22
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.Topology.Labels.Count);
        Assert.AreEqual("Spar 1a", data.Topology.Labels[0].Text);
    }

    #endregion

    #region Gap Tests

    [TestMethod]
    public void ParseGaps()
    {
        var content = """
            TestStation

            [Tracks]
            1.5-1.10

            [Gaps]
            1.5|1.6
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.Topology.Gaps.Count);
        Assert.AreEqual(new GridCoordinate(1, 5), data.Topology.Gaps[0].Coordinate);
        Assert.AreEqual(new GridCoordinate(1, 6), data.Topology.Gaps[0].LinkEnd);
    }

    #endregion

    #region Translation Tests

    [TestMethod]
    public void ParseTranslations()
    {
        var content = """
            TestStation

            [Translations]
            en;sv
            Track;Spar

            [Tracks]
            1.0-1.5
            """;

        var data = _parser.Parse(content);

        Assert.IsNotNull(data.Translations);
        Assert.AreEqual(2, data.Translations.Languages.Length);
        Assert.AreEqual("en", data.Translations.Languages[0]);
        Assert.AreEqual("sv", data.Translations.Languages[1]);
        Assert.AreEqual(1, data.Translations.Rows.Count);
        Assert.AreEqual("Track", data.Translations.Rows[0][0]);
        Assert.AreEqual("Spar", data.Translations.Rows[0][1]);
    }

    #endregion

    #region Turntable Tests

    [TestMethod]
    public void ParseTurntable()
    {
        var content = """
            TestStation

            [Tracks]
            1.0-1.5

            [Turntable]
            Tracks:1-3
            Offset:196
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(3, data.TurntableTracks.Count);
        Assert.AreEqual(1, data.TurntableTracks[0].Number);
        Assert.AreEqual(197, data.TurntableTracks[0].Address);
        Assert.AreEqual(2, data.TurntableTracks[1].Number);
        Assert.AreEqual(198, data.TurntableTracks[1].Address);
        Assert.AreEqual(3, data.TurntableTracks[2].Number);
        Assert.AreEqual(199, data.TurntableTracks[2].Address);
    }

    #endregion

    #region Route Tests

    [TestMethod]
    public void ParseRoutes_ManualRoute()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.30
            3.0-3.30

            [Signals]
            2.5:<21:i
            2.20:<31:h

            [Routes]
            21-31:1+,3+,7+
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.TrainRoutes.Count);
        var route = data.TrainRoutes[0];
        Assert.AreEqual(21, route.FromSignal);
        Assert.AreEqual(31, route.ToSignal);
        Assert.AreEqual(3, route.PointCommands.Count());
    }

    [TestMethod]
    public void ParseRoutes_ManualRouteWithFlankProtection()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.30

            [Signals]
            2.5:<35:h
            2.20:<41:u

            [Routes]
            35-41:x25+,27+,4+,2+
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.TrainRoutes.Count);
        var route = data.TrainRoutes[0];
        Assert.AreEqual(4, route.PointCommands.Count());
        // x25+ should have IsOnRoute = false
        var flankPoint = route.PointCommands.First(p => p.Number == 25);
        Assert.IsFalse(flankPoint.IsOnRoute);
        // Others should have IsOnRoute = true
        Assert.IsTrue(route.PointCommands.First(p => p.Number == 27).IsOnRoute);
    }

    [TestMethod]
    public void ParseRoutes_CompositeRoute()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.30

            [Signals]
            2.5:<21:i
            2.15:<31:h
            2.25:<35:h

            [Routes]
            21-31:1+,3+
            31-35:16+,19+
            21-35:21.31.35
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(3, data.TrainRoutes.Count);
        var composite = data.TrainRoutes[2];
        Assert.AreEqual(21, composite.FromSignal);
        Assert.AreEqual(35, composite.ToSignal);
        // Should have combined point commands from both sub-routes
        Assert.IsTrue(composite.PointCommands.Count() >= 3);
        // Should have intermediate signal 31
        Assert.AreEqual(1, composite.IntermediateSignals.Count);
        Assert.AreEqual(31, composite.IntermediateSignals[0]);
    }

    [TestMethod]
    public void ParseRoutes_ManualRouteWithAddress()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.30
            3.0-3.30

            [Signals]
            2.5:<21:i
            2.20:<31:h

            [Routes]
            21-31:1+,3+,7+  @500
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.TrainRoutes.Count);
        var route = data.TrainRoutes[0];
        Assert.AreEqual(21, route.FromSignal);
        Assert.AreEqual(31, route.ToSignal);
        Assert.IsTrue(route.HasAddress);
        Assert.AreEqual(500, route.Address);
        Assert.AreEqual(3, route.PointCommands.Count());
    }

    [TestMethod]
    public void ParseRoutes_CompositeRouteWithAddress()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.30

            [Signals]
            2.5:<21:i
            2.15:<31:h
            2.25:<35:h

            [Routes]
            21-31:1+,3+
            31-35:16+,19+
            21-35:21.31.35  @501
            """;

        var data = _parser.Parse(content);

        var composite = data.TrainRoutes[2];
        Assert.IsTrue(composite.HasAddress);
        Assert.AreEqual(501, composite.Address);
    }

    [TestMethod]
    public void ParseRoutes_RouteWithoutAddress_HasNoAddress()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.30

            [Signals]
            2.5:<21:i
            2.20:<31:h

            [Routes]
            21-31:1+,3+
            """;

        var data = _parser.Parse(content);

        var route = data.TrainRoutes[0];
        Assert.IsFalse(route.HasAddress);
        Assert.IsNull(route.Address);
    }

    #endregion

    #region Auto-Route Derivation Tests

    [TestMethod]
    public void ParseRoutes_AutoDerived_SimpleLinearPath()
    {
        // Build a simple topology: straight track with one point
        // Signal 21 drives left at 2.10, Signal 31 drives left at 2.0
        // Point 1 at 2.5 diverging to 3.4
        var content = """
            TestStation

            [Tracks]
            2.0-2.10
            3.0-3.5
            2.5-3.4

            [Points]
            2.5(<1)-3.4  @842

            [Signals]
            2.10:<21:h
            2.0:<31:h

            [Routes]
            21-31
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.TrainRoutes.Count);
        var route = data.TrainRoutes[0];
        Assert.AreEqual(21, route.FromSignal);
        Assert.AreEqual(31, route.ToSignal);
        // The route from 2.10 to 2.0 goes straight through point 1
        Assert.IsTrue(route.PointCommands.Any());
        var pointCmd = route.PointCommands.First(p => p.Number == 1);
        Assert.AreEqual(PointPosition.Straight, pointCmd.Position);
    }

    [TestMethod]
    public void ParseRoutes_AutoDerivedWithManualFlank()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.10
            3.0-3.5
            2.5-3.4

            [Points]
            2.5(<1)-3.4  @842

            [Signals]
            2.10:<21:h
            2.0:<31:h

            [Routes]
            21-31:x5+
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual(1, data.TrainRoutes.Count);
        var route = data.TrainRoutes[0];
        // Should have auto-derived on-route point + manual flank point
        Assert.IsTrue(route.PointCommands.Any(p => p.IsOnRoute));
        Assert.IsTrue(route.PointCommands.Any(p => !p.IsOnRoute && p.Number == 5));
    }

    #endregion

    #region Comments and Station Name Tests

    [TestMethod]
    public void ParseStationName()
    {
        var content = """
            Munkerod

            [Tracks]
            1.0-1.5
            """;

        var data = _parser.Parse(content);
        Assert.AreEqual("Munkerod", data.Name);
        Assert.AreEqual("Munkerod", data.Topology.Name);
    }

    [TestMethod]
    public void ParseComments_Ignored()
    {
        var content = """
            TestStation

            [Tracks]
            ' This is a comment
            2.0-2.5

            [Settings]
            ' Another comment
            LockOffset:500
            """;

        var data = _parser.Parse(content);
        Assert.AreEqual(500, data.LockAddressOffset);
        Assert.IsNotNull(data.Topology.Graph.GetNode(new GridCoordinate(2, 0)));
    }

    #endregion

    #region Converter Integration Tests

    private static readonly string DataFolder = Path.Combine(
        AppContext.BaseDirectory, "Data", "Munkeröd");

    [TestMethod]
    public async Task ConvertMunkerod_ProducesValidUnifiedFile()
    {
        var topologyPath = Path.Combine(DataFolder, "Topology.txt");
        var pointsPath = Path.Combine(DataFolder, "Points.txt");
        var trainRoutesPath = Path.Combine(DataFolder, "TrainRoutes.txt");
        var signalsPath = Path.Combine(DataFolder, "Signals.txt");
        var translationsPath = Path.Combine(DataFolder, "LabelTranslations.csv");

        var unifiedContent = await StationFileConverter.ConvertToUnifiedAsync(
            topologyPath, pointsPath, trainRoutesPath, signalsPath, translationsPath);

        Assert.IsFalse(string.IsNullOrWhiteSpace(unifiedContent));
        Console.WriteLine(unifiedContent);

        // Parse the unified content back
        var data = _parser.Parse(unifiedContent);

        Assert.AreEqual("Munkeröd", data.Name);
        Assert.IsTrue(data.Topology.Points.Count > 0, "Should have points");
        Assert.IsTrue(data.Topology.Signals.Count > 0, "Should have signals");
        Assert.IsTrue(data.TrainRoutes.Count > 0, "Should have routes");
    }

    [Ignore("Run manually to generate Munkeröd.txt")]
    [TestMethod]
    public async Task ConvertMunkerod_WriteToFile()
    {
        var topologyPath = Path.Combine(DataFolder, "Topology.txt");
        var pointsPath = Path.Combine(DataFolder, "Points.txt");
        var trainRoutesPath = Path.Combine(DataFolder, "TrainRoutes.txt");
        var signalsPath = Path.Combine(DataFolder, "Signals.txt");
        var translationsPath = Path.Combine(DataFolder, "LabelTranslations.csv");

        var unifiedContent = await StationFileConverter.ConvertToUnifiedAsync(
            topologyPath, pointsPath, trainRoutesPath, signalsPath, translationsPath);

        var outputPath = Path.GetFullPath(Path.Combine(DataFolder, "..", "..", "..", "..", "..", "..", "YardController.Web", "Data", "Munkeröd", "Munkeröd.txt"));
        await File.WriteAllTextAsync(outputPath, unifiedContent);
        Console.WriteLine($"Wrote unified file to: {outputPath}");
    }

    [TestMethod]
    public async Task ConvertMunkerod_PreservesPointCount()
    {
        var topologyPath = Path.Combine(DataFolder, "Topology.txt");
        var pointsPath = Path.Combine(DataFolder, "Points.txt");
        var trainRoutesPath = Path.Combine(DataFolder, "TrainRoutes.txt");

        // Load legacy data
        var legacyParser = new TopologyParser();
        var legacyTopology = await legacyParser.ParseFileAsync(topologyPath);

        // Convert and parse
        var unifiedContent = await StationFileConverter.ConvertToUnifiedAsync(
            topologyPath, pointsPath, trainRoutesPath);
        var data = _parser.Parse(unifiedContent);

        // Point definitions in topology should match
        Assert.AreEqual(legacyTopology.Points.Count, data.Topology.Points.Count,
            "Point definition count should match");
    }

    [TestMethod]
    public async Task ConvertMunkerod_PreservesSignalCount()
    {
        var topologyPath = Path.Combine(DataFolder, "Topology.txt");
        var pointsPath = Path.Combine(DataFolder, "Points.txt");
        var trainRoutesPath = Path.Combine(DataFolder, "TrainRoutes.txt");

        var legacyParser = new TopologyParser();
        var legacyTopology = await legacyParser.ParseFileAsync(topologyPath);

        var unifiedContent = await StationFileConverter.ConvertToUnifiedAsync(
            topologyPath, pointsPath, trainRoutesPath);
        var data = _parser.Parse(unifiedContent);

        Assert.AreEqual(legacyTopology.Signals.Count, data.Topology.Signals.Count,
            "Signal count should match");
    }

    [TestMethod]
    public async Task ConvertMunkerod_PreservesRouteCount()
    {
        var topologyPath = Path.Combine(DataFolder, "Topology.txt");
        var pointsPath = Path.Combine(DataFolder, "Points.txt");
        var trainRoutesPath = Path.Combine(DataFolder, "TrainRoutes.txt");

        var unifiedContent = await StationFileConverter.ConvertToUnifiedAsync(
            topologyPath, pointsPath, trainRoutesPath);
        var data = _parser.Parse(unifiedContent);

        // Count non-composite routes from the legacy file
        var legacyLines = await File.ReadAllLinesAsync(trainRoutesPath);
        var legacyRouteCount = legacyLines
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('\''))
            .Where(l => l.Contains(':') && l.Contains('-') && !l.StartsWith("LockReleaseDelay", StringComparison.OrdinalIgnoreCase))
            .Count();

        Assert.AreEqual(legacyRouteCount, data.TrainRoutes.Count,
            "Route count should match legacy file");
    }

    #endregion

    #region Full Integration Test

    [TestMethod]
    public void ParseFullStation_AllSections()
    {
        var content = """
            Teststation

            [Translations]
            en;sv
            Track;Spar

            [Settings]
            LockOffset:1000
            LockReleaseDelay:30

            [Tracks]
            2.0-2.10
            3.0-3.10
            2.5-3.4

            [Points]
            2.5(<1)-3.4  @842

            [Signals]
            2.1:<21:i  @1050
            2.8:32>:h  @1051;1060

            [Labels]
            2.2[Spar 2]2.3

            [Gaps]
            2.3|2.4

            [Turntable]
            Tracks:1-3
            Offset:196

            [Routes]
            21-32:1+
            """;

        var data = _parser.Parse(content);

        Assert.AreEqual("Teststation", data.Name);
        Assert.AreEqual(1000, data.LockAddressOffset);
        Assert.AreEqual(30, data.LockReleaseDelaySeconds);
        Assert.IsNotNull(data.Translations);
        Assert.AreEqual(1, data.Topology.Points.Count);
        Assert.AreEqual(1, data.Points.Count);
        Assert.AreEqual(2, data.Topology.Signals.Count);
        Assert.AreEqual(2, data.SignalAddresses.Count);
        Assert.AreEqual(1, data.Topology.Labels.Count);
        Assert.AreEqual(1, data.Topology.Gaps.Count);
        Assert.AreEqual(3, data.TurntableTracks.Count);
        Assert.AreEqual(1, data.TrainRoutes.Count);
    }

    #endregion

    #region Route Path Diagnostics

    [TestMethod]
    public void Route92_82_ShouldNotGoVia5_7()
    {
        // Load the unified station file
        var stationPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.Web", "Data", "Munkeröd", "Munkeröd.txt"));
        var content = File.ReadAllText(stationPath);
        var data = _parser.Parse(content);
        var graph = data.Topology.Graph;
        var points = data.Topology.Points;
        var signals = data.Topology.Signals;

        // Dump point definitions at switch 7.5
        Console.WriteLine("=== Point definitions at switch 7.5 ===");
        foreach (var p in points.Where(p => p.SwitchPoint == new GridCoordinate(7, 5)))
        {
            var straight = graph.DeduceStraightArm(p);
            var diverging = graph.DeduceDivergingEnd(p);
            Console.WriteLine($"  Point {p.Label}: switch={p.SwitchPoint}, explicitEnd={p.ExplicitEnd}, " +
                $"dir={p.Direction}, explicitIsStraight={p.ExplicitEndIsStraight}");
            Console.WriteLine($"    DeduceStraightArm={straight}, DeduceDivergingEnd={diverging}");
        }

        // Dump point definitions at switch 7.9
        Console.WriteLine("\n=== Point definitions at switch 7.9 ===");
        foreach (var p in points.Where(p => p.SwitchPoint == new GridCoordinate(7, 9)))
        {
            var straight = graph.DeduceStraightArm(p);
            var diverging = graph.DeduceDivergingEnd(p);
            Console.WriteLine($"  Point {p.Label}: switch={p.SwitchPoint}, explicitEnd={p.ExplicitEnd}, " +
                $"dir={p.Direction}, explicitIsStraight={p.ExplicitEndIsStraight}");
            Console.WriteLine($"    DeduceStraightArm={straight}, DeduceDivergingEnd={diverging}");
        }

        // Dump neighbors at key nodes
        Console.WriteLine("\n=== Forward neighbors at key nodes ===");
        foreach (var coord in new[] { new GridCoordinate(7, 5), new GridCoordinate(7, 6), new GridCoordinate(7, 9) })
        {
            var fwd = graph.GetDirectedAdjacentCoordinates(coord, true).ToList();
            var bwd = graph.GetDirectedAdjacentCoordinates(coord, false).ToList();
            Console.WriteLine($"  {coord}: fwd=[{string.Join(", ", fwd)}], bwd=[{string.Join(", ", bwd)}]");
        }

        // Find the composite route 92-82
        var route = data.TrainRoutes.FirstOrDefault(r => r.FromSignal == 92 && r.ToSignal == 82);
        Assert.IsNotNull(route, "Route 92-82 should exist");

        Console.WriteLine($"\nRoute 92-82 points: {string.Join(", ", route.PointCommands.Select(p => $"{p.Number}{(p.Position == PointPosition.Straight ? "+" : "-")}{(p.IsOnRoute ? "" : "(flank)")}"))}");
        Console.WriteLine($"Intermediate signals: {string.Join(", ", route.IntermediateSignals)}");

        var fromSignalDef = signals.First(s => s.Name == "92");
        var toSignalDef = signals.First(s => s.Name == "82");
        var intermediateSignal10 = signals.First(s => s.Name == "10");

        Console.WriteLine($"\nSignal 92 at {fromSignalDef.Coordinate}, drivesRight={fromSignalDef.DrivesRight}");
        Console.WriteLine($"Signal 10 at {intermediateSignal10.Coordinate}, drivesRight={intermediateSignal10.DrivesRight}");
        Console.WriteLine($"Signal 82 at {toSignalDef.Coordinate}, drivesRight={toSignalDef.DrivesRight}");

        // Test segment 1 WITHOUT route points (should find shortest path)
        var segment1NoConstraints = graph.FindRoutePath(
            fromSignalDef.Coordinate, intermediateSignal10.Coordinate,
            fromSignalDef.DrivesRight, points);
        Console.WriteLine($"\nSegment 1 (92→10) WITHOUT routePoints ({segment1NoConstraints.Count} links):");
        foreach (var link in segment1NoConstraints)
            Console.WriteLine($"  {link.FromNode.Coordinate} → {link.ToNode.Coordinate}");

        // Segment 1 WITH route points
        var segment1 = graph.FindRoutePath(
            fromSignalDef.Coordinate, intermediateSignal10.Coordinate,
            fromSignalDef.DrivesRight, points, route.PointCommands.ToList());
        Console.WriteLine($"\nSegment 1 (92→10) WITH routePoints ({segment1.Count} links):");
        foreach (var link in segment1)
            Console.WriteLine($"  {link.FromNode.Coordinate} → {link.ToNode.Coordinate}");

        // Segment 2 WITH route points
        var segment2 = graph.FindRoutePath(
            intermediateSignal10.Coordinate, toSignalDef.Coordinate,
            intermediateSignal10.DrivesRight, points, route.PointCommands.ToList());
        Console.WriteLine($"\nSegment 2 (10→82) WITH routePoints ({segment2.Count} links):");
        foreach (var link in segment2)
            Console.WriteLine($"  {link.FromNode.Coordinate} → {link.ToNode.Coordinate}");

        // Segment 2 WITHOUT route points
        var segment2NoConstraints = graph.FindRoutePath(
            intermediateSignal10.Coordinate, toSignalDef.Coordinate,
            intermediateSignal10.DrivesRight, points);
        Console.WriteLine($"\nSegment 2 (10→82) WITHOUT routePoints ({segment2NoConstraints.Count} links):");
        foreach (var link in segment2NoConstraints)
            Console.WriteLine($"  {link.FromNode.Coordinate} → {link.ToNode.Coordinate}");

        // Test with ONLY the sub-route points for each segment
        var route92_10 = data.TrainRoutes.FirstOrDefault(r => r.FromSignal == 92 && r.ToSignal == 10);
        if (route92_10 != null)
        {
            var seg1Only = graph.FindRoutePath(
                fromSignalDef.Coordinate, intermediateSignal10.Coordinate,
                fromSignalDef.DrivesRight, points, route92_10.PointCommands.ToList());
            Console.WriteLine($"\nSegment 1 with ONLY 92-10 points ({seg1Only.Count} links):");
            foreach (var link in seg1Only)
                Console.WriteLine($"  {link.FromNode.Coordinate} → {link.ToNode.Coordinate}");
        }

        var route10_82 = data.TrainRoutes.FirstOrDefault(r => r.FromSignal == 10 && r.ToSignal == 82);
        if (route10_82 != null)
        {
            var seg2Only = graph.FindRoutePath(
                intermediateSignal10.Coordinate, toSignalDef.Coordinate,
                intermediateSignal10.DrivesRight, points, route10_82.PointCommands.ToList());
            Console.WriteLine($"\nSegment 2 with ONLY 10-82 points ({seg2Only.Count} links):");
            foreach (var link in seg2Only)
                Console.WriteLine($"  {link.FromNode.Coordinate} → {link.ToNode.Coordinate}");
        }

        // Isolate which route point causes the issue for segment 1
        Console.WriteLine("\n=== Isolating problematic route point for segment 1 ===");
        var basePoints = new List<PointCommand>(); // no route points -> correct
        foreach (var cmd in route92_10?.PointCommands ?? [])
        {
            basePoints.Add(cmd);
            var testPath = graph.FindRoutePath(
                fromSignalDef.Coordinate, intermediateSignal10.Coordinate,
                fromSignalDef.DrivesRight, points, basePoints);
            var coords = new HashSet<GridCoordinate>();
            foreach (var link in testPath)
            {
                coords.Add(link.FromNode.Coordinate);
                coords.Add(link.ToNode.Coordinate);
            }
            var via57 = coords.Contains(new GridCoordinate(5, 7));
            Console.WriteLine($"  After adding {cmd.Number}{(cmd.Position == PointPosition.Straight ? "+" : "-")}: " +
                $"links={testPath.Count}, via5.7={via57}");
        }

        // Isolate which route point breaks segment 2
        Console.WriteLine("\n=== Isolating problematic route point for segment 2 ===");
        var seg2Points = new List<PointCommand>(route10_82?.PointCommands ?? []);
        foreach (var cmd in route92_10?.PointCommands ?? [])
        {
            seg2Points.Add(cmd);
            var testPath = graph.FindRoutePath(
                intermediateSignal10.Coordinate, toSignalDef.Coordinate,
                intermediateSignal10.DrivesRight, points, seg2Points);
            Console.WriteLine($"  After adding {cmd.Number}{(cmd.Position == PointPosition.Straight ? "+" : "-")}: " +
                $"links={testPath.Count}");
        }

        // Verify segment 1 does NOT go through 5.7
        var segment1Coords = new HashSet<GridCoordinate>();
        foreach (var link in segment1)
        {
            segment1Coords.Add(link.FromNode.Coordinate);
            segment1Coords.Add(link.ToNode.Coordinate);
        }

        Assert.IsFalse(segment1Coords.Contains(new GridCoordinate(5, 7)),
            "Segment 92→10 should NOT go through 5.7");
    }

    #endregion
}
