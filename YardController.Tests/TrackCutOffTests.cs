using YardController.Web.Components;

namespace YardController.Tests;

/// <summary>
/// The break drawn across a closed track line. A point's break is measured in the diagram's frame,
/// which is where its arms are level and diagonal; a plain crossing has no level arm, so its break
/// is measured in the closed arm's own frame. The two geometries are separate for that reason, and
/// these tests hold each to its own shape.
/// </summary>
[TestClass]
public class TrackCutOffTests
{
    private const double TrackThickness = 10;

    #region At a point

    /// <summary>
    /// A level arm cut by a diagonal is what every point standing diverging draws, and the great
    /// majority of the breaks on the diagram. Its corners must be exactly where they always were.
    /// </summary>
    [TestMethod]
    public void ALevelArmIsCutExactlyAsBefore()
    {
        var patch = TrackCutOff.AtPoint(0, 0, 100, 0, 40, -40, TrackThickness)!;

        var expected = new[] { (1.0, 5.0), (16.5564, 5.0), (26.5564, -5.0), (11.0, -5.0) };
        foreach (var corner in expected)
            Assert.IsTrue(
                patch.Any(c => Math.Abs(c.X - corner.Item1) < 0.001 && Math.Abs(c.Y - corner.Item2) < 0.001),
                $"corner {corner} is missing from [{string.Join(" ", patch.Select(c => $"{c.X:F2},{c.Y:F2}"))}]");
    }

    /// <summary>
    /// A point standing straight closes its diagonal arm, and that arm ends level — cut with the
    /// track that passes over it, not square across itself. Cutting it square is what made every
    /// point on the diagram look wrong.
    /// </summary>
    [TestMethod]
    [DataRow(-40.0, 40.0, "diverging down and left")]
    [DataRow(40.0, 40.0, "diverging down and right")]
    [DataRow(-40.0, -40.0, "diverging up and left")]
    [DataRow(40.0, -40.0, "diverging up and right")]
    public void APointStandingStraightCutsItsDiagonalArmLevel(double armX, double armY, string _)
    {
        // The closed arm is the explicit end itself, so the cut direction says nothing about it.
        var patch = TrackCutOff.AtPoint(0, 0, armX, armY, armX, armY, TrackThickness);
        Assert.IsNotNull(patch);

        AssertLevel(Edge(patch, 0, 1), "the arm should end level with the track that passes over");
        AssertLevel(Edge(patch, 2, 3), "and so should the edge opposite it");
        Assert.AreEqual(TrackThickness, Math.Abs(patch[2].Y - patch[0].Y), 0.001,
            "the two level edges stand a track's thickness apart");
        AssertParallel(Normalized(armX, armY), Edge(patch, 1, 2), "the other pair runs along the closed arm");
    }

    /// <summary>
    /// The break spans the track it cuts and is a little wider than the rails are thick, so the gap
    /// is visible without eating into the switch beside it.
    /// </summary>
    [TestMethod]
    public void APointsBreakIsATrackThickAndALittleWider()
    {
        var patch = TrackCutOff.AtPoint(0, 0, 100, 0, 40, -40, TrackThickness)!;
        var acrossTheArm = Normalized(0, 1);

        var reach = patch.Select(corner => corner.X * acrossTheArm.X + corner.Y * acrossTheArm.Y).ToList();
        Assert.AreEqual(TrackThickness, reach.Max() - reach.Min(), 0.001, "across the closed arm");

        var alongTheCut = Normalized(40, -40);
        var span = patch.Select(corner => corner.X * alongTheCut.Y - corner.Y * alongTheCut.X).ToList();
        Assert.AreEqual(TrackThickness + 1, span.Max() - span.Min(), 0.001, "along it");
    }

    /// <summary>
    /// The break stands clear of the switch: a gap in the track rather than a bite out of the point.
    /// </summary>
    [TestMethod]
    [DataRow(100.0, 0.0, "level arm")]
    [DataRow(-100.0, 0.0, "level arm the other way")]
    [DataRow(-40.0, 40.0, "diagonal arm")]
    public void APointsBreakStandsClearOfTheSwitch(double armX, double armY, string _)
    {
        var patch = TrackCutOff.AtPoint(0, 0, armX, armY, -40, 40, TrackThickness)!;
        var arm = Normalized(armX, armY);

        var nearest = patch.Min(corner => corner.X * arm.X + corner.Y * arm.Y);
        Assert.AreEqual(1.0, nearest, 0.001, "the nearest corner sits a pixel out along the arm");
    }

    /// <summary>
    /// A level cut direction has no slant to give the patch. Nothing in the topology asks for one —
    /// a point's explicit end is its diagonal arm — and rather than draw a lozenge the length of the
    /// yard, nothing is drawn.
    /// </summary>
    [TestMethod]
    public void APointWhoseCutHasNoSlantDrawsNoBreak() =>
        Assert.IsNull(TrackCutOff.AtPoint(0, 0, 100, 0, 40, 0, TrackThickness));

    #endregion

    #region At a plain crossing

