using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Components;
using YardController.Web.Services;
using YardController.Web.Services.Testing;

namespace YardController.Tests;

/// <summary>
/// A plain crossing declared in <c>[Points]</c>: what the parser makes of it, which arms of it
/// each way through it reaches by, and what it adds to the routes that run over it.
/// </summary>
[TestClass]
public class CrossingTests
{
    private readonly UnifiedStationParser _parser = new();

    /// <summary>
    /// A miniature of Munkeröd's 5.8: two crossover diagonals crossing at 2.2, one carried by
    /// point 8 and the other by point 29. Neither switch point is a neighbour of the crossing,
    /// which is what makes finding its arms worth testing.
    /// </summary>
    private const string TwoDiagonals = """
        TestStation

        [Tracks]
        1.1-2.2-3.3
        3.1-2.2-1.3
        1.1-1.3
        3.1-3.3

        [Points]
        1.1(8a>)-3.3(<8b)  @101,102
        3.1(<29a)-1.3(29b>)  @103,104
        2.2(8-|29+)(29-|8+)

        [Routes]
        11-13:8-
        """;

    [TestMethod]
    public void CrossingIsParsedWithBothWaysAndTheirConditions()
    {
        var topology = _parser.Parse(TwoDiagonals).Topology;

        Assert.HasCount(1, topology.Crossings);
        var crossing = topology.Crossings[0];
        Assert.AreEqual(new GridCoordinate(2, 2), crossing.Coordinate);
        Assert.HasCount(2, crossing.Ways);

        Assert.AreEqual(8, crossing.Ways[0].Governing.Number);
        Assert.AreEqual(PointPosition.Diverging, crossing.Ways[0].Governing.Position);
        Assert.HasCount(1, crossing.Ways[0].Conditions);
        Assert.AreEqual(29, crossing.Ways[0].Conditions[0].Number);
        Assert.AreEqual(PointPosition.Straight, crossing.Ways[0].Conditions[0].Position);

        Assert.AreEqual(29, crossing.Ways[1].Governing.Number);
        Assert.AreEqual(PointPosition.Diverging, crossing.Ways[1].Governing.Position);
        Assert.AreEqual(8, crossing.Ways[1].Conditions[0].Number);
        Assert.AreEqual(PointPosition.Straight, crossing.Ways[1].Conditions[0].Position);
    }

    /// <summary>A condition is flank protection: it holds a point clear, it does not carry the movement.</summary>
    [TestMethod]
    public void CrossingConditionsAreOffRoute()
    {
        var topology = _parser.Parse(TwoDiagonals).Topology;
        Assert.IsFalse(topology.Crossings[0].Ways[0].Conditions[0].IsOnRoute);
    }

    [TestMethod]
    public void CommaBetweenWaysIsOptional()
    {
        var withComma = _parser.Parse(TwoDiagonals.Replace("(8-|29+)(29-|8+)", "(8-|29+), (29-|8+)", StringComparison.Ordinal));
        Assert.HasCount(2, withComma.Topology.Crossings[0].Ways);
    }

    [TestMethod]
    public void SeveralConditionsAreAllKept()
    {
        var data = _parser.Parse(TwoDiagonals.Replace("(8-|29+)", "(8-|29+,4+,5-)", StringComparison.Ordinal));
        var conditions = data.Topology.Crossings[0].Ways[0].Conditions;
        Assert.HasCount(3, conditions);
        Assert.AreEqual(4, conditions[1].Number);
        Assert.AreEqual(PointPosition.Diverging, conditions[2].Position);
    }

    /// <summary>
    /// Each way reaches the crossing by two arms, one on either side. Both switch points here lie
    /// a cell beyond the crossing, so the arm is the first step towards them rather than a link
    /// to them.
    /// </summary>
    [TestMethod]
    public void EachWayReachesTheCrossingByTwoArms()
    {
        var topology = _parser.Parse(TwoDiagonals).Topology;
        var crossing = topology.Crossings[0];

        var eight = topology.ArmsOf(crossing, crossing.Ways[0]).Select(link => link.ToString()).ToHashSet();
        var twentyNine = topology.ArmsOf(crossing, crossing.Ways[1]).Select(link => link.ToString()).ToHashSet();

        Assert.HasCount(2, eight);
        Assert.HasCount(2, twentyNine);
        Assert.IsEmpty(eight.Intersect(twentyNine), "the two ways must not share an arm");
        Assert.AreEqual(4, eight.Union(twentyNine).Count(), "together the ways cover all four arms");
    }

