using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using Tellurian.Trains.YardController.Model.Validation;

namespace YardController.Web.Services.Testing;

/// <summary>
/// Test implementation of YardDataService that allows manual data injection.
/// </summary>
public sealed class TestYardDataService : IYardDataService, IDisposable
{
    private YardTopology _topology = YardTopology.Empty;
    private List<Point> _points = [];
    private List<TurntableTrack> _turntableTracks = [];
    private List<TrainRouteCommand> _trainRoutes = [];
    private List<Signal> _signals = [];

    public event Action<DataChangedEventArgs>? DataChanged;

    public YardTopology Topology => _topology;
    public IReadOnlyList<Point> Points => _points;
    public IReadOnlyList<TurntableTrack> TurntableTracks => _turntableTracks;
    public IReadOnlyList<Signal> Signals => _signals;
    public string CurrentStationName { get; set; } = "";
    public IReadOnlyList<string> AvailableStations { get; set; } = [];
    /// <summary>
    /// Returns train routes with point addresses populated from the points collection.
    /// </summary>
    public IReadOnlyList<TrainRouteCommand> TrainRoutes =>
        _trainRoutes.UpdateCommandsWithPointAddresses(_points.ToDictionary(p => p.Number)).ToList();
    public int LockReleaseDelaySeconds { get; set; }
    public LabelTranslator LabelTranslator { get; } = new();
    public ValidationResult? LastValidationResult { get; private set; }
    public bool HasValidationErrors => LastValidationResult?.HasErrors ?? false;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task SwitchStationAsync(string stationName) => Task.CompletedTask;

    public Task ReloadAllAsync()
    {
        NotifyDataChanged();
        return Task.CompletedTask;
    }

    public void SetTopology(YardTopology topology)
    {
        _topology = topology;
    }

    public void AddPoint(Point point)
    {
        _points.Add(point);
    }

    public void AddPoint(int number, int[] addresses, int lockAddressOffset = 1000)
    {
        _points.Add(new Point(number, addresses, addresses, lockAddressOffset));
    }

    public void AddTurntableTrack(TurntableTrack track)
    {
        _turntableTracks.Add(track);
    }

    public void AddTrainRoute(TrainRouteCommand route)
    {
        _trainRoutes.Add(route);
    }

    public void AddSignal(Signal signal)
    {
        _signals.Add(signal);
    }

    public void ClearAll()
    {
        _topology = YardTopology.Empty;
        _points.Clear();
        _turntableTracks.Clear();
        _trainRoutes.Clear();
        _signals.Clear();
        LastValidationResult = null;
    }

    public void NotifyDataChanged()
    {
        DataChanged?.Invoke(new DataChangedEventArgs(
            _topology,
            _points,
            _turntableTracks,
            _trainRoutes,
            LastValidationResult,
            []));
    }

    public void Dispose()
    {
        // Nothing to dispose in test implementation
    }
}
