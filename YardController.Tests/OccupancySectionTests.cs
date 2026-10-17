using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Tests;

[TestClass]
public class OccupancySectionTests
{
    private readonly UnifiedStationParser _parser = new();

    /// <summary>
    /// A plain through track with a signal in the middle: the yard edges and the signal are the
    /// three boundaries, so the track becomes two stretches meeting at the signal.
    /// </summary>
    private const string StraightTrack = """
        TestStation

        [Tracks]
        2.0-2.4

        [Signals]
        2.2:<71:u
        """;

    /// <summary>
    /// One point splitting a single track into two, with a signal at each end of the layout and
    /// none in the throat, so both stretches run through the point and share its approach.
    /// </summary>
    private const string SingleThroat = """
        TestStation

        [Tracks]
        2.0-2.2-2.4
        2.2-3.3-3.4

        [Points]
        2.2(1>)-3.3  @800
        """;

    [TestMethod]
    public void Boundaries_AreSignalCoordinatesAndLayoutEdges()
    {
        var topology = _parser.Parse(StraightTrack).Topology;

        CollectionAssert.AreEquivalent(
            new[] { new GridCoordinate(2, 0), new GridCoordinate(2, 2), new GridCoordinate(2, 4) },
            topology.OccupancySectionBoundaries.ToArray());
    }

    [TestMethod]
    public void Boundaries_TwoSignalsOnOneCoordinate_CountAsOne()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.4