    [TestMethod]
    public void OnlyTheGoverningPositionLaysAWay()
    {
        var topology = _parser.Parse(TwoDiagonals).Topology;

        Assert.HasCount(1, topology.WaysLaidBy([new PointCommand(8, PointPosition.Diverging)]).ToList());
        Assert.IsEmpty(topology.WaysLaidBy([new PointCommand(8, PointPosition.Straight)]).ToList());
        Assert.HasCount(2, topology.WaysLaidBy(
            [new PointCommand(8, PointPosition.Diverging), new PointCommand(29, PointPosition.Diverging)]).ToList());
    }

    [TestMethod]
    public void ConditionInTheWayIsReportedWithTheOffendingPosition()
    {
        var topology = _parser.Parse(TwoDiagonals).Topology;

        var blocked = topology.CrossingConditionsInTheWay(
            new PointCommand(8, PointPosition.Diverging), _ => PointPosition.Diverging).ToList();
        Assert.HasCount(1, blocked);
        Assert.AreEqual(29, blocked[0].Condition.Number);
        Assert.AreEqual(PointPosition.Diverging, blocked[0].Actual);

        Assert.IsEmpty(topology.CrossingConditionsInTheWay(
            new PointCommand(8, PointPosition.Diverging), _ => PointPosition.Straight).ToList());
    }

    /// <summary>
    /// A point nothing has reported yet is as much a reason to refuse as one standing the wrong
    /// way: the crossing has to be known clear, not merely not known to be blocked.
    /// </summary>
    [TestMethod]
    public void UnknownPositionIsAConditionInTheWay()
    {
        var topology = _parser.Parse(TwoDiagonals).Topology;
        Assert.HasCount(1, topology.CrossingConditionsInTheWay(
            new PointCommand(8, PointPosition.Diverging), _ => PointPosition.Undefined).ToList());
    }

    [TestMethod]
    public void RouteLayingAWayGetsItsConditionsAsFlankProtection()
    {
        var route = _parser.Parse(TwoDiagonals).TrainRoutes.Single();

        var flank = route.OffRoutePoints.SingleOrDefault(point => point.Number == 29);
        Assert.IsNotNull(flank, "the crossing's condition should have been added to the route");
        Assert.AreEqual(PointPosition.Straight, flank.Position);
    }

    [TestMethod]
    public void RouteThatLaysNoWayIsLeftAlone()
    {
        var content = TwoDiagonals.Replace("11-13:8-", "11-13:8+", StringComparison.Ordinal);
        var route = _parser.Parse(content).TrainRoutes.Single();
        Assert.IsEmpty(route.PointCommands.Where(point => point.Number == 29));
    }

    /// <summary>A route already holding the point where the crossing wants it keeps its own command.</summary>
    [TestMethod]
    public void ConditionAlreadyOnTheRouteIsNotAddedTwice()
    {
        var content = TwoDiagonals.Replace("11-13:8-", "11-13:8-,29+", StringComparison.Ordinal);
        var route = _parser.Parse(content).TrainRoutes.Single();
        Assert.HasCount(1, route.PointCommands.Where(point => point.Number == 29).ToList());
    }

    /// <summary>A crossing written back out must read as the same crossing.</summary>
    [TestMethod]
    public void CrossingSurvivesARoundTripThroughTheWriter()
    {
        var written = UnifiedStationWriter.Write(_parser.Parse(TwoDiagonals));
        var crossing = _parser.Parse(written).Topology.Crossings.Single();

        Assert.AreEqual(new GridCoordinate(2, 2), crossing.Coordinate);
        Assert.HasCount(2, crossing.Ways);
        Assert.AreEqual("(8-|29+)", crossing.Ways[0].ToString());
        Assert.AreEqual("(29-|8+)", crossing.Ways[1].ToString());
    }

