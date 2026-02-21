using Tellurian.Trains.YardController.Model.Control;

namespace Tellurian.Trains.YardController.Model;

public static class TrackGraphExtensions
{
    /// <summary>
    /// Deduces the straight arm endpoint for a point.
    /// If the explicit end is marked as straight (+), returns it directly.
    /// Otherwise, deduces the other arm from graph topology (excluding the explicit end).
    /// </summary>
    public static GridCoordinate DeduceStraightArm(this TrackGraph graph, PointDefinition point)
    {
        if (point.ExplicitEndIsStraight)
            return point.ExplicitEnd;

        return DeduceOtherArm(graph, point);
    }

    /// <summary>
    /// Deduces the diverging arm endpoint for a point.
    /// If the explicit end is NOT marked as straight, returns it directly (default behaviour).
    /// Otherwise, deduces the other arm from graph topology (excluding the explicit end).
    /// </summary>
    public static GridCoordinate DeduceDivergingEnd(this TrackGraph graph, PointDefinition point)
    {
        if (!point.ExplicitEndIsStraight)
            return point.ExplicitEnd;

        return DeduceOtherArm(graph, point);
    }

    /// <summary>
    /// Deduces the arm that is NOT the explicit end by examining the graph topology.
    /// </summary>
    private static GridCoordinate DeduceOtherArm(TrackGraph graph, PointDefinition point)
    {
        var isForward = point.Direction == DivergeDirection.Forward;
        var node = graph.GetNode(point.SwitchPoint);

        if (node == null)
        {
            var offset = isForward ? 1 : -1;
            return new GridCoordinate(point.SwitchPoint.Row, point.SwitchPoint.Column + offset);
        }

        var connectedCoords = node.OutgoingLinks
            .Select(l => l.ToNode.Coordinate)
            .Concat(node.IncomingLinks.Select(l => l.FromNode.Coordinate))
            .Where(c => c != point.ExplicitEnd)
            .ToList();

        if (connectedCoords.Count == 0)
        {
            var offset = isForward ? 1 : -1;
            return new GridCoordinate(point.SwitchPoint.Row, point.SwitchPoint.Column + offset);
        }

        if (connectedCoords.Count == 1)
            return connectedCoords[0];

        // Forward point (>) - straight arm is to the RIGHT (higher column)
        // Backward point (<) - straight arm is to the LEFT (lower column)
        return connectedCoords
            .OrderBy(c => Math.Abs(c.Row - point.SwitchPoint.Row)) // Prefer horizontal
            .ThenBy(c => isForward ? -c.Column : c.Column) // Forward prefers right (desc), Backward prefers left (asc)
            .First();
    }

