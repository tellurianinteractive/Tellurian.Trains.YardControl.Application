namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// A track detection stretch (<i>spårledning</i>): an isolated piece of track the hardware can
/// report as occupied. A stretch has no direction — it detects a movement coming from either
/// end — so <see cref="From"/> and <see cref="To"/> only give the section one canonical
/// spelling in the station file, ordered column-first like every other coordinate pair.
/// </summary>
/// <param name="Number">Section identifier, unique within the station.</param>
/// <param name="From">One end of the stretch.</param>
/// <param name="To">The other end.</param>
/// <param name="Address">
/// The feedback address the stretch reports occupancy on, or null while it is not connected to
/// any detector. A section without an address is drawn but never turns red on its own. Several
/// stretches may share an address: where the track branches they are the ways through one piece
/// of isolated rail, which is one detector, and the points tell them apart.
/// </param>
/// <param name="RequiresManualRelease">
/// True when the stretch keeps reporting occupied after the detector has let it go, until an
/// operator frees it by hand. A current detector sees the locomotive and nothing else: uncouple
/// it and drive it away, and the rail falls silent while the wagons are still standing there.
/// Holding the indication until someone says otherwise is the safe direction — the stretch is
/// occupied until a person has looked at it.
/// <para>
/// Only meaningful with an <see cref="Address"/>: a stretch with no detector behind it never
/// reports free on its own, so there is no automatic indication to override. The parser drops
/// the flag from an unaddressed stretch and warns.
/// </para>
/// </param>
public record OccupancySection(int Number, GridCoordinate From, GridCoordinate To, int? Address = null, bool RequiresManualRelease = false)
{
    public bool HasAddress => Address is > 0;

    /// <summary>
    /// True when this stretch latches: it has a detector to fall silent, and has been declared
    /// as one an operator frees by hand.
    /// </summary>
    public bool HoldsOccupancy => HasAddress && RequiresManualRelease;

    public override string ToString() => $"{Number}{(RequiresManualRelease ? "!" : "")}:{From},{To}";
}

/// <summary>
/// Where a detection stretch lies on the diagram.
/// </summary>
/// <param name="Links">
/// The track the stretch covers, in order from one end of the stretch to the other. The order is
/// what lets <see cref="OccupancySectionExtensions"/> tell which part of the stretch the points
/// still connect to an end.
/// </param>
/// <param name="IsExclusive">
/// True when no other stretch covers any of these links. Stretches overlap at the points, where
/// several of them run over the same throat; the tracks a train actually stands on are covered
/// by one stretch alone.
/// </param>
public record OccupancySectionTrack(IReadOnlyList<TrackLink> Links, bool IsExclusive);