    [TestMethod]
    public void MalformedCrossingIsNotParsedAsAPoint()
    {
        var content = TwoDiagonals.Replace("2.2(8-|29+)(29-|8+)", "2.2(8|29+)", StringComparison.Ordinal);
        var topology = _parser.Parse(content).Topology;
        Assert.IsEmpty(topology.Crossings);
        Assert.HasCount(4, topology.Points, "the crossing line must not add a point definition");
    }

    #region Munkeröd

    private static string MunkerödPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.Web", "Data", "Munkeröd.txt"));

    /// <summary>
    /// The crossing at 5.8, where the diagonal carried by point 8 (4.7 to 7.10) crosses the one
    /// carried by point 29 (7.6 to 4.9). Its four arms are the four links at 5.8, two per way.
    /// </summary>
    [TestMethod]
    public void MunkerödCrossingCoversAllFourArmsAt58()
    {
        var topology = new UnifiedStationParser().Parse(File.ReadAllText(MunkerödPath)).Topology;
        var crossing = topology.Crossings.Single(c => c.Coordinate == new GridCoordinate(5, 8));

        var arms = crossing.Ways.SelectMany(way => topology.ArmsOf(crossing, way)).ToList();
        Assert.HasCount(4, arms.Distinct().ToList());
        Assert.HasCount(4, topology.Graph.GetLinksAt(crossing.Coordinate).ToList());
        foreach (var link in topology.Graph.GetLinksAt(crossing.Coordinate))
            Assert.Contains(link, arms, $"{link} belongs to no declared way through the crossing");
    }

    /// <summary>
    /// Every route over the crossing holds the other diagonal's point straight, whether it was
    /// written into the route by hand or added by the crossing.
    /// </summary>
    [TestMethod]
    public void EveryMunkerödRouteOverTheCrossingHoldsTheOtherWayClear()
    {
        var data = new UnifiedStationParser().Parse(File.ReadAllText(MunkerödPath));

        foreach (var route in data.TrainRoutes)
        {
            AssertHeldStraight(route, lays: 8, holds: 29);
            AssertHeldStraight(route, lays: 29, holds: 8);
        }

        static void AssertHeldStraight(TrainRouteCommand route, int lays, int holds)
        {
            if (!route.PointCommands.Any(point => point.Number == lays && point.Position == PointPosition.Diverging)) return;
            var held = route.PointCommands.SingleOrDefault(point => point.Number == holds);
            Assert.IsNotNull(held, $"Route {route.FromSignal}-{route.ToSignal} lays {lays}- but says nothing about {holds}");
            Assert.AreEqual(PointPosition.Straight, held.Position,
                $"Route {route.FromSignal}-{route.ToSignal} lays {lays}- and {holds}- over the same crossing");
        }
    }

