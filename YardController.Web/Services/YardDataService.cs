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
public sealed partial class YardDataService : IYardDataService, IDisposable
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
    public event Action<StationDataChange>? DataChanged;

    public YardTopology Topology => ActiveData.Topology;
    public IReadOnlyList<Point> Points => ActiveData.Points;
    public IReadOnlyList<TurntableTrack> TurntableTracks => ActiveData.TurntableTracks;
    public IReadOnlyList<TrainRouteCommand> TrainRoutes => ActiveData.TrainRoutes;
    public IReadOnlyList<Signal> Signals => ActiveData.Signals;
    public IReadOnlyList<ShuntingArea> ShuntingAreas => ActiveData.ShuntingAreas;
    public IReadOnlyList<OccupancySection> OccupancySections => ActiveData.OccupancySections;
    public ButtonLayout Buttons => ActiveData.Buttons;
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
            LogNoStationsConfigured();
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
            LogStationNotFound(stationName);
            return Task.CompletedTask;
        }

        if (CurrentStationName.Equals(station.Name, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        LogSwitchingStation(station.Name);
        CurrentStationName = station.Name;

        // Fire DataChanged so background services rebind to the new active station's data
        if (_stationCache.TryGetValue(station.Name, out var data))
        {
            DataChanged?.Invoke(new StationDataChange(
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The watcher is kept in _stationWatchers and disposed by DisposeWatchersForStation.")]
    private void CreateWatcherForStation(string stationName, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        // Editors that save atomically (write to temp, then rename) fire Renamed/Created instead of Changed.
        watcher.Changed += (_, e) => OnFileChanged(stationName, e.FullPath);
        watcher.Created += (_, e) => OnFileChanged(stationName, e.FullPath);
        watcher.Renamed += (_, e) => OnFileChanged(stationName, e.FullPath);
        watcher.Error += (_, e) => LogFileWatcherError(e.GetException(), stationName);
        _stationWatchers[stationName] = [watcher];
        LogWatchingFile(fileName, directory, stationName);
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

        LogFileChanged(stationName, path);

        // Small delay to ensure file is fully written
        await Task.Delay(100);

        if (_stationFilePaths.TryGetValue(stationName, out var filePath))
            await ReloadStationAsync(stationName, filePath);
    }

    private async Task LoadStationAsync(string stationName, string filePath)
    {
        await ReloadStationAsync(stationName, filePath);
        LogStationLoaded(stationName, filePath);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A station file that fails to load for any reason is reported to the operator as a load error; it must not take the application down.")]
    private async Task ReloadStationAsync(string stationName, string filePath)
    {
        if (!await _reloadLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            LogReloadInProgress();
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
            IReadOnlyList<ShuntingArea> shuntingAreas = [];
            var buttons = ButtonLayout.Default;
            IReadOnlyList<OccupancySection> occupancySections = [];
            int lockReleaseDelaySeconds = 0;
            LabelTranslator labelTranslator = new();

            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                var data = _unifiedParser.Parse(content);
                data = await DeriveOccupancySectionsIfMissingAsync(stationName, filePath, content, data);
                occupancySections = data.OccupancySections;
                topology = data.Topology;
                points = data.Points;
                turntableTracks = data.TurntableTracks;
                var pointsDict = points.ToDictionary(p => p.Number);
                trainRoutes = data.TrainRoutes
                    .UpdateCommandsWithPointAddresses(pointsDict)
                    .Select(r =>
                    {
                        try
                        {
                            return r.WithSlavesExpanded(pointsDict);
                        }
                        catch (InvalidPointCascadeException ex)
                        {
                            errors.Add($"Route {r.FromSignal}-{r.ToSignal}: {ex.Message}");
                            LogSlaveCascadeConflict(r, ex.Message);
                            return r;
                        }
                    })
                    .ToList();
                lockReleaseDelaySeconds = data.LockReleaseDelaySeconds;
                // Flank protection commands are parsed without addresses, like route point commands.
                shuntingAreas = data.ShuntingAreas.UpdateFlankProtectionWithPointAddresses(pointsDict).ToList();
                buttons = data.Buttons;
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

                LogStationContentLoaded(data.Name, points.Count, signals.Count, trainRoutes.Count);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load station: {ex.Message}");
                LogStationLoadFailed(ex, filePath);
            }

            // Validate consistency
            var validationResult = ValidateConsistency(topology, points, trainRoutes);

            // Update cache
            _stationCache[stationName] = new StationData(
                topology, points, turntableTracks, trainRoutes, signals, shuntingAreas, occupancySections,
                buttons, lockReleaseDelaySeconds, labelTranslator, validationResult);

            // Notify subscribers
            DataChanged?.Invoke(new StationDataChange(
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

    /// <summary>
    /// Gives a station file that has never had an [OccupancySections] section a first set of
    /// detection stretches, deduced from the signal and yard edge coordinates, and writes them
    /// back so the author can add feedback addresses to them. A file that already declares the
    /// section — even an empty one — is left alone: an empty section says the station has no
    /// detection, which is an answer, not a gap to fill.
    /// </summary>
    private async Task<UnifiedStationData> DeriveOccupancySectionsIfMissingAsync(
        string stationName,
        string filePath,
        string content,
        UnifiedStationData data)
    {
        if (UnifiedStationWriter.ContainsOccupancySections(content)) return data;

        var derived = data.Topology.DeriveOccupancySections();
        if (derived.Count == 0) return data;

        try
        {
            await File.WriteAllTextAsync(filePath, UnifiedStationWriter.InsertOccupancySections(content, derived));
            // The write is our own, so keep the file watcher from treating it as an edit.
            _lastReloadByStation[stationName] = DateTime.Now;
            LogOccupancySectionsDerived(derived.Count, stationName, filePath);
        }
        catch (IOException ex)
        {
            LogOccupancySectionsNotWritten(ex, filePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogOccupancySectionsNotWritten(ex, filePath);
        }

        return data with { OccupancySections = derived };
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
                    LogRoutePointNotInTopology(route.FromSignal, route.ToSignal, pointCommand.Number);
            }

            // Check path exists in topology graph
            if (errors.Count == 0 && !routeValidator.ValidateRoute(route))
                errors.Add($"No valid path in topology for route {route.FromSignal}-{route.ToSignal}");

            if (errors.Count > 0)
            {
                LogRouteValidationErrors(route.FromSignal, route.ToSignal, string.Join("; ", errors));
                invalidRoutes.Add(route);
            }
            else
            {
                validRoutes.Add(route);
            }
        }

        LogValidationComplete(validRoutes.Count, invalidRoutes.Count);

        return new ValidationResult(validRoutes, invalidRoutes);
    }

    public void Dispose()
    {
        foreach (var stationName in _stationWatchers.Keys.ToList())
            DisposeWatchersForStation(stationName);
        _reloadLock.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No stations configured in settings")]
    private partial void LogNoStationsConfigured();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Station '{StationName}' not found in configuration")]
    private partial void LogStationNotFound(string stationName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Switching active station to '{StationName}'")]
    private partial void LogSwitchingStation(string stationName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FileSystemWatcher error for station '{Station}'")]
    private partial void LogFileWatcherError(Exception exception, string station);

    [LoggerMessage(Level = LogLevel.Information, Message = "Watching '{File}' in '{Directory}' for station '{Station}'")]
    private partial void LogWatchingFile(string file, string directory, string station);

    [LoggerMessage(Level = LogLevel.Information, Message = "File changed for station '{Station}' ({Path}), reloading...")]
    private partial void LogFileChanged(string station, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Station '{Name}' loaded from: {Path}")]
    private partial void LogStationLoaded(string name, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reload already in progress, skipping")]
    private partial void LogReloadInProgress();

    [LoggerMessage(Level = LogLevel.Error, Message = "Slave cascade conflict for route {Route}: {Message}")]
    private partial void LogSlaveCascadeConflict(TrainRouteCommand route, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded station '{Name}': {Points} points, {Signals} signals, {Routes} routes")]
    private partial void LogStationContentLoaded(string name, int points, int signals, int routes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load station from {Path}")]
    private partial void LogStationLoadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deduced {Count} occupancy sections for station '{Station}' and wrote them to {Path}")]
    private partial void LogOccupancySectionsDerived(int count, string station, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write deduced occupancy sections to {Path}")]
    private partial void LogOccupancySectionsNotWritten(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Route {From}-{To}: on-route point {Point} not in topology")]
    private partial void LogRoutePointNotInTopology(int from, int to, int point);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Route {From}-{To} validation errors: {Errors}")]
    private partial void LogRouteValidationErrors(int from, int to, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Validation complete: {Valid} valid routes, {Invalid} invalid routes")]
    private partial void LogValidationComplete(int valid, int invalid);
}

public record StationDataChange(
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
    IReadOnlyList<ShuntingArea> ShuntingAreas,
    IReadOnlyList<OccupancySection> OccupancySections,
    ButtonLayout Buttons,
    int LockReleaseDelaySeconds,
    LabelTranslator LabelTranslator,
    ValidationResult? LastValidationResult)
{
    public static StationData Empty => new(
        YardTopology.Empty, [], [], [], [], [], [], ButtonLayout.Default, 0, new LabelTranslator(), null);
}
