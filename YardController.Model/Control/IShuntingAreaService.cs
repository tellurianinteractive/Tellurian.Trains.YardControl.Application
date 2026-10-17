namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Tracks which shunting areas are released to a shunter, per station.
/// There is no hardware behind this — an area is released and locked by operator command only —
/// so unlike <see cref="ISignalStateService"/> the state is written through this same interface.
/// </summary>
public interface IShuntingAreaService
{
    event Action<ShuntingAreaFeedback>? ShuntingAreaChanged;

    /// <summary>Raised when a shunter asks for an area, or withdraws the request.</summary>
    event Action<ShuntingAreaRequestFeedback>? ReleaseRequestChanged;

    ShuntingAreaState GetState(string stationName, int areaNumber);
    IReadOnlyDictionary<int, ShuntingAreaState> GetAllStates(string stationName);

    /// <summary>Sets the area's state and raises <see cref="ShuntingAreaChanged"/> if it changed.</summary>
    void SetState(string stationName, int areaNumber, ShuntingAreaState state);

    /// <summary>
    /// True while a shunter has asked for the area and the dispatcher has not yet acted on it.
    /// The request is dropped as soon as the area changes state either way.
    /// </summary>
    bool IsReleaseRequested(string stationName, int areaNumber);

    /// <summary>The areas a shunter is currently asking for, in ascending number order.</summary>
    IReadOnlyList<int> ReleaseRequests(string stationName);

    void SetReleaseRequest(string stationName, int areaNumber, bool requested);
}

public record ShuntingAreaFeedback(string StationName, int AreaNumber, ShuntingAreaState State);

public record ShuntingAreaRequestFeedback(string StationName, int AreaNumber, bool IsRequested);