            [Signals]
            2.2:<71:u
            2.2:22>:i
            """;

        var topology = _parser.Parse(content).Topology;

        Assert.AreEqual(3, topology.OccupancySectionBoundaries.Count);
    }

    [TestMethod]
    public void DeriveOccupancySections_StraightTrack_SplitsAtTheSignal()
    {
        var sections = _parser.Parse(StraightTrack).Topology.DeriveOccupancySections();

        Assert.AreEqual(2, sections.Count);
        Assert.AreEqual("1:2.0,2.2", sections[0].ToString());
        Assert.AreEqual("2:2.2,2.4", sections[1].ToString());
    }

    [TestMethod]
    public void DeriveOccupancySections_AreNumberedFromOneInCoordinateOrder()
    {
        var sections = _parser.Parse(StraightTrack).Topology.DeriveOccupancySections();

        CollectionAssert.AreEqual(new[] { 1, 2 }, sections.Select(s => s.Number).ToArray());
    }

    [TestMethod]
    public void DeriveOccupancySections_AreWithoutAddress()
    {
        var sections = _parser.Parse(StraightTrack).Topology.DeriveOccupancySections();

        Assert.IsFalse(sections.Any(s => s.HasAddress));
    }

    [TestMethod]
    public void DeriveOccupancySections_Throat_OverlapsOnTheSharedApproach()
    {
        var topology = _parser.Parse(SingleThroat).Topology;

        var sections = topology.DeriveOccupancySections();

        // 2.0 to 2.4 and 2.0 to 3.4 both run over the point, and 2.4 to 3.4 runs through it.
        Assert.AreEqual(3, sections.Count);
        var tracks = topology.MapOccupancySectionTracks(sections);
        var shared = topology.Graph.GetLink(new GridCoordinate(2, 0), new GridCoordinate(2, 1));
        Assert.AreEqual(2, tracks.Values.Count(track => track.Links.Contains(shared!)));
    }

    [TestMethod]
    public void MapOccupancySectionTracks_ReturnsTheLinksBetweenTheEnds()
    {
        var topology = _parser.Parse(StraightTrack).Topology;
        var sections = topology.DeriveOccupancySections();

        var tracks = topology.MapOccupancySectionTracks(sections);

        CollectionAssert.AreEqual(
            new[] { "2.0-2.1", "2.1-2.2" },
            tracks[1].Links.Select(link => link.ToString()).ToArray());
    }

    [TestMethod]
    public void MapOccupancySectionTracks_SectionSpanningASignal_FallsBackToTheShortestPath()
    {
        var topology = _parser.Parse(StraightTrack).Topology;
        var wholeTrack = new OccupancySection(1, new GridCoordinate(2, 0), new GridCoordinate(2, 4));

        var tracks = topology.MapOccupancySectionTracks([wholeTrack]);

        Assert.AreEqual(4, tracks[1].Links.Count);
    }

    [TestMethod]
    public void MapOccupancySectionTracks_PlainTrack_IsExclusive()
    {
        var topology = _parser.Parse(StraightTrack).Topology;

        var tracks = topology.MapOccupancySectionTracks(topology.DeriveOccupancySections());

        Assert.IsTrue(tracks.Values.All(track => track.IsExclusive));
    }

    [TestMethod]
    public void MapOccupancySectionTracks_Throat_IsNotExclusive()
    {
        var topology = _parser.Parse(SingleThroat).Topology;

        var tracks = topology.MapOccupancySectionTracks(topology.DeriveOccupancySections());

        Assert.IsFalse(tracks.Values.Any(track => track.IsExclusive),
            "Every stretch through the single throat shares track with another");
    }

    [TestMethod]
    public void MapOccupancySectionTracks_Munkerod_TrackTrainsStandOnIsExclusive()
    {
        var topology = MunkerodTopology;
        var sections = topology.DeriveOccupancySections();

        var tracks = topology.MapOccupancySectionTracks(sections);

        // Found by their ends rather than by their numbers. A stretch is numbered by the order
        // derivation happens to reach it, so every number shifts the moment a signal is added or
        // moved — and what this test is about is which rails a stretch has to itself, which its
        // ends say and its number does not.
        var trackOneB = Between(sections, "1.17", "1.20");
        var throat = Between(sections, "2.2", "3.11");

        // Track 1b runs straight from signal 34 at 1.17 to signal 63 at 1.20 with no point on
        // it, so it can be shown occupied without a route saying which way the train went.
        Assert.IsTrue(tracks[trackOneB.Number].IsExclusive, "Track 1b should have its rails to itself");

        // The western throat from signal 22 at 2.2 to signal 35 at 3.11 runs over the points,
        // where several stretches share the same rails.
        Assert.IsFalse(tracks[throat.Number].IsExclusive, "The throat is shared with other stretches");
    }


    /// <summary>
    /// The one derived stretch running between two coordinates, named the way the station file
    /// names a pair: column-first, either end first.
    /// </summary>
    private static OccupancySection Between(IReadOnlyList<OccupancySection> sections, string from, string to)
    {
        var a = GridCoordinate.Parse(from);
        var b = GridCoordinate.Parse(to);
        var found = sections.Where(s => (s.From == a && s.To == b) || (s.From == b && s.To == a)).ToList();
        Assert.AreEqual(1, found.Count, $"expected exactly one stretch between {from} and {to}");
        return found[0];
    }

    [TestMethod]
    public void StraightRuns_PlainTrack_IsOneRun()
    {
        var topology = _parser.Parse(StraightTrack).Topology;
        var sections = topology.DeriveOccupancySections();
        var tracks = topology.MapOccupancySectionTracks(sections);

        var runs = tracks[Between(sections, "2.0", "2.2").Number].StraightRuns;

        CollectionAssert.AreEqual(new[] { "2.0-2.2" }, runs.Select(run => $"{run.From}-{run.To}").ToArray());
    }

    [TestMethod]
    public void StraightRuns_TrackThroughAThroat_IsCutWhereItChangesDirection()
    {
        var (topology, tracks) = SingleThroatTracks();
        var sections = topology.DeriveOccupancySections();

        var runs = tracks[Between(sections, "2.0", "3.4").Number].StraightRuns;

        // Level in from the yard edge, over the diverging arm, then level again to the far edge:
        // three runs out of three links, so the label has a level run to choose from.
        CollectionAssert.AreEqual(
            new[] { "2.0-2.2", "2.2-3.3", "3.3-3.4" },
            runs.Select(run => $"{run.From}-{run.To}").ToArray());
    }

    [TestMethod]
    public void StraightRuns_StretchDoublingBack_IsWalkedFromItsOwnEnd()
    {
        var topology = MunkerodTopology;
        var sections = topology.DeriveOccupancySections();
        var tracks = topology.MapOccupancySectionTracks(sections);

        var runs = tracks[Between(sections, "7.10", "1.12").Number].StraightRuns;

        // This stretch leaves the slip at 7.10 westwards and only then climbs east to 1.12, so
        // its links are not all spelled the way the path walks them. Walked from the end the
        // second link does not touch, it is two straight runs; read off the link ends, it would
        // fall into four.
        CollectionAssert.AreEqual(
            new[] { "7.10-5.8", "5.8-1.12" },
            runs.Select(run => $"{run.From}-{run.To}").ToArray());
    }

    [TestMethod]
    public void LabelRun_PrefersTheLongestLevelRun()
    {
        var (topology, tracks) = SingleThroatTracks();
        var sections = topology.DeriveOccupancySections();

        var run = tracks[Between(sections, "2.0", "3.4").Number].LabelRun;

        // Both ends of the stretch are level; the approach is the longer of the two, and text
        // across the diverging arm in the middle would read as belonging to neither track.
        Assert.AreEqual("2.0-2.2", $"{run!.Value.From}-{run.Value.To}");
    }

    [TestMethod]
    public void LabelRun_Munkerod_IsLevelOnEveryStretchThatHasALevelRun()
    {
        var topology = MunkerodTopology;
        var sections = topology.DeriveOccupancySections();
        var tracks = topology.MapOccupancySectionTracks(sections);

        Assert.AreEqual(sections.Count, tracks.Count, "every derived stretch has track under it");

        var slanting = sections
            .Where(section => tracks[section.Number].LabelRun is { } run && run.From.Row != run.To.Row)
            .Select(section => $"{section.From}-{section.To}")
            .ToArray();

        // Munkeröd is the yard this rule was measured against: of its 171 stretches, exactly one
        // never runs level, so it is the only one whose train number is set at 45°.
        CollectionAssert.AreEqual(new[] { "7.10-1.12" }, slanting);
    }

    [TestMethod]
    public void LabelRun_Munkerod_IsNeverShorterThanOneCell()
    {
        var topology = MunkerodTopology;
        var tracks = topology.MapOccupancySectionTracks(topology.DeriveOccupancySections());

        var shortest = tracks.Values
            .Select(track => Math.Abs(track.LabelRun!.Value.To.Column - track.LabelRun!.Value.From.Column))
            .Min();

        // A cell carries a five-digit train number with room to spare, so no stretch in the yard
        // is offered a run too short to write one along.
        Assert.AreEqual(1, shortest);
    }

    [TestMethod]
    public void ArmsTurnedAway_StraightPoint_IsTheDivergingArm()
    {
        var topology = _parser.Parse(SingleThroat).Topology;

        var turnedAway = topology.ArmsTurnedAway(Standing(PointPosition.Straight));

        CollectionAssert.AreEqual(
            new[] { "2.2-3.3" },
            turnedAway.Select(link => link.ToString()).ToArray());
    }

    [TestMethod]
    public void ArmsTurnedAway_DivergingPoint_IsTheStraightArm()
    {
        var topology = _parser.Parse(SingleThroat).Topology;

        var turnedAway = topology.ArmsTurnedAway(Standing(PointPosition.Diverging));

        CollectionAssert.AreEqual(
            new[] { "2.2-2.3" },
            turnedAway.Select(link => link.ToString()).ToArray());
    }

    [TestMethod]
    public void ArmsTurnedAway_PointOfUnknownPosition_IsTurnedAwayFromNothing()
    {
        var topology = _parser.Parse(SingleThroat).Topology;

        var turnedAway = topology.ArmsTurnedAway(Standing(PointPosition.Undefined));

        Assert.AreEqual(0, turnedAway.Count, "Not knowing where a point stands must not hide a train");
    }

    [TestMethod]
    public void ConnectedPart_StretchWithoutPoints_IsTheWholeStretch()
    {
        var topology = _parser.Parse(StraightTrack).Topology;
        var tracks = topology.MapOccupancySectionTracks(topology.DeriveOccupancySections());

        var connected = tracks[1].ConnectedPart(topology.ArmsTurnedAway(Standing(PointPosition.Straight)));

        CollectionAssert.AreEqual(tracks[1].Links.ToArray(), connected.ToArray());
    }

    [TestMethod]
    public void ConnectedPart_StretchThePointsSet_IsTheWholeStretch()
    {
        var (topology, tracks) = SingleThroatTracks();
        var straightOn = SectionThrough(topology, tracks, new GridCoordinate(2, 3));

        var connected = tracks[straightOn].ConnectedPart(topology.ArmsTurnedAway(Standing(PointPosition.Straight)));

        CollectionAssert.AreEqual(
            new[] { "2.0-2.1", "2.1-2.2", "2.2-2.3", "2.3-2.4" },
            connected.Select(link => link.ToString()).ToArray());
    }

    [TestMethod]
    public void ConnectedPart_StretchThePointIsTurnedAgainst_KeepsWhatIsStillReachable()
    {
        var (topology, tracks) = SingleThroatTracks();
        var divergingOff = SectionThrough(topology, tracks, new GridCoordinate(3, 3));

        var connected = tracks[divergingOff].ConnectedPart(topology.ArmsTurnedAway(Standing(PointPosition.Straight)));

        // The approach from 2.0 is still track a movement can be on, and so is the far end past
        // the point; only the arm the point turned away drops out.
        CollectionAssert.AreEqual(
            new[] { "2.0-2.1", "2.1-2.2", "3.3-3.4" },
            connected.Select(link => link.ToString()).ToArray());
    }

    [TestMethod]
    public void ConnectedPart_StretchThroughBothArms_KeepsOnlyItsEnds()
    {
        var (topology, tracks) = SingleThroatTracks();
        // 2.4 to 3.4 runs in over one arm and out over the other, so a point can never set it.
        var acrossThePoint = tracks.Keys.Single(number =>
            number != SectionThrough(topology, tracks, new GridCoordinate(2, 3))
            && number != SectionThrough(topology, tracks, new GridCoordinate(3, 3)));

        var connected = tracks[acrossThePoint].ConnectedPart(topology.ArmsTurnedAway(Standing(PointPosition.Diverging)));

        CollectionAssert.AreEqual(
            new[] { "2.3-2.4", "2.2-3.3", "3.3-3.4" },
            connected.Select(link => link.ToString()).ToArray());
    }

    private static Func<PointDefinition, PointPosition> Standing(PointPosition position) => _ => position;

    private (YardTopology Topology, IReadOnlyDictionary<int, OccupancySectionTrack> Tracks) SingleThroatTracks()
    {
        var topology = _parser.Parse(SingleThroat).Topology;
        return (topology, topology.MapOccupancySectionTracks(topology.DeriveOccupancySections()));
    }

    /// <summary>The stretch running from the yard edge at 2.0 over the given arm of the point.</summary>
    private static int SectionThrough(
        YardTopology topology,
        IReadOnlyDictionary<int, OccupancySectionTrack> tracks,
        GridCoordinate arm)
    {
        var armLink = topology.Graph.GetLink(new GridCoordinate(2, 2), arm)!;
        var approach = topology.Graph.GetLink(new GridCoordinate(2, 0), new GridCoordinate(2, 1))!;
        return tracks.Single(entry => entry.Value.Links.Contains(armLink) && entry.Value.Links.Contains(approach)).Key;
    }

    [TestMethod]
    public void ParseOccupancySections_ReadsNumberCoordinatesAndAddress()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.4

            [OccupancySections]
            1:2.0,2.2 @901
            2:2.2,2.4
            """;

