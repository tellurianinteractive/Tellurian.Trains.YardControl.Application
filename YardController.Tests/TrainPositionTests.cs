using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Tests;

[TestClass]
public class TrainPositionTests
{
    private readonly UnifiedStationParser _parser = new();

    /// <summary>
    /// A through station: the track runs in from the west edge past inbound signal 21, on to
    /// outbound signal 81, and out to the east edge. One detection stretch per run.
    /// </summary>
    private const string ThroughStation = """
        TestStation

        [Tracks]
        1.0-1.5-1.10-1.15

        [Signals]
        1.5:21>:i
        1.10:81>:u

        [OccupancySections]
        1:1.0,1.5
        2:1.5,1.10
        3:1.10,1.15
        """;

    [TestMethod]
    public void SectionLeadingUpTo_IsTheStretchOnTheSideTheTrainComesFrom()
    {
        var station = _parser.Parse(ThroughStation);
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);

        // A train accepted from the previous station stands on the run in from the edge.
        Assert.AreEqual(1, topology.SectionLeadingUpTo(21, station.OccupancySections, tracks));
        // A train that has arrived stands on the run up to the outbound signal.
        Assert.AreEqual(2, topology.SectionLeadingUpTo(81, station.OccupancySections, tracks));
    }

    [TestMethod]
    public void SectionBeyond_IsTheRunOutToTheEdge()
    {
        var station = _parser.Parse(ThroughStation);
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);

        Assert.AreEqual(3, topology.SectionBeyond(81, station.OccupancySections, tracks),
            "A departed train stands between the last signal and the edge");
        Assert.IsNull(topology.SectionBeyond(21, station.OccupancySections, tracks),
            "Nothing leaves the station through an inbound signal, so there is nothing beyond it");
    }

    [TestMethod]
    public void DepartureSectionFrom_TakesATrainStandingAtTheOutboundSignalOutOfTheStation()
    {
        var station = _parser.Parse(ThroughStation);
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);

        Assert.AreEqual(3, topology.DepartureSectionFrom(2, station.OccupancySections, tracks));
        Assert.IsNull(topology.DepartureSectionFrom(3, station.OccupancySections, tracks),
            "A train already out at the edge has nowhere further to depart to");
    }

    [TestMethod]
    public void SectionPositionOnRoute_OrdersTheStretchesTheWayTheTrainRuns()
    {
        var station = _parser.Parse(ThroughStation);
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);
        var route = new TrainRouteCommand(21, 81, TrainRouteState.SetMain, []);
        var path = topology.RouteLinksInOrder(route);

        Assert.IsGreaterThanOrEqualTo(0, topology.SectionPositionOnRoute(2, path, tracks),
            "The stretch between the two signals is the one the route runs over");
        Assert.AreEqual(-1, topology.SectionPositionOnRoute(1, path, tracks),
            "The approach stretch lies behind the from signal, so it is not on the route");
        Assert.AreEqual(-1, topology.SectionPositionOnRoute(3, path, tracks),
            "The departure stretch lies beyond the to signal, so it is not on the route");
    }

    [TestMethod]
    public void IsSectionAheadOnRoute_LetsATrainMoveForwardOnly()
    {
        var station = _parser.Parse(ThroughStation);
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);
        var path = topology.RouteLinksInOrder(new TrainRouteCommand(21, 81, TrainRouteState.SetMain, []));

        Assert.IsTrue(topology.IsSectionAheadOnRoute(1, 2, path, tracks),
            "A train waiting at its from signal has the whole route ahead of it");
        Assert.IsFalse(topology.IsSectionAheadOnRoute(2, 1, path, tracks),
            "Going back to the approach stretch is not moving forward");
        Assert.IsFalse(topology.IsSectionAheadOnRoute(2, 2, path, tracks),
            "Where the train already stands is not ahead of it");
        Assert.IsFalse(topology.IsSectionAheadOnRoute(2, 3, path, tracks),
            "A stretch off the route is not reachable by running the route");
    }

    [TestMethod]
    public void SectionLeadingUpTo_Munkerod_TakesTheRunInFromTheEdgeAtTheInboundSignal()
    {
        var station = MunkerodStation;
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);

        // Signal 22 stands at 2.2 facing east, and 1:2.0,2.2 is the only stretch reaching it
        // from the west — the track a train accepted from the previous station runs in on.
        Assert.AreEqual(1, topology.SectionLeadingUpTo(22, station.OccupancySections, tracks));

        // Signal 71 shares that coordinate facing the other way, so it departs onto the same run.
        Assert.AreEqual(1, topology.SectionBeyond(71, station.OccupancySections, tracks));
    }

    [TestMethod]
    public void SectionLeadingUpTo_Munkerod_LetsTheRouteChooseAmongTheStretchesSharingASignal()
    {
        var station = MunkerodStation;
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);
        var atSignal71 = new GridCoordinate(2, 2);

        // Twelve stretches leave 2.2 into the western throat. Handed the track of any one of
        // them, the answer has to be that one: only the route can say which way a movement came.
        var candidates = station.OccupancySections
            .Where(section => section.From == atSignal71)
            .Select(section => section.Number)
            .ToArray();
        Assert.IsGreaterThan(1, candidates.Length, "The western throat should offer several stretches");

        foreach (var candidate in candidates)
        {
            var routeLinks = tracks[candidate].Links.ToHashSet();
            Assert.AreEqual(candidate,
                topology.SectionLeadingUpTo(71, station.OccupancySections, tracks, routeLinks),
                $"A route running over stretch {candidate} should place the train on it");
        }
    }

    [TestMethod]
    public void SectionLeadingUpTo_Munkerod_TakesTheShortestStretchWithoutARoute()
    {
        var station = MunkerodStation;
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);
        var atSignal71 = new GridCoordinate(2, 2);

        var chosen = topology.SectionLeadingUpTo(71, station.OccupancySections, tracks);
        Assert.IsNotNull(chosen);

        // Without a route the track alongside the signal is taken, not a run across the throat.
        var chosenLength = tracks[chosen.Value].Links.Count;
        foreach (var section in station.OccupancySections.Where(section => section.From == atSignal71))
            Assert.IsLessThanOrEqualTo(tracks[section.Number].Links.Count, chosenLength,
                $"Stretch {section.Number} is shorter than the chosen {chosen.Value}");
    }

    [TestMethod]
    public void SectionLeadingUpTo_Munkerod_TakesTheSideAMovementApproachesFromAtASignalFacingTheOtherWay()
    {
        var station = MunkerodStation;
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);
        var route = station.TrainRoutes.Single(command => command.FromSignal == 22 && command.ToSignal == 38);
        var links = topology.RouteLinksInOrder(route).ToHashSet();
        Assert.IsNotEmpty(links, "Route 22-38 should resolve to track");

        // Signal 65 stands at 2.12 facing west, halfway along a route running east. A train on
        // that route comes up to it over the stretch behind it, not the one in front.
        Assert.AreEqual(
            SectionBetween(station, new GridCoordinate(2, 2), new GridCoordinate(2, 12)),
            topology.SectionLeadingUpTo(65, station.OccupancySections, tracks, links, topology.RouteDrivesRight(route)),
            "The route says which way the train runs, so the stretch behind the signal is the one it waits on");

        Assert.AreEqual(
            SectionBetween(station, new GridCoordinate(2, 12), new GridCoordinate(2, 17)),
            topology.SectionLeadingUpTo(65, station.OccupancySections, tracks, links),
            "Without a direction the signal's own facing stands in for the movement's");
    }

    [TestMethod]
    public void RouteLinksInOrder_Munkerod_RunsInTheOrderTheTrainCoversTheTrack()
    {
        var station = MunkerodStation;
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);
        var route = station.TrainRoutes.Single(command => command.FromSignal == 22 && command.ToSignal == 38);
        var ordered = topology.RouteLinksInOrder(route);
        Assert.IsNotEmpty(ordered, "Route 22-38 should resolve to track");

        // The four stretches route 22-38 runs over, west to east as the train covers them.
        var along = new[]
        {
            SectionBetween(station, new GridCoordinate(2, 2), new GridCoordinate(2, 12)),
            SectionBetween(station, new GridCoordinate(2, 12), new GridCoordinate(2, 17)),
            SectionBetween(station, new GridCoordinate(2, 17), new GridCoordinate(1, 20)),
            SectionBetween(station, new GridCoordinate(1, 20), new GridCoordinate(1, 25)),
        };

        for (var i = 1; i < along.Length; i++)
        {
            Assert.IsGreaterThan(
                topology.SectionPositionOnRoute(along[i - 1], ordered, tracks),
                topology.SectionPositionOnRoute(along[i], ordered, tracks),
                $"Stretch {along[i]} comes after {along[i - 1]} the way the train runs");
            Assert.IsTrue(topology.IsSectionAheadOnRoute(along[i - 1], along[i], ordered, tracks),
                $"A train on stretch {along[i - 1]} moves forward by reaching {along[i]}");
        }
    }

    [TestMethod]
    public void SectionsAlongRoute_IsTheRunBetweenTheTwoSignals()
    {
        var station = _parser.Parse(ThroughStation);
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);
        var path = topology.RouteLinksInOrder(new TrainRouteCommand(21, 81, TrainRouteState.SetMain, [])).ToHashSet();

        var along = tracks.SectionsAlongRoute(path);

        Assert.AreEqual(1, along.Count);
        Assert.AreEqual(2, along[0], "Only the stretch between the two signals is on the route");
    }

    [TestMethod]
    public void SectionsAlongRoute_Munkerod_CoversTheRouteWithoutWritingOverTheSameRailsTwice()
    {
        var station = MunkerodStation;
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);
        var route = station.TrainRoutes.Single(command => command.FromSignal == 22 && command.ToSignal == 34);
        var path = topology.RouteLinksInOrder(route).ToHashSet();
        Assert.IsGreaterThan(0, path.Count, "Route 22-34 should resolve to track");

        var along = tracks.SectionsAlongRoute(path);

        Assert.IsGreaterThan(1, along.Count, "A route across the yard runs over several stretches");

        foreach (var section in along)
            Assert.IsTrue(tracks[section].Links.All(path.Contains),
                $"Stretch {section} reaches off the route");

        // None of the kept stretches swallows another, or the number would be written twice over
        // the same rails — the derived stretches overlap wherever the track branches.
        foreach (var section in along)
            foreach (var other in along.Where(other => other != section))
                Assert.IsFalse(
                    tracks[other].Links.Count < tracks[section].Links.Count
                    && tracks[other].Links.All(tracks[section].Links.Contains),
                    $"Stretch {section} spans stretch {other}");

        var covered = along.SelectMany(section => tracks[section].Links).ToHashSet();
        var uncovered = path.Where(link => !covered.Contains(link)).ToArray();
        Assert.AreEqual(0, uncovered.Length,
            $"The kept stretches leave {string.Join(", ", uncovered.Select(link => link.ToString()))} without a train number");
    }

    [TestMethod]
    public void Format_PadsTrainNumbersToFiveDigits()
    {
        Assert.AreEqual("01234", TrainNumberDisplay.Format("1234"));
        Assert.AreEqual("00007", TrainNumberDisplay.Format("7"));
        Assert.AreEqual("12345", TrainNumberDisplay.Format("12345"));
        Assert.AreEqual("123456", TrainNumberDisplay.Format("123456"),
            "A longer number is left alone — cutting it would name a different train");
        Assert.AreEqual("", TrainNumberDisplay.Format(null));
    }

    /// <summary>The stretch running between two coordinates, named the way the file spells it.</summary>
    private static int SectionBetween(UnifiedStationData station, GridCoordinate from, GridCoordinate to) =>
        station.OccupancySections.Single(section => section.From == from && section.To == to).Number;

    [TestMethod]
    public void SectionFromEdge_IsTheRunInFromTheEdge()
    {
        var station = _parser.Parse(ThroughStation);
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);

        Assert.AreEqual(1, topology.SectionFromEdge(21, station.OccupancySections, tracks),
            "A train the previous station has offered stands between the edge and the inbound signal");
        Assert.IsNull(topology.SectionFromEdge(81, station.OccupancySections, tracks),
            "Nothing comes into the station through an outbound signal, so no track runs in to it");
    }

    [TestMethod]
    public void SectionFromEdge_Munkerod_AnswersOnlyForTheSignalTheTrackComesInThrough()
    {
        var station = MunkerodStation;
        var topology = station.Topology;
        var tracks = topology.MapOccupancySectionTracks(station.OccupancySections);

        // 22 and 71 share 2.2. The inbound one is the way in from the western edge; the outbound
        // one is approached from the throat, so it has no run in from an edge at all.
        Assert.AreEqual(1, topology.SectionFromEdge(22, station.OccupancySections, tracks));
        Assert.IsNull(topology.SectionFromEdge(71, station.OccupancySections, tracks));

        // The same at the eastern edge, where the inbound signal faces the other way. The stretch
        // is named by its ends rather than by its number: a stretch is numbered by the order
        // derivation happens to reach it, so every number shifts the moment a signal is added.
        var easternApproach = station.OccupancySections
            .Single(section => section.From.ToString() == "2.32" && section.To.ToString() == "2.34");
        Assert.AreEqual(easternApproach.Number, topology.SectionFromEdge(51, station.OccupancySections, tracks));
        Assert.IsNull(topology.SectionFromEdge(42, station.OccupancySections, tracks));
    }

    private static UnifiedStationData MunkerodStation
    {
        get
        {
            var stationPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.Web", "Data", "Munkeröd.txt"));
            return new UnifiedStationParser().Parse(File.ReadAllText(stationPath));
        }
    }
}
