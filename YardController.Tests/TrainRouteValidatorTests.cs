using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using Tellurian.Trains.YardController.Model.Validation;

namespace YardController.Tests;

[TestClass]
public class TrainRouteValidatorTests
{
    private ILogger<TrainRouteValidator> _logger = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<TrainRouteValidator>();
    }

    #region Helper Methods

    /// <summary>
    /// Creates a simple test topology:
    ///
    ///   Signal21 ---- Point1 ---- Signal31
    ///                   |
    ///                Signal41
    ///
    /// Point1 at (1,2): straight goes to (1,4), diverging goes to (2,3)
    /// Signal21 at (1,0)
    /// Signal31 at (1,4)
    /// Signal41 at (2,3)
    /// </summary>
    private static YardTopology CreateSimpleTopology()
    {
        var graph = new TrackGraph();

        // Main line: (1,0) - (1,2) - (1,4)
        graph.TryAddLink(new GridCoordinate(1, 0), new GridCoordinate(1, 2));
        graph.TryAddLink(new GridCoordinate(1, 2), new GridCoordinate(1, 4));

        // Diverging branch: (1,2) - (2,3)
        graph.TryAddLink(new GridCoordinate(1, 2), new GridCoordinate(2, 3));

        var points = new List<PointDefinition>
        {
            // Point 1 at (1,2), diverging to (2,3)
            new("1", new GridCoordinate(1, 2), new GridCoordinate(2, 3), DivergeDirection.Forward)
        };

        var signals = new List<SignalDefinition>
        {
            new("21", new GridCoordinate(1, 0), true),
            new("31", new GridCoordinate(1, 4), false),
            new("41", new GridCoordinate(2, 3), false)
        };

        return new YardTopology("Test", graph, points, signals, [], [], new HashSet<GridCoordinate>());
    }

    /// <summary>
    /// Creates a topology with a crossover (two paired points):
    ///
    ///   Signal21 ---- Point2a ---- Signal31
    ///                   X
    ///   Signal22 ---- Point2b ---- Signal32
    ///
    /// Point2a at (1,2): diverging to (2,2)
    /// Point2b at (2,2): diverging to (1,2)
    /// </summary>
    private static YardTopology CreateCrossoverTopology()
    {
        var graph = new TrackGraph();

        // Upper track: (1,0) - (1,2) - (1,4)
        graph.TryAddLink(new GridCoordinate(1, 0), new GridCoordinate(1, 2));
        graph.TryAddLink(new GridCoordinate(1, 2), new GridCoordinate(1, 4));

        // Lower track: (2,0) - (2,2) - (2,4)
        graph.TryAddLink(new GridCoordinate(2, 0), new GridCoordinate(2, 2));
        graph.TryAddLink(new GridCoordinate(2, 2), new GridCoordinate(2, 4));

        // Crossover link: (1,2) - (2,2)
        graph.TryAddLink(new GridCoordinate(1, 2), new GridCoordinate(2, 2));

        var points = new List<PointDefinition>
        {
            // Point 2a at (1,2), diverging to (2,2)
            new("2a", new GridCoordinate(1, 2), new GridCoordinate(2, 2), DivergeDirection.Forward),
            // Point 2b at (2,2), diverging to (1,2)
            new("2b", new GridCoordinate(2, 2), new GridCoordinate(1, 2), DivergeDirection.Backward)
        };

        var signals = new List<SignalDefinition>
        {
            new("21", new GridCoordinate(1, 0), true),
            new("31", new GridCoordinate(1, 4), false),
            new("22", new GridCoordinate(2, 0), true),
            new("32", new GridCoordinate(2, 4), false)
        };

        return new YardTopology("Crossover Test", graph, points, signals, [], [], new HashSet<GridCoordinate>());
    }

    private static TrainRouteCommand CreateRoute(int from, int to, params (int number, PointPosition position, bool isOnRoute)[] points)
    {
        var pointCommands = points.Select(p => new PointCommand(p.number, p.position, null, p.isOnRoute)).ToList();
        return new TrainRouteCommand(from, to, TrainRouteState.SetMain, pointCommands);
    }

    #endregion

    #region Signal Validation Tests

    [TestMethod]
    public void ValidateRoute_ReturnsFalse_WhenFromSignalNotFound()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        var route = CreateRoute(99, 31, (1, PointPosition.Straight, true));

        var result = validator.ValidateRoute(route);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ValidateRoute_ReturnsFalse_WhenToSignalNotFound()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        var route = CreateRoute(21, 99, (1, PointPosition.Straight, true));

        var result = validator.ValidateRoute(route);

        Assert.IsFalse(result);
    }

    #endregion

    #region Point Validation Tests

    [TestMethod]
    public void ValidateRoute_ReturnsTrue_WhenPointNotFoundInTopology_ButPathExists()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        // Point 99 doesn't exist, but path between signals is valid
        var route = CreateRoute(21, 31, (99, PointPosition.Straight, true));

        var result = validator.ValidateRoute(route);

        Assert.IsTrue(result);
    }

    #endregion

    #region Path Validation Tests

    [TestMethod]
    public void ValidateRoute_ReturnsTrue_ForValidStraightRoute()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        // Route from 21 to 31 via point 1 set to straight
        var route = CreateRoute(21, 31, (1, PointPosition.Straight, true));

        var result = validator.ValidateRoute(route);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ValidateRoute_ReturnsTrue_ForValidDivergingRoute()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        // Route from 21 to 41 via point 1 set to diverging
        var route = CreateRoute(21, 41, (1, PointPosition.Diverging, true));

        var result = validator.ValidateRoute(route);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ValidateRoute_ReturnsTrue_RegardlessOfPointPosition()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        // Route from 21 to 31 with point 1 set to diverging — path is still valid
        // because validation uses directed BFS between signals, ignoring point positions
        var route = CreateRoute(21, 31, (1, PointPosition.Diverging, true));

        var result = validator.ValidateRoute(route);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ValidateRoute_ReturnsTrue_WhenPathReachableByDirection()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        // Route from 21 to 41 — reachable via directed BFS regardless of point position
        var route = CreateRoute(21, 41, (1, PointPosition.Straight, true));

        var result = validator.ValidateRoute(route);

        Assert.IsTrue(result);
    }

    #endregion

    #region Off-Route Points Tests

    [TestMethod]
    public void ValidateRoute_IgnoresOffRoutePoints()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        // Route from 21 to 31 with off-route point (should be ignored in path validation)
        var route = CreateRoute(21, 31,
            (1, PointPosition.Straight, true),  // On-route
            (99, PointPosition.Diverging, false)); // Off-route (doesn't exist, but should be ignored)

        var result = validator.ValidateRoute(route);

        // Should still pass because off-route point 99 is not validated for path
        Assert.IsTrue(result);
    }

    #endregion

    #region Route Without Points Tests

    [TestMethod]
    public void ValidateRoute_ReturnsTrue_ForRouteWithNoOnRoutePoints()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        // Route with only off-route points (no path validation needed)
        var route = CreateRoute(21, 31, (1, PointPosition.Straight, false));

        var result = validator.ValidateRoute(route);

        Assert.IsTrue(result);
    }

    #endregion

    #region Crossover Tests

    [TestMethod]
    public void ValidateRoute_ReturnsTrue_ForCrossoverStraight()
    {
        var topology = CreateCrossoverTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        // Route from 21 to 31 (upper track), point 2 (2a) set to straight
        var route = CreateRoute(21, 31, (2, PointPosition.Straight, true));

        var result = validator.ValidateRoute(route);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ValidateRoute_ReturnsTrue_ForCrossoverDiverging()
    {
        var topology = CreateCrossoverTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        // Route from 21 to 32 (crossover), point 2 (2a) set to diverging
        var route = CreateRoute(21, 32, (2, PointPosition.Diverging, true));

        var result = validator.ValidateRoute(route);

        Assert.IsTrue(result);
    }

    #endregion

    #region Batch Validation Tests

    [TestMethod]
    public void ValidateRoutes_ReturnsValidAndInvalidRoutes()
    {
        var topology = CreateSimpleTopology();
        var validator = new TrainRouteValidator(topology, _logger);

        var routes = new[]
        {
            CreateRoute(21, 31, (1, PointPosition.Straight, true)),  // Valid
            CreateRoute(21, 41, (1, PointPosition.Diverging, true)), // Valid
            CreateRoute(21, 31, (1, PointPosition.Diverging, true)), // Valid (path exists regardless of point position)
            CreateRoute(99, 31, (1, PointPosition.Straight, true))   // Invalid (signal not found)
        };

        var result = validator.ValidateRoutes(routes);

        Assert.HasCount(3, result.ValidRoutes);
        Assert.HasCount(1, result.InvalidRoutes);
        Assert.IsTrue(result.HasErrors);
        Assert.AreEqual(4, result.TotalRoutes);
    }

    #endregion
}
