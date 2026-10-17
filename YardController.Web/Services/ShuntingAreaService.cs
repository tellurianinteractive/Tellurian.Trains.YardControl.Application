using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Holds which shunting areas are released, per station. An area not mentioned is
/// <see cref="ShuntingAreaState.Locked"/> — the normal state everything starts in.
/// </summary>
public sealed class ShuntingAreaService : IShuntingAreaService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, ShuntingAreaState>> _states =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _requests =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action<ShuntingAreaFeedback>? ShuntingAreaChanged;
    public event Action<ShuntingAreaRequestFeedback>? ReleaseRequestChanged;

    public ShuntingAreaState GetState(string stationName, int areaNumber) =>
        _states.TryGetValue(stationName, out var areas) && areas.TryGetValue(areaNumber, out var state)
            ? state
            : ShuntingAreaState.Locked;

    public IReadOnlyDictionary<int, ShuntingAreaState> GetAllStates(string stationName) =>
        _states.TryGetValue(stationName, out var areas)
            ? new Dictionary<int, ShuntingAreaState>(areas)
            : new Dictionary<int, ShuntingAreaState>();

    public void SetState(string stationName, int areaNumber, ShuntingAreaState state)
    {
        var areas = _states.GetOrAdd(stationName, _ => new ConcurrentDictionary<int, ShuntingAreaState>());
        var previous = areas.TryGetValue(areaNumber, out var existing) ? existing : ShuntingAreaState.Locked;
        areas[areaNumber] = state;
        if (previous == state) return;
        // The dispatcher has answered the request by acting on the area, either way.
        SetReleaseRequest(stationName, areaNumber, false);
        ShuntingAreaChanged?.Invoke(new ShuntingAreaFeedback(stationName, areaNumber, state));
    }

    public bool IsReleaseRequested(string stationName, int areaNumber) =>
        _requests.TryGetValue(stationName, out var areas) && areas.TryGetValue(areaNumber, out var requested) && requested;

    public IReadOnlyList<int> ReleaseRequests(string stationName) =>
        _requests.TryGetValue(stationName, out var areas)
            ? [.. areas.Where(a => a.Value).Select(a => a.Key).Order()]
            : [];

    public void SetReleaseRequest(string stationName, int areaNumber, bool requested)
    {
        var areas = _requests.GetOrAdd(stationName, _ => new ConcurrentDictionary<int, bool>());
        var previous = areas.TryGetValue(areaNumber, out var existing) && existing;
        areas[areaNumber] = requested;
        if (previous != requested)
            ReleaseRequestChanged?.Invoke(new ShuntingAreaRequestFeedback(stationName, areaNumber, requested));
    }
}
