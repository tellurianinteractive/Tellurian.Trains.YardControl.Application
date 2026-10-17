using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Wraps an <see cref="IOccupancySectionService"/> and holds the occupied indication of the
/// stretches declared with <see cref="OccupancySection.RequiresManualRelease"/> after their
/// detector has let go.
/// <para>
/// A current detector reports the locomotive, not the train. A train arrives at a goods track, the
/// loco is uncoupled and driven away, and the rail falls silent with the wagons still standing on
/// it — the one failure of track detection that no amount of tuning fixes, because there is
/// genuinely no current to read. So a stretch declared for manual release keeps its indication
/// when the detector releases it, and only an operator takes it back.
/// </para>
/// <para>
/// It sits in front of the real service rather than inside it, so the same latch applies to the
/// LocoNet and Z21 detectors in production and to the diagram's simulation in development, and
/// neither implementation has to know about it. State is in memory only: a restart clears the
/// board, which is what the start of an operating session wants.
/// </para>
/// </summary>
public sealed partial class LatchingOccupancySectionService : IOccupancySectionService, IOccupancyHoldService, IDisposable
{
    private readonly IOccupancySectionService _inner;
    private readonly IYardDataService _yardDataService;
    private readonly ILogger<LatchingOccupancySectionService> _logger;
    private readonly ConcurrentDictionary<(string StationName, int SectionNumber), byte> _held = new();

    public LatchingOccupancySectionService(
        IOccupancySectionService inner,
        IYardDataService yardDataService,
        ILogger<LatchingOccupancySectionService> logger)
    {
        _inner = inner;
        _yardDataService = yardDataService;
        _logger = logger;
        _inner.OccupancyChanged += OnInnerOccupancyChanged;
    }

    public event Action<OccupancyFeedback>? OccupancyChanged;
    public event Action<OccupancyHoldFeedback>? HoldChanged;

    public void Dispose() => _inner.OccupancyChanged -= OnInnerOccupancyChanged;

    public bool IsOccupied(string stationName, int sectionNumber) =>
        _inner.IsOccupied(stationName, sectionNumber) || IsHeld(stationName, sectionNumber);

    public IReadOnlyDictionary<int, bool> GetAllOccupancies(string stationName)
    {
        var occupancies = new Dictionary<int, bool>(_inner.GetAllOccupancies(stationName));
        foreach (var sectionNumber in HeldSections(stationName)) occupancies[sectionNumber] = true;
        return occupancies;
    }

    public void SetOccupied(string stationName, int sectionNumber, bool isOccupied) =>
        _inner.SetOccupied(stationName, sectionNumber, isOccupied);

    public bool IsHeld(string stationName, int sectionNumber) =>
        _held.ContainsKey((stationName, sectionNumber));

    public IReadOnlyList<int> HeldSections(string stationName) =>
        [.. _held.Keys
            .Where(key => key.StationName.Equals(stationName, StringComparison.OrdinalIgnoreCase))
            .Select(key => key.SectionNumber)
            .Order()];

    public bool Hold(string stationName, int sectionNumber)
    {
        // A hold answers a detector. Without one there is nothing to answer and nothing to release
        // it either, so the stretch would stay red for the rest of the session.
        if (FindSection(stationName, sectionNumber) is not { HasAddress: true }) return false;
        if (!_held.TryAdd((stationName, sectionNumber), 0)) return true;

        LogHeld(sectionNumber, stationName);
        NotifyHold(stationName, sectionNumber, isHeld: true);
        return true;
    }

    public bool Release(string stationName, int sectionNumber)
    {
        if (!_held.TryRemove((stationName, sectionNumber), out _)) return false;

        LogReleased(sectionNumber, stationName);
        NotifyHold(stationName, sectionNumber, isHeld: false);
        return true;
    }

    public int ReleaseAll(string stationName)
    {
        var released = 0;
        foreach (var sectionNumber in HeldSections(stationName))
            if (Release(stationName, sectionNumber)) released++;
        return released;
    }

    /// <summary>
    /// Turns the detector's report into what the diagram is told. Going occupied drops any hold —
    /// the detector has taken the stretch back, and it may latch again the next time it lets go.
    /// Going free latches a stretch declared for manual release, and the free report is swallowed:
    /// as far as everything downstream is concerned the stretch never stopped being occupied.
    /// </summary>
    private void OnInnerOccupancyChanged(OccupancyFeedback feedback)
    {
        if (feedback.IsOccupied)
        {
            if (_held.TryRemove((feedback.StationName, feedback.SectionNumber), out _))
            {
                LogReleasedByDetector(feedback.SectionNumber, feedback.StationName);
                HoldChanged?.Invoke(new OccupancyHoldFeedback(feedback.StationName, feedback.SectionNumber, false));
            }
            OccupancyChanged?.Invoke(feedback);
            return;
        }

        if (FindSection(feedback.StationName, feedback.SectionNumber) is { HoldsOccupancy: true }
            && _held.TryAdd((feedback.StationName, feedback.SectionNumber), 0))
        {
            LogHeldAfterDetectorReleased(feedback.SectionNumber, feedback.StationName);
            HoldChanged?.Invoke(new OccupancyHoldFeedback(feedback.StationName, feedback.SectionNumber, true));
            return;
        }

        if (IsHeld(feedback.StationName, feedback.SectionNumber)) return;
        OccupancyChanged?.Invoke(feedback);
    }

    /// <summary>
    /// A hold changing changes what the diagram must paint, so it is announced on both events:
    /// <see cref="HoldChanged"/> for the mark, <see cref="OccupancyChanged"/> for the colour. The
    /// occupancy carried is the one that now stands, which after a release is still occupied when
    /// the detector is reporting the stretch as well.
    /// </summary>
    private void NotifyHold(string stationName, int sectionNumber, bool isHeld)
    {
        HoldChanged?.Invoke(new OccupancyHoldFeedback(stationName, sectionNumber, isHeld));
        OccupancyChanged?.Invoke(new OccupancyFeedback(stationName, sectionNumber, IsOccupied(stationName, sectionNumber)));
    }

    private OccupancySection? FindSection(string stationName, int sectionNumber) =>
        _yardDataService.GetStationData(stationName)?.OccupancySections
            .FirstOrDefault(section => section.Number == sectionNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Occupancy section {Section} is held occupied after its detector released it (station {Station})")]
    private partial void LogHeldAfterDetectorReleased(int section, string station);

    [LoggerMessage(Level = LogLevel.Information, Message = "Occupancy section {Section} is held occupied by hand (station {Station})")]
    private partial void LogHeld(int section, string station);

    [LoggerMessage(Level = LogLevel.Information, Message = "Occupancy section {Section} was freed by hand (station {Station})")]
    private partial void LogReleased(int section, string station);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Occupancy section {Section} is reported occupied again, so its hold is no longer needed (station {Station})")]
    private partial void LogReleasedByDetector(int section, string station);
}
