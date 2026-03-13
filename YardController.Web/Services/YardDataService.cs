using Microsoft.Extensions.Options;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using Tellurian.Trains.YardController.Model.Validation;
using YardController.Web.Services.Data;

namespace YardController.Web.Services;

/// <summary>
/// Coordinates loading and validation of all yard data files (Topology, Points, TrainRoutes).
/// Watches all files for changes and validates consistency on reload.
/// Caches all configured stations simultaneously so concurrent browser windows can view different stations.
/// </summary>
public sealed class YardDataService : IYardDataService, IDisposable
{
    private readonly ILogger<YardDataService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly UnifiedStationParser _unifiedParser;
    private readonly IReadOnlyList<StationConfig> _stations;

    private readonly Dictionary<string, StationData> _stationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stationFilePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FileSystemWatcher>> _stationWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastReloadByStation = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    /// <summary>
    /// Raised when any data file changes and data is reloaded.
    /// </summary>
    public event Action<DataChangedEventArgs>? DataChanged;

    public YardTopology Topology => ActiveData.Topology;
    public IReadOnlyList<Point> Points => ActiveData.Points;
    public IReadOnlyList<TurntableTrack> TurntableTracks => ActiveData.TurntableTracks;
    public IReadOnlyList<TrainRouteCommand> TrainRoutes => ActiveData.TrainRoutes;
    public IReadOnlyList<Signal> Signals => ActiveData.Signals;
    public int LockReleaseDelaySeconds => ActiveData.LockReleaseDelaySeconds;
    public LabelTranslator LabelTranslator => ActiveData.LabelTranslator;
    public ValidationResult? LastValidationResult => ActiveData.LastValidationResult;
    public bool HasValidationErrors => ActiveData.LastValidationResult?.HasErrors ?? false;

    public string CurrentStationName { get; private set; } = "";
    public IReadOnlyList<string> AvailableStations { get; }

    private StationData ActiveData =>
        _stationCache.TryGetValue(CurrentStationName, out var data) ? data : StationData.Empty;

    public YardDataService(
        IOptions<StationSettings> stationSettings,
        ILogger<YardDataService> logger,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _unifiedParser = new UnifiedStationParser(logger);
        _stations = stationSettings.Value.Stations;
        AvailableStations = _stations.Select(s => s.Name).ToList();
    }

    public async Task InitializeAsync()
    {
        if (_stations.Count == 0)
        {
            _logger.LogWarning("No stations configured in settings");
            return;
        }

        // Load all configured stations
        foreach (var station in _stations)
        {
            var filePath = Path.GetFullPath(station.DataFolder);
            _stationFilePaths[station.Name] = filePath;
            await LoadStationAsync(station.Name, filePath);
            CreateWatcherForStation(station.Name, filePath);
        }

        // Set first station as active
        CurrentStationName = _stations[0].Name;
    }