    /// <summary>
    /// Every arm of a crossing is a diagonal, and each is skew to the track that cuts it, so the
    /// break is measured in the arm's own frame: one pair of edges along the arm, the other pair
    /// parallel to the cut, so it reads as a gap in that track and not as a lozenge laid over it.
    /// </summary>
    [TestMethod]
    [DataRow(100.0, 0.0, "level arm")]
    [DataRow(-100.0, 0.0, "level arm the other way")]
    [DataRow(-40.0, -40.0, "45 degrees up and left")]
    [DataRow(40.0, 40.0, "45 degrees down and right")]
    [DataRow(40.0, -40.0, "45 degrees up and right")]
    public void BreakFollowsTheArmItCuts(double armX, double armY, string _)
    {
        // The cut is made square across the arm, which is the crossing's case.
        var patch = TrackCutOff.AtCrossing(0, 0, armX, armY, -armY, armX, TrackThickness);
        Assert.IsNotNull(patch);
        Assert.HasCount(4, patch);

        var arm = Normalized(armX, armY);
        AssertParallel(arm, Edge(patch, 0, 1), "the first pair of edges should run along the arm");
        AssertParallel(arm, Edge(patch, 2, 3), "and so should the pair opposite it");
    }

    /// <summary>
    /// The break spans the track it cuts and is a little wider than the rails are thick, so the gap
    /// is visible without eating into the crossing beside it.
    /// </summary>
    [TestMethod]
    public void BreakIsATrackThickAndALittleWider()
    {
        var patch = TrackCutOff.AtCrossing(0, 0, -40, -40, 40, -40, TrackThickness)!;

        Assert.AreEqual(TrackThickness + 1, Length(Edge(patch, 0, 1)), 0.001, "along the arm");
        Assert.AreEqual(TrackThickness, Length(Edge(patch, 1, 2)), 0.001, "across it");
    }

    /// <summary>
    /// The two ways cross, so the closed arm leaves the crossing underneath the rails of the way
    /// that is laid. The break begins a pixel beyond those rails — measuring a pixel from the
    /// crossing itself instead painted a bite out of the track that passes over.
    /// </summary>
    [TestMethod]
    [DataRow(-40.0, -40.0, 40.0, -40.0, "square crossing")]
    [DataRow(40.0, 40.0, -40.0, 40.0, "square crossing the other way")]
    [DataRow(-80.0, -40.0, 40.0, -40.0, "a shallower one, where the rails reach further along the arm")]
    public void BreakStandsClearOfTheTrackThatPassesOver(
        double armX, double armY, double passingX, double passingY, string _)
    {
        var patch = TrackCutOff.AtCrossing(0, 0, armX, armY, passingX, passingY, TrackThickness)!;

        var nearest = patch.Min(corner => DistanceAcross(corner, Normalized(passingX, passingY)));
        Assert.AreEqual(TrackThickness / 2 + 1, nearest, 0.001,
            "the nearest corner clears the passing rails by a pixel");
    }

    /// <summary>
    /// The break still lies on the closed arm and on the crossing's side of its far end: clearing
    /// the passing track moves it out, it must not move it off.
    /// </summary>
    [TestMethod]
    public void BreakStaysOnTheArmItCuts()
    {
        var patch = TrackCutOff.AtCrossing(0, 0, -40, -40, 40, -40, TrackThickness)!;
        var arm = Normalized(-40, -40);

        var reach = patch.Select(corner => corner.X * arm.X + corner.Y * arm.Y).ToList();
        Assert.IsGreaterThan(0, reach.Min(), "the break is on the wrong side of the crossing");
        Assert.IsLessThan(Length((-40, -40)), reach.Max(), "the break runs past the end of the arm");
    }

    /// <summary>
    /// A cut direction parallel to the arm says nothing, so the break is made square across that
    /// arm instead. A crossing never asks for it — its two ways cross — but the fallback is what
    /// keeps a malformed one from drawing a lozenge.
    /// </summary>
    [TestMethod]
    public void ACutParallelToTheArmIsMadeSquareAcrossItInstead()
    {
        var alongTheArm = TrackCutOff.AtCrossing(0, 0, -40, 40, -40, 40, TrackThickness);
        var squareAcross = TrackCutOff.AtCrossing(0, 0, -40, 40, -40, -40, TrackThickness);

        Assert.IsNotNull(alongTheArm);
        Assert.IsNotNull(squareAcross);
        for (var i = 0; i < 4; i++)
        {
            Assert.AreEqual(squareAcross[i].X, alongTheArm[i].X, 0.001);
            Assert.AreEqual(squareAcross[i].Y, alongTheArm[i].Y, 0.001);
        }
    }

    #endregion

    private static (double X, double Y) Normalized(double x, double y)
    {
        var length = Math.Sqrt(x * x + y * y);
        return (x / length, y / length);
    }

    private static (double X, double Y) Edge((double X, double Y)[] patch, int from, int to) =>
        (patch[to].X - patch[from].X, patch[to].Y - patch[from].Y);

    private static double Length((double X, double Y) edge) =>
        Math.Sqrt(edge.X * edge.X + edge.Y * edge.Y);

    /// <summary>How far a corner stands from the centre line of a track running one way.</summary>
    private static double DistanceAcross((double X, double Y) corner, (double X, double Y) track) =>
        Math.Abs(corner.X * track.Y - corner.Y * track.X);

    private static void AssertLevel((double X, double Y) edge, string message) =>
        Assert.AreEqual(0.0, edge.Y, 0.001, message);

    private static void AssertParallel((double X, double Y) direction, (double X, double Y) edge, string message)
    {
        var cross = direction.X * edge.Y - direction.Y * edge.X;
        Assert.AreEqual(0.0, cross, 0.001, message);
    }
}
