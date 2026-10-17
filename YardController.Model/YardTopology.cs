using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace Tellurian.Trains.YardController.Model;

public record YardTopology(
    string Name,
    TrackGraph Graph,
    IReadOnlyList<PointDefinition> Points,
    IReadOnlyList<SignalDefinition> Signals,
    IReadOnlyList<LabelDefinition> Labels,
    IReadOnlyList<GapDefinition> Gaps,
    IReadOnlyList<PlatformDefinition> Platforms,
    IReadOnlySet<GridCoordinate> ForcedNecessaryCoordinates,
    IReadOnlyList<CrossingDefinition> Crossings)
{
    public static YardTopology Empty =>
        new("", new TrackGraph(), [], [], [], [], [], new HashSet<GridCoordinate>(), []);

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
/// A plain crossing (<i>korsning</i>): two tracks crossing at one coordinate with nothing on it
/// that moves. Because it has no point of its own, which way through it is open is read off the
/// points that lead into it, one per way — and because the two ways share the same rails, only
/// one of them may ever be laid.
/// </summary>
/// <param name="Coordinate">Where the two tracks cross.</param>
/// <param name="Ways">The ways through the crossing, at most one of which may be open at a time.</param>
public record CrossingDefinition(GridCoordinate Coordinate, IReadOnlyList<CrossingWay> Ways)
{
    public override string ToString() => $"{Coordinate}: {string.Join(' ', Ways)}";
}

/// <summary>
/// One way through a crossing: the point position that lays a movement along it, and the
/// positions every other point must be held at while it is laid. The conditions are what keep
/// the other way clear — they are the crossing's flank protection, declared once here instead of
/// written out in every route that runs over it.
/// </summary>
/// <param name="Governing">The point and position that lay this way through the crossing.</param>
/// <param name="Conditions">The positions that must hold while this way is laid.</param>
public record CrossingWay(PointCommand Governing, IReadOnlyList<PointCommand> Conditions)
{
    public override string ToString() =>
        $"({Governing.Number}{Governing.Position.Sign}|{string.Join(',', Conditions.Select(c => $"{c.Number}{c.Position.Sign}"))})";
}

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
/// A platform (<i>plattform</i>) running alongside a stretch of track, drawn as a band beside
/// the rails rather than on them: it is where passengers stand, not something a train runs over.
/// </summary>
/// <param name="From">One end of the stretch the platform serves.</param>
/// <param name="To">The other end.</param>
/// <param name="Side">Which side of the track the platform lies on.</param>
public record PlatformDefinition(GridCoordinate From, GridCoordinate To, PlatformSide Side);

/// <summary>
/// A gap (occupancy divider/insulated joint) at a node or on a link.
/// </summary>
/// <param name="Coordinate">Primary coordinate</param>
/// <param name="LinkEnd">If set, gap is on the link between Coordinate and LinkEnd</param>
public record GapDefinition(GridCoordinate Coordinate, GridCoordinate? LinkEnd = null);
