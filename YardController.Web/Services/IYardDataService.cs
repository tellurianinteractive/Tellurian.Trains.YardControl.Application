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
    ValidationResult? LastValidationResult { get; }
    bool HasValidationErrors { get; }

    Task InitializeAsync();
    Task ReloadAllAsync();
}
