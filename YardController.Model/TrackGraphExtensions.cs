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
    /// Finds the shortest path of track links between two coordinates using directed BFS.
    /// Returns a single path (the shortest by link count). If multiple paths exist, only the first found is returned.
    /// The route is deduced from signal positions and graph direction only, without point constraints.
    /// Returns an empty list if no path exists.
    /// </summary>
    public static IReadOnlyList<TrackLink> FindRoutePath(
        this TrackGraph graph,
        GridCoordinate start,
        GridCoordinate end,
        bool drivesForward)
    {
        var visited = new HashSet<GridCoordinate> { start };
        var parent = new Dictionary<GridCoordinate, GridCoordinate>();
        var queue = new Queue<GridCoordinate>();
        queue.Enqueue(start);
        var found = false;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == end)
            {
                found = true;
                break;
            }

            foreach (var neighbor in graph.GetDirectedAdjacentCoordinates(current, drivesForward))
            {
                if (visited.Contains(neighbor)) continue;
                visited.Add(neighbor);
                parent[neighbor] = current;
                queue.Enqueue(neighbor);
            }
        }

        if (!found) return [];

        // Reconstruct path and collect TrackLink objects
        var result = new List<TrackLink>();
        var coord = end;
        while (parent.TryGetValue(coord, out var prev))
        {
            var link = graph.GetLink(prev, coord);
            if (link is not null)
                result.Add(link);
            coord = prev;
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