        var sections = _parser.Parse(content).OccupancySections;

        Assert.AreEqual(2, sections.Count);
        Assert.AreEqual(new GridCoordinate(2, 0), sections[0].From);
        Assert.AreEqual(new GridCoordinate(2, 2), sections[0].To);
        Assert.AreEqual(901, sections[0].Address);
        Assert.IsNull(sections[1].Address);
    }

    [TestMethod]
    public void ParseOccupancySections_MalformedLine_IsSkipped()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.4

            [OccupancySections]
            1:2.0-2.2
            2:2.2,2.4
            """;

        var sections = _parser.Parse(content).OccupancySections;

        Assert.AreEqual(1, sections.Count);
        Assert.AreEqual(2, sections[0].Number);
    }

    [TestMethod]
    public void ContainsOccupancySections_IsTrueForAnEmptySection()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.4

            [OccupancySections]
            """;

        Assert.IsTrue(UnifiedStationWriter.ContainsOccupancySections(content));
    }

    [TestMethod]
    public void ContainsOccupancySections_IsFalseWhenTheSectionIsAbsent()
    {
        Assert.IsFalse(UnifiedStationWriter.ContainsOccupancySections(StraightTrack));
    }

    [TestMethod]
    public void InsertOccupancySections_PutsTheSectionBeforeTheRoutes()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.4

            [Signals]
            2.2:<71:u

            [Routes]
            71-72

            """;
        var sections = _parser.Parse(content).Topology.DeriveOccupancySections();

        var written = UnifiedStationWriter.InsertOccupancySections(content, sections);

        var lines = written.Split('\n').Select(line => line.Trim()).ToList();
        Assert.IsTrue(lines.IndexOf("[OccupancySections]") > lines.IndexOf("[Signals]"));
        Assert.IsTrue(lines.IndexOf("[OccupancySections]") < lines.IndexOf("[Routes]"));
    }

    [TestMethod]
    public void InsertOccupancySections_RoundTripsThroughTheParser()
    {
        var sections = _parser.Parse(StraightTrack).Topology.DeriveOccupancySections();

        var written = UnifiedStationWriter.InsertOccupancySections(StraightTrack, sections);
        var reparsed = _parser.Parse(written).OccupancySections;

        CollectionAssert.AreEqual(
            sections.Select(s => s.ToString()).ToArray(),
            reparsed.Select(s => s.ToString()).ToArray());
    }

    [TestMethod]
    public void InsertOccupancySections_KeepsEverythingElse()
    {
        var sections = _parser.Parse(StraightTrack).Topology.DeriveOccupancySections();

        var written = UnifiedStationWriter.InsertOccupancySections(StraightTrack, sections);

        foreach (var line in StraightTrack.Split('\n'))
            Assert.IsTrue(written.Contains(line, StringComparison.Ordinal), $"Line '{line}' was lost");
    }

    [TestMethod]
    public void DeriveOccupancySections_Munkerod_StartsAtTheWesternMainTrack()
    {
        var topology = MunkerodTopology;

        var sections = topology.DeriveOccupancySections();

        // The example from the specification of the format: the first stretch runs from the
        // western layout edge to the coordinate signals 22 and 71 share.
        Assert.AreEqual("1:2.0,2.2", sections[0].ToString());
        Assert.IsTrue(sections.All(section => topology.MapOccupancySectionTracks([section]).Count == 1),
            "Every derived section must resolve back to a run of track");
    }

    [TestMethod]
    public void MapOccupancySectionLinks_Munkerod_PrefersTheStraightRunOverAnEqualDetour()
    {
        var topology = MunkerodTopology;
        // Signal 64 at 4.17 reaches signal 33 at 4.20 either straight along row 4 or over the
        // points 19 and 16 through row 3. Both are three links; the straight run is shorter.
        var section = new OccupancySection(1, new GridCoordinate(4, 17), new GridCoordinate(4, 20));

        var tracks = topology.MapOccupancySectionTracks([section]);

        CollectionAssert.AreEqual(
            new[] { "4.17-4.18", "4.18-4.19", "4.19-4.20" },
            tracks[1].Links.Select(link => link.ToString()).ToArray());
    }

    [TestMethod]
    public void DeriveOccupancySections_Munkerod_CoverEveryTrackLink()
    {
        var topology = MunkerodTopology;

        var covered = topology
            .MapOccupancySectionTracks(topology.DeriveOccupancySections())
            .Values.SelectMany(track => track.Links)
            .ToHashSet();

        var uncovered = topology.Graph.Links.Where(link => !covered.Contains(link)).ToArray();
        Assert.AreEqual(0, uncovered.Length,
            $"No stretch covers {string.Join(", ", uncovered.Select(link => link.ToString()))}");
    }

    [TestMethod]
    public void ConnectedPart_Munkerod_NeverDrawsAnArmThePointsTurnedAway()
    {
        var topology = MunkerodTopology;
        var tracks = topology.MapOccupancySectionTracks(topology.DeriveOccupancySections());
        var turnedAway = topology.ArmsTurnedAway(Standing(PointPosition.Straight));

        // With every point straight, no stretch may draw a diverging arm — whatever a detector
        // says, a movement cannot be on track the points lead away from.
        var drawn = tracks.Values.SelectMany(track => track.ConnectedPart(turnedAway)).ToHashSet();

        Assert.AreEqual(30, turnedAway.Count,
            "Munkeröd's 46 point halves leave 30 arms, the halves of a crossover sharing the diagonal between them");
        Assert.IsFalse(drawn.Overlaps(turnedAway));
    }

    private static YardTopology MunkerodTopology
    {
        get
        {
            var stationPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.Web", "Data", "Munkeröd.txt"));
            return new UnifiedStationParser().Parse(File.ReadAllText(stationPath)).Topology;
        }
    }

    /// <summary>
    /// A <c>!</c> after the number marks a stretch an operator frees by hand. It only means
    /// anything where there is a detector to fall silent, so the parser drops it from a stretch
    /// with no address rather than leaving a flag that could never be answered.
    /// </summary>
    [TestMethod]
    public void ManualReleaseFlag_IsParsedAndRoundTripped()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.4

            [Signals]
            2.2:<71:u

            [OccupancySections]
            1!:2.0,2.2  @901
            2:2.2,2.4  @902
            """;

        var sections = _parser.Parse(content).OccupancySections;

        Assert.IsTrue(sections.Single(s => s.Number == 1).RequiresManualRelease);
        Assert.IsTrue(sections.Single(s => s.Number == 1).HoldsOccupancy);
        Assert.IsFalse(sections.Single(s => s.Number == 2).RequiresManualRelease);
        Assert.AreEqual("1!:2.0,2.2  @901", UnifiedStationWriter.FormatOccupancySection(sections.Single(s => s.Number == 1)));
        Assert.AreEqual("2:2.2,2.4  @902", UnifiedStationWriter.FormatOccupancySection(sections.Single(s => s.Number == 2)));
    }

    [TestMethod]
    public void ManualReleaseFlag_WithoutAddress_IsDropped()
    {
        var content = """
            TestStation

            [Tracks]
            2.0-2.4

            [Signals]
            2.2:<71:u

            [OccupancySections]
            1!:2.0,2.2
            """;

        var section = _parser.Parse(content).OccupancySections.Single();

        Assert.IsFalse(section.RequiresManualRelease, "a stretch with no detector never reports free on its own");
        Assert.IsFalse(section.HoldsOccupancy);
    }
}
