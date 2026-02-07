namespace Tellurian.Trains.YardController.Model;

public static class TrackGraphExtensions
{
    /// <summary>
    /// Deduces the straight arm endpoint for a point by examining the graph topology.
    /// The straight arm is the connection from the switch point that is NOT the diverging end.
    /// </summary>
    public static GridCoordinate DeduceStraightArm(this TrackGraph graph, PointDefinition point)
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
            .Where(c => c != point.DivergingEnd)
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
    /// Finds the path of track links between two coordinates using directed BFS.
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
