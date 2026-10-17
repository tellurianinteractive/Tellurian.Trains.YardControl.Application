namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Which track detection stretches report a train standing on them, per station.
/// The same shape as <see cref="ISignalStateService"/>: state arrives from the hardware and is
/// only read by the diagram, since occupancy is something the yard reports rather than
/// something the interlocking decides.
/// </summary>
public interface IOccupancySectionService
{
    event Action<OccupancyFeedback>? OccupancyChanged;

    bool IsOccupied(string stationName, int sectionNumber);

    IReadOnlyDictionary<int, bool> GetAllOccupancies(string stationName);

    /// <summary>
    /// Reports occupancy from somewhere other than a detector — a stretch without a feedback
    /// address, or the development stand-in for hardware that is not there.
    /// </summary>
    void SetOccupied(string stationName, int sectionNumber, bool isOccupied);
}

public record OccupancyFeedback(string StationName, int SectionNumber, bool IsOccupied);
