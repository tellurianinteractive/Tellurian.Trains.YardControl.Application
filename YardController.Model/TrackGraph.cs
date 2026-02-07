namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// A node in the track graph, representing a position where tracks connect.
/// </summary>
public class TrackNode
{
    public GridCoordinate Coordinate { get; }

    /// <summary>Forward links FROM this node (toward higher coordinates)</summary>
    public List<TrackLink> OutgoingLinks { get; } = [];

    /// <summary>Links TO this node (from lower coordinates)</summary>
    public List<TrackLink> IncomingLinks { get; } = [];

    /// <summary>Total number of connections at this node</summary>
    public int Degree => OutgoingLinks.Count + IncomingLinks.Count;

    public TrackNode(GridCoordinate coordinate)
    {
        Coordinate = coordinate;
    }

    public override string ToString() => Coordinate.ToString();
}

/// <summary>
/// A link between two nodes in the track graph.
/// Link direction is determined by the topology file order (increasing columns = forward).
/// </summary>
public class TrackLink
{
    public TrackNode FromNode { get; }
    public TrackNode ToNode { get; }

    /// <summary>Indicates an occupancy divider (gap) on this link</summary>
    public bool HasGap { get; set; }

    public TrackLink(TrackNode fromNode, TrackNode toNode)
    {
        FromNode = fromNode;
        ToNode = toNode;
    }

    public override string ToString() => $"{FromNode.Coordinate}-{ToNode.Coordinate}";
}

/// <summary>
/// A graph representation of the track topology.
/// Nodes represent coordinate positions, links represent track connections.
/// </summary>
public class TrackGraph
{
    private readonly Dictionary<GridCoordinate, TrackNode> _nodes = [];
    private readonly List<TrackLink> _links = [];

    public IReadOnlyDictionary<GridCoordinate, TrackNode> Nodes => _nodes;
    public IReadOnlyList<TrackLink> Links => _links;

    /// <summary>
    /// Gets an existing node or creates a new one at the specified coordinate.
    /// </summary>
    public TrackNode GetOrCreateNode(GridCoordinate coord)
    {
        if (!_nodes.TryGetValue(coord, out var node))
        {
            node = new TrackNode(coord);
            _nodes[coord] = node;
        }
        return node;
    }

    /// <summary>
    /// Gets the node at the specified coordinate, or null if it doesn't exist.
    /// </summary>
    public TrackNode? GetNode(GridCoordinate coord)
    {
        return _nodes.GetValueOrDefault(coord);
    }

    /// <summary>
    /// Attempts to add a link between two coordinates.
    /// Returns false if the link already exists or coordinates are equal.
    /// Link direction is preserved from the topology file (caller determines direction).
    /// </summary>
    public bool TryAddLink(GridCoordinate from, GridCoordinate to)
    {
        if (from == to) return false;

        // Check for existing link (in either direction)
        if (GetLink(from, to) != null)
        {
            return false;
        }

        var fromNode = GetOrCreateNode(from);
        var toNode = GetOrCreateNode(to);

        var link = new TrackLink(fromNode, toNode);
        _links.Add(link);
        fromNode.OutgoingLinks.Add(link);
        toNode.IncomingLinks.Add(link);

        return true;
    }

    /// <summary>
    /// Gets the link between two coordinates (in either direction), or null if none exists.
    /// </summary>
    public TrackLink? GetLink(GridCoordinate coord1, GridCoordinate coord2)
    {
        return _links.FirstOrDefault(l =>
            (l.FromNode.Coordinate == coord1 && l.ToNode.Coordinate == coord2) ||
            (l.FromNode.Coordinate == coord2 && l.ToNode.Coordinate == coord1));
    }

    /// <summary>
    /// Gets all links connected to a node at the specified coordinate.
    /// </summary>
    public IEnumerable<TrackLink> GetLinksAt(GridCoordinate coord)
    {
        var node = GetNode(coord);
        if (node == null) yield break;

        foreach (var link in node.OutgoingLinks)
            yield return link;
        foreach (var link in node.IncomingLinks)
            yield return link;
    }

    /// <summary>
    /// Gets all coordinates adjacent to the specified coordinate.
    /// </summary>
    public IEnumerable<GridCoordinate> GetAdjacentCoordinates(GridCoordinate coord)
    {
        var node = GetNode(coord);
        if (node == null) yield break;

        foreach (var link in node.OutgoingLinks)
            yield return link.ToNode.Coordinate;
        foreach (var link in node.IncomingLinks)
            yield return link.FromNode.Coordinate;
    }

    /// <summary>
    /// Gets adjacent coordinates constrained by direction.
    /// Forward (drivesForward=true) returns only outgoing neighbors (toward higher columns).
    /// Backward (drivesForward=false) returns only incoming neighbors (toward lower columns).
    /// </summary>
    public IEnumerable<GridCoordinate> GetDirectedAdjacentCoordinates(GridCoordinate coord, bool drivesForward)
    {
        var node = GetNode(coord);
        if (node == null) yield break;

        if (drivesForward)
        {
            foreach (var link in node.OutgoingLinks)
                yield return link.ToNode.Coordinate;
        }
        else
        {
            foreach (var link in node.IncomingLinks)
                yield return link.FromNode.Coordinate;
        }
    }

    /// <summary>
    /// Gets the maximum row value across all nodes.
    /// </summary>
    public int MaxRow => _nodes.Count > 0 ? _nodes.Keys.Max(c => c.Row) : 0;

    /// <summary>
    /// Gets the maximum column value across all nodes.
    /// </summary>
    public int MaxColumn => _nodes.Count > 0 ? _nodes.Keys.Max(c => c.Column) : 0;
}
