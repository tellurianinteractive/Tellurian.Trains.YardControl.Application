namespace Tellurian.Trains.YardController.Model;

public record YardTopology(
    string Name,
    TrackGraph Graph,
    IReadOnlyList<PointDefinition> Points,
    IReadOnlyList<SignalDefinition> Signals,
    IReadOnlyList<LabelDefinition> Labels,
    IReadOnlyList<GapDefinition> Gaps,
    IReadOnlySet<GridCoordinate> ForcedNecessaryCoordinates)
{
    public static YardTopology Empty =>
        new("", new TrackGraph(), [], [], [], [], new HashSet<GridCoordinate>());

    public int MaxRow => Graph.MaxRow;
    public int MaxColumn => Graph.MaxColumn;
}

/// <summary>
/// A point (switch/turnout) with its switch coordinate and diverging arm.
/// The straight arm is deduced from the network topology.
/// </summary>
/// <param name="Label">Point identifier (e.g., "2a", "6")</param>
/// <param name="SwitchPoint">Where the point mechanism is located</param>
/// <param name="DivergingEnd">End of the diverging arm (explicitly specified)</param>
/// <param name="Direction">Forward (>) or Backward (&lt;)</param>
public record PointDefinition(
    string Label,
    GridCoordinate SwitchPoint,
    GridCoordinate DivergingEnd,
    DivergeDirection Direction);

/// <summary>
/// A signal at a specific coordinate with driving direction.
/// </summary>
/// <param name="Name">Signal identifier</param>
/// <param name="Coordinate">Position of the signal</param>
/// <param name="DrivesRight">True if train drives right ('>'), false for left ('&lt;')</param>
/// <param name="IsHidden">True if signal should not be displayed (fictive or hidden for other reasons)</param>
public record SignalDefinition(string Name, GridCoordinate Coordinate, bool DrivesRight, bool IsHidden = false)
{
    public bool IsVisible => !IsHidden;
}

/// <summary>
/// A text label positioned between two coordinates.
/// </summary>
/// <param name="Text">The label text</param>
/// <param name="Start">Start coordinate</param>
/// <param name="End">End coordinate</param>
public record LabelDefinition(string Text, GridCoordinate Start, GridCoordinate End);

/// <summary>
/// A gap (occupancy divider/insulated joint) at a node or on a link.
/// </summary>
/// <param name="Coordinate">Primary coordinate</param>
/// <param name="LinkEnd">If set, gap is on the link between Coordinate and LinkEnd</param>
public record GapDefinition(GridCoordinate Coordinate, GridCoordinate? LinkEnd = null);
