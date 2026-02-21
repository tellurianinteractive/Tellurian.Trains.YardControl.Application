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
/// A point (switch/turnout) with its switch coordinate and an explicitly specified arm.
/// The other arm is deduced from the network topology.
/// </summary>
/// <param name="Label">Point identifier (e.g., "2a", "6")</param>
/// <param name="SwitchPoint">Where the point mechanism is located</param>
/// <param name="ExplicitEnd">End of the explicitly specified arm</param>
/// <param name="Direction">Forward (>) or Backward (&lt;)</param>
/// <param name="ExplicitEndIsStraight">If true, the explicit end is the straight arm (marked with + suffix)</param>
public record PointDefinition(
    string Label,
    GridCoordinate SwitchPoint,
    GridCoordinate ExplicitEnd,
    DivergeDirection Direction,
    bool ExplicitEndIsStraight = false);

/// <summary>
/// A signal at a specific coordinate with driving direction.
/// </summary>
/// <param name="Name">Signal identifier</param>
/// <param name="Coordinate">Position of the signal</param>
/// <param name="DrivesRight">True if train drives right ('>'), false for left ('&lt;')</param>
/// <param name="Type">Signal type (hidden, outbound main, inbound main, main dwarf, shunting dwarf)</param>
public record SignalDefinition(string Name, GridCoordinate Coordinate, bool DrivesRight, SignalType Type = SignalType.Default, string? Label = null)
{
    public bool IsHidden => Type == SignalType.Hidden;
    public bool IsVisible => !IsHidden;
    /// <summary>
    /// Returns the label if set, otherwise the name.
    /// </summary>
    public string DisplayText => Label ?? Name;
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