    /// <summary>
    /// Finds the physically shortest path between two coordinates using Dijkstra
    /// weighted by Euclidean distance, respecting point (turnout) constraints.
    /// At a point's switch coordinate, the path cannot cross from the straight arm
    /// to the diverging arm or vice versa - it must go through the common rail.
    /// When routePoints are provided, the path is forced through the specified arm
    /// (straight or diverging) at each constrained point.
    /// Uses edge-based state (current, previous) to track entry direction.
    /// Returns an empty list if no path exists.
    /// </summary>
    public static IReadOnlyList<TrackLink> FindRoutePath(
        this TrackGraph graph,
        GridCoordinate start,
        GridCoordinate end,
        bool drivesForward,
        IReadOnlyList<PointDefinition> pointDefinitions,
        IReadOnlyList<PointCommand>? routePoints = null)
    {
        var constraintsWithOwner = BuildPointConstraints(graph, pointDefinitions);
        var forcedExclusions = BuildForcedExclusions(constraintsWithOwner, routePoints, pointDefinitions);

        // Edge-based state: (current coordinate, previous coordinate)
        // Using previous=default for start state (GridCoordinate is a struct)
        var startState = (current: start, previous: default(GridCoordinate), hasParent: false);
        var dist = new Dictionary<(GridCoordinate current, GridCoordinate previous, bool hasParent), double>
        {
            [startState] = 0
        };
        var parent = new Dictionary<
            (GridCoordinate current, GridCoordinate previous, bool hasParent),
            (GridCoordinate current, GridCoordinate previous, bool hasParent)>();
        var queue = new PriorityQueue<
            (GridCoordinate current, GridCoordinate previous, bool hasParent), double>();
        queue.Enqueue(startState, 0);

        (GridCoordinate current, GridCoordinate previous, bool hasParent)? endState = null;

        while (queue.TryDequeue(out var state, out _))
        {
            if (state.current == end) { endState = state; break; }

            var neighbors = graph.GetDirectedAdjacentCoordinates(state.current, drivesForward);

            // Apply forced point position exclusions (from route point commands)
            if (forcedExclusions.TryGetValue(state.current, out var forced))
                neighbors = neighbors.Where(n => !forced.Contains(n));

            // Apply point constraints: prevent crossing between arms at switch points
            if (state.hasParent && constraintsWithOwner.TryGetValue(state.current, out var pointArms))
            {
                var excluded = new HashSet<GridCoordinate>();
                foreach (var (straight, diverging, _) in pointArms)
                {
                    if (state.previous == straight) excluded.Add(diverging);
                    else if (state.previous == diverging) excluded.Add(straight);
                }
                if (excluded.Count > 0)
                    neighbors = neighbors.Where(n => !excluded.Contains(n));
            }

            foreach (var neighbor in neighbors)
            {
                var dx = neighbor.Column - state.current.Column;
                var dy = neighbor.Row - state.current.Row;
                var weight = Math.Sqrt(dx * dx + dy * dy);
                var newDist = dist[state] + weight;

                var nextState = (current: neighbor, previous: state.current, hasParent: true);

                if (!dist.TryGetValue(nextState, out var oldDist) || newDist < oldDist)
                {
                    dist[nextState] = newDist;
                    parent[nextState] = state;
                    queue.Enqueue(nextState, newDist);
                }
            }
        }

        if (endState is null) return [];

        // Reconstruct path and collect TrackLink objects
        var result = new List<TrackLink>();
        var current = endState.Value;
        while (parent.TryGetValue(current, out var prev))
        {
            var link = graph.GetLink(prev.current, current.current);
            if (link is not null)
                result.Add(link);
            current = prev;
        }

        return result;
    }

    /// <summary>
    /// Builds a lookup from switch point coordinate to its (straight, diverging, owner) tuples.
    /// Used by FindRoutePath to enforce point constraints and by BuildForcedExclusions
    /// to apply route-specific exclusions only to the owning point's arms.
    /// </summary>
    private static Dictionary<GridCoordinate, List<(GridCoordinate straight, GridCoordinate diverging, PointDefinition point)>>
        BuildPointConstraints(TrackGraph graph, IReadOnlyList<PointDefinition> allPoints)
    {
        var result = new Dictionary<GridCoordinate, List<(GridCoordinate, GridCoordinate, PointDefinition)>>();

        foreach (var point in allPoints)
        {
            var straight = graph.DeduceStraightArm(point);
            var diverging = graph.DeduceDivergingEnd(point);

            if (!result.TryGetValue(point.SwitchPoint, out var list))
            {
                list = [];
                result[point.SwitchPoint] = list;
            }
            list.Add((straight, diverging, point));
        }

        return result;
    }

    /// <summary>
    /// Builds forced exclusions from route point commands.
    /// If a route specifies point N as straight, the diverging arm is excluded at that switch.
    /// If diverging, the straight arm is excluded. This forces the path through the correct arm.
    /// Only excludes arms belonging to the specific point definition, not other points at the same switch.
    /// </summary>
    private static Dictionary<GridCoordinate, HashSet<GridCoordinate>> BuildForcedExclusions(
        Dictionary<GridCoordinate, List<(GridCoordinate straight, GridCoordinate diverging, PointDefinition point)>> constraintsWithOwner,
        IReadOnlyList<PointCommand>? routePoints,
        IReadOnlyList<PointDefinition> allPoints)
    {
        var result = new Dictionary<GridCoordinate, HashSet<GridCoordinate>>();
        if (routePoints is null || routePoints.Count == 0) return result;

        // Map point number → point definitions
        var pointsByNumber = new Dictionary<int, List<PointDefinition>>();
        foreach (var p in allPoints)
        {
            var digits = new string(p.Label.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var number))
            {
                if (!pointsByNumber.TryGetValue(number, out var list))
                {
                    list = [];
                    pointsByNumber[number] = list;
                }
                list.Add(p);
            }
        }