    public Task SwitchStationAsync(string stationName)
    {
        var station = _stations.FirstOrDefault(s =>
            s.Name.Equals(stationName, StringComparison.OrdinalIgnoreCase));

        if (station is null)
        {
            _logger.LogWarning("Station '{StationName}' not found in configuration", stationName);
            return Task.CompletedTask;
        }

        if (CurrentStationName.Equals(station.Name, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        _logger.LogInformation("Switching active station to '{StationName}'", station.Name);
        CurrentStationName = station.Name;

        // Fire DataChanged so background services rebind to the new active station's data
        if (_stationCache.TryGetValue(station.Name, out var data))
        {
            DataChanged?.Invoke(new DataChangedEventArgs(
                station.Name,
                data.Topology,
                data.Points,
                data.TurntableTracks,
                data.TrainRoutes,
                data.LastValidationResult,
                []));
        }

        return Task.CompletedTask;
    }

    public StationData? GetStationData(string stationName) =>
        _stationCache.TryGetValue(stationName, out var data) ? data : null;

    public async Task ReloadAllAsync()
    {
        // Reload the active station (backward compatibility for background services)
        if (!string.IsNullOrEmpty(CurrentStationName) && _stationFilePaths.TryGetValue(CurrentStationName, out var filePath))
            await ReloadStationAsync(CurrentStationName, filePath);
    }

    private void CreateWatcherForStation(string stationName, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, e) => OnFileChanged(stationName, e.FullPath);
        _stationWatchers[stationName] = [watcher];
    }

    private void DisposeWatchersForStation(string stationName)
    {
        if (_stationWatchers.TryGetValue(stationName, out var watchers))
        {
            foreach (var watcher in watchers) watcher.Dispose();
            _stationWatchers.Remove(stationName);
        }
    }

    private async void OnFileChanged(string stationName, string path)
    {
        // Debounce - ignore rapid successive changes per station
        if (_lastReloadByStation.TryGetValue(stationName, out var lastReload) &&
            (DateTime.Now - lastReload).TotalMilliseconds < 500)
        {
            return;
        }

        _logger.LogInformation("File changed for station '{Station}' ({Path}), reloading...", stationName, path);

        // Small delay to ensure file is fully written
        await Task.Delay(100);

        if (_stationFilePaths.TryGetValue(stationName, out var filePath))
            await ReloadStationAsync(stationName, filePath);
    }

    private async Task LoadStationAsync(string stationName, string filePath)
    {
        await ReloadStationAsync(stationName, filePath);
        _logger.LogInformation("Station '{Name}' loaded from: {Path}", stationName, filePath);
    }

    private async Task ReloadStationAsync(string stationName, string filePath)
    {
        if (!await _reloadLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("Reload already in progress, skipping");
            return;
        }

        try
        {
            _lastReloadByStation[stationName] = DateTime.Now;
            var errors = new List<string>();

            YardTopology topology = YardTopology.Empty;
            IReadOnlyList<Point> points = [];
            IReadOnlyList<TurntableTrack> turntableTracks = [];
            IReadOnlyList<TrainRouteCommand> trainRoutes = [];
            IReadOnlyList<Signal> signals = [];
            int lockReleaseDelaySeconds = 0;
            LabelTranslator labelTranslator = new();

            try
            {
                var data = await _unifiedParser.ParseFileAsync(filePath);
                topology = data.Topology;
                points = data.Points;
                turntableTracks = data.TurntableTracks;
                trainRoutes = data.TrainRoutes.UpdateCommandsWithPointAddresses(points.ToDictionary(p => p.Number)).ToList();
                lockReleaseDelaySeconds = data.LockReleaseDelaySeconds;
                labelTranslator = data.Translations is not null
                    ? LabelTranslator.FromData(data.Translations)
                    : new LabelTranslator();

                // Merge signal addresses with topology signal definitions
                var signalAddressMap = data.SignalAddresses.ToDictionary(s => s.SignalName);
                signals = topology.Signals.Select(sd =>
                {
                    var signal = Signal.FromDefinition(sd);
                    if (signalAddressMap.TryGetValue(sd.Name, out var hw))
                    {
                        signal = new Signal(signal.Name, hw.Address, hw.FeedbackAddress)
                        {
                            Coordinate = signal.Coordinate,
                            DrivesRight = signal.DrivesRight,
                            IsVisible = signal.IsVisible,
                            Type = signal.Type,
                            DisplayText = signal.DisplayText
                        };
                    }
                    return signal;
                }).ToList();

                _logger.LogInformation(
                    "Loaded station '{Name}': {Points} points, {Signals} signals, {Routes} routes",
                    data.Name, points.Count, signals.Count, trainRoutes.Count);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load station: {ex.Message}");
                _logger.LogError(ex, "Failed to load station from {Path}", filePath);
            }

            // Validate consistency
            var validationResult = ValidateConsistency(topology, points, trainRoutes);

            // Update cache
            _stationCache[stationName] = new StationData(
                topology, points, turntableTracks, trainRoutes, signals,
                lockReleaseDelaySeconds, labelTranslator, validationResult);

            // Notify subscribers
            DataChanged?.Invoke(new DataChangedEventArgs(
                stationName,
                topology,
                points,
                turntableTracks,
                trainRoutes,
                validationResult,
                errors));
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private ValidationResult ValidateConsistency(YardTopology topology, IReadOnlyList<Point> points, IReadOnlyList<TrainRouteCommand> trainRoutes)
    {
        var validRoutes = new List<TrainRouteCommand>();
        var invalidRoutes = new List<TrainRouteCommand>();

        // Build lookups
        var pointNumbers = new HashSet<int>(points.Select(p => p.Number));
        var hiddenPointNumbers = new HashSet<int>(points.Where(p => p.IsHidden).Select(p => p.Number));
        var topologyPointLabels = new HashSet<int>(topology.Points
            .Select(p => int.TryParse(new string(p.Label.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .Where(n => n > 0));
        var signalNames = new HashSet<int>(topology.Signals
            .Select(s => int.TryParse(s.Name, out var n) ? n : 0)
            .Where(n => n > 0));

        var routeValidator = new TrainRouteValidator(topology, _loggerFactory.CreateLogger<TrainRouteValidator>());

        foreach (var route in trainRoutes)
        {
            var errors = new List<string>();

            // Check signals exist in topology
            if (!signalNames.Contains(route.FromSignal))
                errors.Add($"FromSignal {route.FromSignal} not in topology");
            if (!signalNames.Contains(route.ToSignal))
                errors.Add($"ToSignal {route.ToSignal} not in topology");

            // Check points exist in Points.txt
            foreach (var pointCommand in route.PointCommands)
            {
                if (!pointNumbers.Contains(pointCommand.Number))
                    errors.Add($"Point {pointCommand.Number} not in points configuration");
            }

            // Check on-route points exist in topology (skip hidden points)
            foreach (var pointCommand in route.OnRoutePoints)
            {
                if (!topologyPointLabels.Contains(pointCommand.Number) && !hiddenPointNumbers.Contains(pointCommand.Number))
                    _logger.LogWarning("Route {From}-{To}: on-route point {Point} not in topology",
                        route.FromSignal, route.ToSignal, pointCommand.Number);
            }

            // Check path exists in topology graph
            if (errors.Count == 0 && !routeValidator.ValidateRoute(route))
                errors.Add($"No valid path in topology for route {route.FromSignal}-{route.ToSignal}");

            if (errors.Count > 0)
            {
                _logger.LogWarning("Route {From}-{To} validation errors: {Errors}",
                    route.FromSignal, route.ToSignal, string.Join("; ", errors));
                invalidRoutes.Add(route);
            }
            else
            {
                validRoutes.Add(route);
            }
        }

        _logger.LogInformation("Validation complete: {Valid} valid routes, {Invalid} invalid routes",
            validRoutes.Count, invalidRoutes.Count);

        return new ValidationResult(validRoutes, invalidRoutes);
    }

    public void Dispose()
    {
        foreach (var stationName in _stationWatchers.Keys.ToList())
            DisposeWatchersForStation(stationName);
        _reloadLock.Dispose();
    }
}

public record DataChangedEventArgs(
    string StationName,
    YardTopology Topology,
    IReadOnlyList<Point> Points,
    IReadOnlyList<TurntableTrack> TurntableTracks,
    IReadOnlyList<TrainRouteCommand> TrainRoutes,
    ValidationResult? ValidationResult,
    IReadOnlyList<string> LoadErrors)
{
    public bool HasErrors => LoadErrors.Count > 0 || (ValidationResult?.HasErrors ?? false);
}

public record StationData(
    YardTopology Topology,
    IReadOnlyList<Point> Points,
    IReadOnlyList<TurntableTrack> TurntableTracks,
    IReadOnlyList<TrainRouteCommand> TrainRoutes,
    IReadOnlyList<Signal> Signals,
    int LockReleaseDelaySeconds,
    LabelTranslator LabelTranslator,
    ValidationResult? LastValidationResult)
{
    public static StationData Empty => new(
        YardTopology.Empty, [], [], [], [], 0, new LabelTranslator(), null);
}
