using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Validation;

namespace YardController.Web.Services;

/// <summary>
/// Interface for yard data service that provides access to all yard data
/// and notifies when data changes.
/// </summary>
public interface IYardDataService
{
    /// <summary>
    /// Raised when any data file changes and data is reloaded.
    /// </summary>
    event Action<DataChangedEventArgs>? DataChanged;

    YardTopology Topology { get; }
    IReadOnlyList<Point> Points { get; }
    IReadOnlyList<TurntableTrack> TurntableTracks { get; }
    IReadOnlyList<TrainRouteCommand> TrainRoutes { get; }
    IReadOnlyList<Signal> Signals { get; }
    int LockReleaseDelaySeconds { get; }
    LabelTranslator LabelTranslator { get; }
    ValidationResult? LastValidationResult { get; }
    bool HasValidationErrors { get; }

    /// <summary>
    /// Name of the currently loaded station.
    /// </summary>
    string CurrentStationName { get; }

    /// <summary>
    /// Names of all configured stations.
    /// </summary>
    IReadOnlyList<string> AvailableStations { get; }

    Task InitializeAsync();
    Task ReloadAllAsync();

    /// <summary>
    /// Switches to a different station by name (case-insensitive, supports non-ASCII characters).
    /// Disposes existing file watchers, updates paths, creates new watchers, and reloads all data.
    /// </summary>
    Task SwitchStationAsync(string stationName);
}
