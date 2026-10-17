using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Tests;

[TestClass]
public class ShuntingAreaExtensionsTests
{
    private static readonly Dictionary<int, Point> Points = new()
    {
        [4] = new Point(4, [804], [804], 0),
        [8] = new Point(8, [808], [808], 0),
        [9] = new Point(9, [809], [809], 0),
        [10] = new Point(10, [810], [810], 0),
        [19] = new Point(19, [819], [819], 0),
        [27] = new Point(27, [827], [827], 0),
    };

    /// <summary>Area 5 hands over points 4 and 19 and holds point 27 straight to protect them.</summary>
    private static ShuntingArea Runaround => new(5, "Runaround", [
        new PointCommand(4, PointPosition.Undefined, null, false),
        new PointCommand(27, PointPosition.Straight, null, false),
        new PointCommand(19, PointPosition.Undefined, null, false)]);

    /// <summary>Area 1 hands over point 10 and holds point 8 straight.</summary>
    private static ShuntingArea Left => new(1, "Left", [
        new PointCommand(8, PointPosition.Straight, null, false),
        new PointCommand(10, PointPosition.Undefined, null, false)]);

    private static TrainRouteCommand Route(params PointCommand[] pointCommands) =>
        new(21, 31, TrainRouteState.SetShunting, pointCommands);

    private static PointCommand On(int number, PointPosition position) => new(number, position);

    [TestMethod]
    public void RouteOverFlankProtectionInItsHeldPositionIsWithinTheArea()
    {
        var route = Route(On(4, PointPosition.Straight), On(27, PointPosition.Straight), On(19, PointPosition.Straight));
        Assert.IsTrue(route.IsWithinShuntingAreas([Runaround], Points),
            "The route asks for point 27 in the very position the area holds it, so it alters nothing.");
    }

    [TestMethod]
    public void RouteAgainstFlankProtectionIsRejected()
    {
        var route = Route(On(4, PointPosition.Straight), On(27, PointPosition.Diverging), On(19, PointPosition.Straight));
        Assert.IsFalse(route.IsWithinShuntingAreas([Runaround], Points),
            "Moving point 27 off its held position would break the protection the area rests on.");
    }

    [TestMethod]
    public void RouteAcrossTwoReleasedAreasIsWithinThem()
    {
        var route = Route(On(4, PointPosition.Straight), On(19, PointPosition.Straight), On(10, PointPosition.Straight));
        Assert.IsTrue(route.IsWithinShuntingAreas([Runaround, Left], Points),
            "Ownership spans every released area, so a route may run from one into the next.");
    }

    [TestMethod]
    public void RouteAcrossTwoAreasOverBothFlankPointsIsWithinThem()
    {
        var route = Route(On(4, PointPosition.Straight), On(27, PointPosition.Straight),
                          On(8, PointPosition.Straight), On(10, PointPosition.Straight));
        Assert.IsTrue(route.IsWithinShuntingAreas([Runaround, Left], Points),
            "Both flank points are asked for in their held positions.");
    }

    [TestMethod]
    public void RouteReachingOutsideEveryReleasedAreaIsRejected()
    {
        var route = Route(On(4, PointPosition.Straight), On(9, PointPosition.Straight));
        Assert.IsFalse(route.IsWithinShuntingAreas([Runaround, Left], Points),
            "Point 9 is declared by no released area, neither as a shunter point nor as flank protection.");
    }

    [TestMethod]
    public void RouteIsRejectedWhenNoAreaIsReleased()
    {
        var route = Route(On(4, PointPosition.Straight), On(19, PointPosition.Straight));
        Assert.IsFalse(route.IsWithinShuntingAreas([], Points));
    }
}
