namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Which track detection stretches are held occupied by hand rather than by their detector.
/// <para>
/// A current detector reports the locomotive, not the train: uncouple it at an arrival track and
/// drive it away, and the rail goes quiet while the wagons stand where they were left. A stretch
/// declared with <see cref="OccupancySection.RequiresManualRelease"/> therefore keeps its occupied
/// indication when the detector lets go, and only an operator can take it back — which is the safe
/// direction, since the alternative is a track the diagram calls free with stock standing on it.
/// </para>
/// <para>
/// Held is not a second kind of occupancy: <see cref="IOccupancySectionService.IsOccupied"/> reports
/// a held stretch as occupied like any other, because it is. This interface exists only so the
/// diagram can mark which indications will not clear themselves, and so the operator can clear them.
/// </para>
/// </summary>
public interface IOccupancyHoldService
{
    event Action<OccupancyHoldFeedback>? HoldChanged;

    /// <summary>True while the stretch is occupied only because it is being held.</summary>
    bool IsHeld(string stationName, int sectionNumber);

    /// <summary>The stretches this station is holding, in ascending section order.</summary>
    IReadOnlyList<int> HeldSections(string stationName);

    /// <summary>
    /// Holds the stretch occupied by hand. Reports false when the stretch has no detector behind
    /// it, since a hold is an answer to a detector and there is nothing there to answer.
    /// </summary>
    bool Hold(string stationName, int sectionNumber);

    /// <summary>
    /// Frees a held stretch. The indication only goes out if the detector is not reporting it
    /// occupied as well: releasing the hold says the operator has looked, not that the rail is quiet.
    /// </summary>
    bool Release(string stationName, int sectionNumber);

    /// <summary>Frees every held stretch of the station and returns how many there were.</summary>
    int ReleaseAll(string stationName);
}

public record OccupancyHoldFeedback(string StationName, int SectionNumber, bool IsHeld);