        foreach (var cmd in routePoints)
        {
            if (!pointsByNumber.TryGetValue(cmd.Number, out var defs)) continue;

            foreach (var def in defs)
            {
                if (!constraintsWithOwner.TryGetValue(def.SwitchPoint, out var arms)) continue;

                foreach (var (straight, diverging, owner) in arms)
                {
                    // Only apply exclusion to the arm pair belonging to THIS point definition
                    if (owner != def) continue;

                    // Exclude the arm we DON'T want
                    var excluded = cmd.Position == PointPosition.Straight ? diverging : straight;

                    if (!result.TryGetValue(def.SwitchPoint, out var set))
                    {
                        set = [];
                        result[def.SwitchPoint] = set;
                    }
                    set.Add(excluded);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Given a BFS path through the graph and all point definitions,
    /// determines which points the path traverses and their required positions.
    /// Returns a list of PointCommands with IsOnRoute = true.
    /// </summary>
    public static List<PointCommand> DeriveRoutePoints(
        this TrackGraph graph,
        IReadOnlyList<TrackLink> path,
        IReadOnlyList<PointDefinition> allPoints)
    {
        // Build set of all coordinates on the path
        var pathCoords = new HashSet<GridCoordinate>();
        foreach (var link in path)
        {
            pathCoords.Add(link.FromNode.Coordinate);
            pathCoords.Add(link.ToNode.Coordinate);
        }

        // Group point definitions by switch point coordinate
        var pointsBySwitch = new Dictionary<GridCoordinate, List<PointDefinition>>();
        foreach (var p in allPoints)
        {
            if (!pointsBySwitch.TryGetValue(p.SwitchPoint, out var list))
            {
                list = [];
                pointsBySwitch[p.SwitchPoint] = list;
            }
            list.Add(p);
        }

        var result = new List<PointCommand>();
        var processedNumbers = new HashSet<int>();

        foreach (var coord in pathCoords)
        {
            if (!pointsBySwitch.TryGetValue(coord, out var defs)) continue;

            foreach (var pointDef in defs)
            {
                var number = ExtractPointNumber(pointDef.Label);
                if (number <= 0 || !processedNumbers.Add(number)) continue;

                var straightArm = graph.DeduceStraightArm(pointDef);
                var divergingEnd = graph.DeduceDivergingEnd(pointDef);

                // Determine which arm the path uses
                var useStraight = pathCoords.Contains(straightArm);
                var useDiverging = pathCoords.Contains(divergingEnd);

                if (useStraight && useDiverging)
                {
                    // Both arms in path - need to check which link is actually on the path
                    var straightOnPath = path.Any(l =>
                        (l.FromNode.Coordinate == coord && l.ToNode.Coordinate == straightArm) ||
                        (l.FromNode.Coordinate == straightArm && l.ToNode.Coordinate == coord));
                    var divergingOnPath = path.Any(l =>
                        (l.FromNode.Coordinate == coord && l.ToNode.Coordinate == divergingEnd) ||
                        (l.FromNode.Coordinate == divergingEnd && l.ToNode.Coordinate == coord));

                    var position = divergingOnPath && !straightOnPath
                        ? PointPosition.Diverging
                        : PointPosition.Straight;
                    result.Add(new PointCommand(number, position, IsOnRoute: true));
                }
                else if (useDiverging)
                {
                    result.Add(new PointCommand(number, PointPosition.Diverging, IsOnRoute: true));
                }
                else if (useStraight)
                {
                    result.Add(new PointCommand(number, PointPosition.Straight, IsOnRoute: true));
                }
            }
        }

        return result;
    }

    private static int ExtractPointNumber(string label)
    {
        var digits = new string(label.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : 0;
    }

    /// <summary>
    /// Builds a mapping from point number to PointDefinition(s).
    /// Extracts numeric part from labels like "27a", "27b" → 27.
    /// </summary>
    public static Dictionary<int, List<PointDefinition>> BuildPointNumberMapping(IReadOnlyList<PointDefinition> points)
    {
        var result = new Dictionary<int, List<PointDefinition>>();

        foreach (var point in points)
        {
            var digits = new string(point.Label.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var number))
            {
                if (!result.TryGetValue(number, out var list))
                {
                    list = [];
                    result[number] = list;
                }
                list.Add(point);
            }
        }

        return result;
    }
}
