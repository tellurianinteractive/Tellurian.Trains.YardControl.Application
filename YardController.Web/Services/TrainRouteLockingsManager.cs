using System.Collections.Concurrent;

namespace YardController.Web.Services;

/// <summary>
/// Manages per-station TrainRouteLockings instances.
/// Each station has its own independent set of point locks and route state.
/// </summary>
public sealed class TrainRouteLockingsManager(ILoggerFactory loggerFactory)
{
    private readonly ConcurrentDictionary<string, TrainRouteLockings> _lockings = new(StringComparer.OrdinalIgnoreCase);

    public TrainRouteLockings GetForStation(string stationName) =>
        _lockings.GetOrAdd(stationName, _ => new TrainRouteLockings(loggerFactory.CreateLogger<TrainRouteLockings>()));

    /// <summary>
    /// Returns all station lockings (for shutdown/cleanup scenarios).
    /// </summary>
    public IEnumerable<(string StationName, TrainRouteLockings Lockings)> All =>
        _lockings.Select(kv => (kv.Key, kv.Value));
}
