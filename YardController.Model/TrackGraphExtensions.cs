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
    /// Finds signals that are "exit signals" - the last signal in their driving direction
    /// with no further signals beyond them. These are typically signals at the station boundary
    /// where trains depart toward the next station.
    /// </summary>
    public static HashSet<string> FindExitSignals(this YardTopology topology)
    {
        var signalCoordinates = topology.Signals
            .Select(s => s.Coordinate)
            .ToHashSet();

        var exitSignals = new HashSet<string>();

        foreach (var signal in topology.Signals)
        {
            if (HasNoFurtherSignals(topology.Graph, signal.Coordinate, signal.DrivesRight, signalCoordinates))
                exitSignals.Add(signal.Name);
        }

        return exitSignals;
    }

    /// <summary>
    /// Checks whether there are no more signals beyond the given coordinate in the specified direction.
    /// Uses directed BFS to walk the graph and check for signal coordinates.
    /// </summary>
    private static bool HasNoFurtherSignals(
        TrackGraph graph,
        GridCoordinate start,
        bool drivesForward,
        HashSet<GridCoordinate> signalCoordinates)
    {
        var visited = new HashSet<GridCoordinate> { start };
        var queue = new Queue<GridCoordinate>();

        foreach (var neighbor in graph.GetDirectedAdjacentCoordinates(start, drivesForward))
        {
            if (visited.Add(neighbor))
                queue.Enqueue(neighbor);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (signalCoordinates.Contains(current))
                return false;

            foreach (var neighbor in graph.GetDirectedAdjacentCoordinates(current, drivesForward))
            {
                if (visited.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return true;
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
