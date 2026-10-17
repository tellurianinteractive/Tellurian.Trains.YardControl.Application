using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Development-mode implementation of <see cref="IOccupancySectionService"/>. There is no
/// detector behind it, so every stretch starts free and only changes when something reports it
/// through <see cref="SetOccupied"/> — the diagram's occupancy simulation.
/// </summary>
public sealed partial class LoggingOccupancySectionService : IOccupancySectionService
{
    private readonly ILogger<LoggingOccupancySectionService> _logger;
    private readonly ConcurrentDictionary<(string StationName, int SectionNumber), bool> _occupancies = new();

    public LoggingOccupancySectionService(ILogger<LoggingOccupancySectionService> logger)
    {
        _logger = logger;
    }

    public event Action<OccupancyFeedback>? OccupancyChanged;

    public bool IsOccupied(string stationName, int sectionNumber) =>
        _occupancies.TryGetValue((stationName, sectionNumber), out var occupied) && occupied;

    public IReadOnlyDictionary<int, bool> GetAllOccupancies(string stationName) =>
        _occupancies
            .Where(kvp => kvp.Key.StationName.Equals(stationName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key.SectionNumber, kvp => kvp.Value);

    public void SetOccupied(string stationName, int sectionNumber, bool isOccupied)
    {
        _occupancies[(stationName, sectionNumber)] = isOccupied;
        LogOccupancyChanged(sectionNumber, isOccupied ? "occupied" : "free", stationName);
        OccupancyChanged?.Invoke(new OccupancyFeedback(stationName, sectionNumber, isOccupied));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Occupancy section {Section} is {State} (station {Station})")]
    private partial void LogOccupancyChanged(int section, string state, string station);
}