    /// <summary>
    /// The break drawn across each closed arm lands on that arm, just clear of the crossing, and
    /// is cut parallel to the track that passes over. Munkeröd's 5.8 is the only crossing there
    /// is, and all four of its arms are diagonals, which is the case the geometry can get wrong.
    /// </summary>
    [TestMethod]
    public void EveryArmOfTheMunkerödCrossingCanBeDrawnClosed()
    {
        const double cellSize = 40;
        const double trackThickness = 10;
        var topology = new UnifiedStationParser().Parse(File.ReadAllText(MunkerödPath)).Topology;
        var crossing = topology.Crossings.Single(c => c.Coordinate == new GridCoordinate(5, 8));
        var (cx, cy) = Pixel(crossing.Coordinate);

        foreach (var way in crossing.Ways)
        {
            var across = crossing.Ways.Single(other => other != way);
            var (acrossX, acrossY) = Pixel(OtherEnd(topology.ArmsOf(crossing, across)[0], crossing.Coordinate));

            foreach (var arm in topology.ArmsOf(crossing, way))
            {
                var (armX, armY) = Pixel(OtherEnd(arm, crossing.Coordinate));
                var patch = TrackCutOff.AtCrossing(cx, cy, armX, armY, acrossX - cx, acrossY - cy, trackThickness);
                Assert.IsNotNull(patch, $"arm {arm} of way {way.Governing.Number} draws no break");

                // The break sits on the closed arm, on the crossing's side of its far end.
                var centre = (X: patch.Average(corner => corner.X), Y: patch.Average(corner => corner.Y));
                var along = (X: armX - cx, Y: armY - cy);
                var reach = ((centre.X - cx) * along.X + (centre.Y - cy) * along.Y)
                    / (along.X * along.X + along.Y * along.Y);
                Assert.IsGreaterThan(0, reach, $"the break on {arm} is on the wrong side of the crossing");
                Assert.IsLessThan(0.5, reach, $"the break on {arm} is not close to the crossing");
            }
        }

        static (double X, double Y) Pixel(GridCoordinate coordinate) =>
            (coordinate.Column * cellSize, coordinate.Row * cellSize);

        static GridCoordinate OtherEnd(TrackLink link, GridCoordinate coordinate) =>
            link.FromNode.Coordinate == coordinate ? link.ToNode.Coordinate : link.FromNode.Coordinate;
    }

    #endregion

    #region At the keypad

    /// <summary>
    /// The whole way through: keys in, point commands out, with the crossing standing between.
    /// The station is the same miniature, loaded into the test services as a real one would be.
    /// </summary>
    private static async Task<(List<PointCommand> Sent, List<PointResult> Rejected)> AtTheKeypad(string keys)
    {
        await using var services = ServiceProvider.InstanceForTesting;
        var sut = services.GetRequiredService<IHostedService>();
        var yardData = services.GetRequiredService<TestYardDataService>();
        var station = new UnifiedStationParser().Parse(TwoDiagonals);
        yardData.SetTopology(station.Topology);
        foreach (var point in station.Points) yardData.AddPoint(point);

        var rejected = new List<PointResult>();
        services.GetRequiredService<IPointNotificationService>().PointChanged += result =>
        {
            if (result.ResultType == PointResultType.Rejected) rejected.Add(result);
        };

        var keyReader = (TestKeyReader)services.GetRequiredService<IKeyReader>();
        keyReader.AddKeys(keys);

        await sut.StartAsync(default);
        await Task.Delay(400, default);
        var sent = ((TestYardController)services.GetRequiredService<IYardController>()).Commands.ToList();
        await sut.StopAsync(default);
        return (sent, rejected);
    }

    /// <summary>
    /// The crossing must be known clear, not merely not known to be blocked: with nothing yet
    /// reported about the other point, laying a way over it is refused. The way out is always
    /// open, since turning the other point away lays nothing and is never refused.
    /// </summary>
    [TestMethod]
    public async Task LayingAWayIsRefusedWhileTheOtherPointIsUnknown()
    {
        var (sent, rejected) = await AtTheKeypad("8-");

        Assert.IsEmpty(sent, "nothing should reach the yard");
        Assert.HasCount(1, rejected);
        Assert.Contains("29", rejected[0].Message!, "the refusal should name the point in the way");
    }

    [TestMethod]
    public async Task LayingAWayIsAllowedOnceTheOtherPointIsTurnedAway()
    {
        var (sent, rejected) = await AtTheKeypad("29+8-");

        Assert.IsEmpty(rejected);
        Assert.IsTrue(sent.Any(command => command.Number == 8 && command.Position == PointPosition.Diverging),
            "point 8 should have reached the yard");
    }

    /// <summary>Symmetric: with one way laid, the other is refused, and the refusal names it.</summary>
    [TestMethod]
    public async Task LayingTheSecondWayOverTheCrossingIsRefused()
    {
        var (sent, rejected) = await AtTheKeypad("29+8-29-");

        Assert.HasCount(1, rejected);
        Assert.Contains("8", rejected[0].Message!);
        Assert.IsEmpty(sent.Where(command => command.Number == 29 && command.Position == PointPosition.Diverging));
    }

    #endregion
}
