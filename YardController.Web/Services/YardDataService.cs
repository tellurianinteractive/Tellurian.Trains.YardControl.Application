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
    private readonly TopologyParser _topologyParser;
    private readonly UnifiedStationParser _unifiedParser;
    private readonly IReadOnlyList<StationConfig> _stations;

    private readonly Dictionary<string, StationData> _stationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StationPaths> _stationPaths = new(StringComparer.OrdinalIgnoreCase);
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
        _topologyParser = new TopologyParser(logger);
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
            var paths = BuildStationPaths(station);
            _stationPaths[station.Name] = paths;
            await LoadStationAsync(station.Name, paths);
            CreateWatchersForStation(station.Name, paths);
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
        if (!string.IsNullOrEmpty(CurrentStationName) && _stationPaths.TryGetValue(CurrentStationName, out var paths))
            await ReloadStationAsync(CurrentStationName, paths);
    }

    private static StationPaths BuildStationPaths(StationConfig station)
    {
        var dataPath = Path.GetFullPath(station.DataFolder);
        var useUnifiedFormat = Path.GetExtension(dataPath).Equals(".txt", StringComparison.OrdinalIgnoreCase);

        if (useUnifiedFormat)
        {
            return new StationPaths(dataPath, "", "", "", "", true);
        }
        else
        {
            return new StationPaths(
                Path.Combine(dataPath, "Topology.txt"),
                Path.Combine(dataPath, "Points.txt"),
                Path.Combine(dataPath, "TrainRoutes.txt"),
                Path.Combine(dataPath, "Signals.txt"),
                Path.Combine(dataPath, "LabelTranslations.csv"),
                false);
        }
    }

    private void CreateWatchersForStation(string stationName, StationPaths paths)
    {
        var watchers = new List<FileSystemWatcher>();

        if (paths.UseUnifiedFormat)
        {
            watchers.Add(CreateWatcher(paths.TopologyPath, stationName));
        }
        else
        {
            watchers.Add(CreateWatcher(paths.TopologyPath, stationName));
            watchers.Add(CreateWatcher(paths.PointsPath, stationName));
            watchers.Add(CreateWatcher(paths.TrainRoutesPath, stationName));
            if (File.Exists(paths.SignalsPath))
                watchers.Add(CreateWatcher(paths.SignalsPath, stationName));
        }

        _stationWatchers[stationName] = watchers;
    }

    private void DisposeWatchersForStation(string stationName)
    {
        if (_stationWatchers.TryGetValue(stationName, out var watchers))
        {
            foreach (var watcher in watchers) watcher.Dispose();
            _stationWatchers.Remove(stationName);
        }
    }

    private FileSystemWatcher CreateWatcher(string filePath, string stationName)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, e) => OnFileChanged(stationName, e.FullPath);
        return watcher;
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

        if (_stationPaths.TryGetValue(stationName, out var paths))
            await ReloadStationAsync(stationName, paths);
    }

    private async Task LoadStationAsync(string stationName, StationPaths paths)
    {
        await ReloadStationAsync(stationName, paths);

        if (paths.UseUnifiedFormat)
            _logger.LogInformation("Station '{Name}' using unified format: {Path}", stationName, paths.TopologyPath);
        else
            _logger.LogInformation("Station '{Name}' paths: Topology={Topology}, Points={Points}, TrainRoutes={TrainRoutes}, Signals={Signals}",
                stationName, paths.TopologyPath, paths.PointsPath, paths.TrainRoutesPath, paths.SignalsPath);
    }

    private async Task ReloadStationAsync(string stationName, StationPaths paths)
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

            if (paths.UseUnifiedFormat)
            {
                try
                {
                    var data = await _unifiedParser.ParseFileAsync(paths.TopologyPath);
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
                        "Loaded unified station '{Name}': {Points} points, {Signals} signals, {Routes} routes",
                        data.Name, points.Count, signals.Count, trainRoutes.Count);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to load unified station: {ex.Message}");
                    _logger.LogError(ex, "Failed to load unified station from {Path}", paths.TopologyPath);
                }
            }
            else
            {
                // Legacy multi-file loading
                try
                {
                    topology = await _topologyParser.ParseFileAsync(paths.TopologyPath);
                    _logger.LogInformation("Loaded topology '{Name}': {Points} points, {Signals} signals",
                        topology.Name, topology.Points.Count, topology.Signals.Count);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to load topology: {ex.Message}");
                    _logger.LogError(ex, "Failed to load topology from {Path}", paths.TopologyPath);
                }

                try
                {
                    labelTranslator = await LabelTranslator.LoadFileAsync(paths.LabelTranslationsPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load label translations from {Path}", paths.LabelTranslationsPath);
                }

                try
                {
                    (points, turntableTracks) = await LoadPointsAsync(paths.PointsPath);
                    _logger.LogInformation("Loaded {PointCount} points, {TurntableCount} turntable tracks",
                        points.Count, turntableTracks.Count);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to load points: {ex.Message}");
                    _logger.LogError(ex, "Failed to load points from {Path}", paths.PointsPath);
                }

                try
                {
                    (trainRoutes, lockReleaseDelaySeconds) = await LoadTrainRoutesAsync(paths.TrainRoutesPath);
                    trainRoutes = trainRoutes.UpdateCommandsWithPointAddresses(points.ToDictionary(p => p.Number)).ToList();
                    _logger.LogInformation("Loaded {RouteCount} train routes", trainRoutes.Count);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to load train routes: {ex.Message}");
                    _logger.LogError(ex, "Failed to load train routes from {Path}", paths.TrainRoutesPath);
                }

                try
                {
                    signals = await LoadSignalsAsync(topology, paths.SignalsPath);
                    _logger.LogInformation("Loaded {SignalCount} signal configurations", signals.Count);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to load signals: {ex.Message}");
                    _logger.LogError(ex, "Failed to load signals from {Path}", paths.SignalsPath);
                }
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

    private async Task<(IReadOnlyList<Point>, IReadOnlyList<TurntableTrack>)> LoadPointsAsync(string pointsPath)
    {
        if (!File.Exists(pointsPath))
        {
            _logger.LogWarning("Points file not found: {Path}", pointsPath);
            return ([], []);
        }

        var lines = await File.ReadAllLinesAsync(pointsPath);
        var points = new List<Point>();
        var turntableTracks = new List<TurntableTrack>();
        int lockAddressOffset = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('\''))
                continue;

            var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            if (parts[0].Equals("LockOffset", StringComparison.OrdinalIgnoreCase))
            {
                lockAddressOffset = int.TryParse(parts[1], out var offset) ? offset : 0;
            }
            else if (parts[0].Equals("Adresses", StringComparison.OrdinalIgnoreCase))
            {
                var range = parts[1].Split('-');
                if (range.Length == 2 && int.TryParse(range[0], out var start) && int.TryParse(range[1], out var end))
                {
                    for (var address = start; address <= end; address++)
                    {
                        points.Add(new Point(address, [address], [address], lockAddressOffset, IsAddressOnly: true));
                    }
                }
            }
            else if (parts[0].Equals("Turntable", StringComparison.OrdinalIgnoreCase))
            {
                var configParts = line.Split([':', '-', ';']);
                if (configParts.Length == 4 &&
                    int.TryParse(configParts[1], out var startNumber) &&
                    int.TryParse(configParts[2], out var endNumber) &&
                    int.TryParse(configParts[3], out var addressOffset))
                {
                    for (int number = startNumber; number <= endNumber; number++)
                    {
                        turntableTracks.Add(new TurntableTrack(number, number + addressOffset));
                    }
                }
            }
            else if (int.TryParse(parts[0], out var number))
            {
                var addressPart = parts[1];
                if (addressPart.Contains('('))
                {
                    var (straightAddresses, straightSubPoints) = ParseGroupedAddressesWithSubPoints(addressPart, '+');
                    var (divergingAddresses, divergingSubPoints) = ParseGroupedAddressesWithSubPoints(addressPart, '-');
                    if (straightAddresses.Length > 0 || divergingAddresses.Length > 0)
                    {
                        var subPointMap = BuildSubPointMap(straightSubPoints.Concat(divergingSubPoints));
                        points.Add(new Point(number, straightAddresses, divergingAddresses, lockAddressOffset, subPointMap));
                    }
                }
                else
                {
                    var parsed = addressPart.Split(',')
                        .Select(a => a.Trim().ToAddressWithSubPoint())
                        .Where(p => p.Address != 0)
                        .ToArray();
                    var addresses = parsed.Select(p => p.Address).ToArray();
                    if (addresses.Length > 0)
                    {
                        var subPointMap = BuildSubPointMap(parsed);
                        points.Add(new Point(number, addresses, addresses, lockAddressOffset, subPointMap));
                    }
                }
            }
        }

        return (points, turntableTracks);
    }

    private static (int[] Addresses, IEnumerable<(int Address, char? SubPoint)> SubPoints) ParseGroupedAddressesWithSubPoints(string addressPart, char positionSuffix)
    {
        var addresses = new List<int>();
        var subPoints = new List<(int Address, char? SubPoint)>();
        var searchSuffix = ")" + positionSuffix;
        var suffixIndex = addressPart.IndexOf(searchSuffix);

        while (suffixIndex >= 0)
        {
            var openIndex = addressPart.LastIndexOf('(', suffixIndex);
            if (openIndex >= 0 && openIndex < suffixIndex)
            {
                var addressesStr = addressPart.Substring(openIndex + 1, suffixIndex - openIndex - 1);
                var parsed = addressesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(a => a.ToAddressWithSubPoint())
                    .Where(p => p.Address != 0);
                foreach (var p in parsed)
                {
                    addresses.Add(p.Address);
                    subPoints.Add(p);
                }
            }
            suffixIndex = addressPart.IndexOf(searchSuffix, suffixIndex + 2);
        }

        return ([.. addresses], subPoints);
    }

    private static IReadOnlyDictionary<int, char>? BuildSubPointMap(
        IEnumerable<(int Address, char? SubPoint)> parsed)
    {
        var map = new Dictionary<int, char>();
        foreach (var (address, subPoint) in parsed)
            if (subPoint.HasValue) map[Math.Abs(address)] = subPoint.Value;
        return map.Count > 0 ? map : null;
    }

    private async Task<(IReadOnlyList<TrainRouteCommand> Routes, int LockReleaseDelaySeconds)> LoadTrainRoutesAsync(string trainRoutesPath)
    {
        if (!File.Exists(trainRoutesPath))
        {
            _logger.LogWarning("Train routes file not found: {Path}", trainRoutesPath);
            return ([], 0);
        }

        var lines = await File.ReadAllLinesAsync(trainRoutesPath);
        var commands = new List<TrainRouteCommand>();
        int lockReleaseDelaySeconds = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('\''))
                continue;

            var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;

            // Parse settings
            if (parts[0].Equals("LockReleaseDelay", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(parts[1], out var delay) && delay >= 0)
                {
                    lockReleaseDelaySeconds = delay;
                    _logger.LogInformation("Lock release delay configured: {Delay} seconds", delay);
                }
                continue;
            }

            var signals = parts[0].Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (signals.Length != 2) continue;

            if (!int.TryParse(signals[0], out var fromSignal) || !int.TryParse(signals[1], out var toSignal))
                continue;

            // Handle composite routes (with dots)
            if (parts[1].Contains('.'))
            {
                var route = parts[1].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (route.Length >= 2)
                {
                    var pointCommands = new List<PointCommand>();
                    for (var i = 0; i < route.Length - 1; i++)
                    {
                        if (int.TryParse(route[i], out var from) && int.TryParse(route[i + 1], out var to))
                        {
                            var baseRoute = commands.FirstOrDefault(c => c.FromSignal == from && c.ToSignal == to);
                            if (baseRoute != null)
                                pointCommands.AddRange(baseRoute.PointCommands);
                        }
                    }
                    if (pointCommands.Count > 0)
                    {
                        var intermediateSignals = route.Skip(1).Take(route.Length - 2)
                            .Where(s => int.TryParse(s, out _))
                            .Select(int.Parse)
                            .ToArray();
                        commands.Add(new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, pointCommands.Distinct())
                        {
                            IntermediateSignals = intermediateSignals
                        });
                    }
                }
            }
            else
            {
                var pointPositions = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var pointCommands = pointPositions.Select(pp => pp.ToPointCommand()).ToList();
                commands.Add(new TrainRouteCommand(fromSignal, toSignal, TrainRouteState.Unset, pointCommands));
            }
        }

        return (commands, lockReleaseDelaySeconds);
    }

    private async Task<IReadOnlyList<Signal>> LoadSignalsAsync(YardTopology topology, string signalsPath)
    {
        // Build signals from topology definitions
        var signals = topology.Signals
            .Select(Signal.FromDefinition)
            .ToDictionary(s => s.Name);

        // If signals file exists, merge addresses from it
        if (File.Exists(signalsPath))
        {
            var lines = await File.ReadAllLinesAsync(signalsPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('\''))
                    continue;

                var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;

                var signalName = parts[0];
                var addressPart = parts[1];

                int address;
                int? feedbackAddress = null;

                if (addressPart.Contains(';'))
                {
                    var addressParts = addressPart.Split(';', StringSplitOptions.TrimEntries);
                    if (!int.TryParse(addressParts[0], out address)) continue;
                    if (addressParts.Length > 1 && int.TryParse(addressParts[1], out var parsedFeedbackAddress))
                        feedbackAddress = parsedFeedbackAddress;
                }
                else
                {
                    if (!int.TryParse(addressPart, out address)) continue;
                }

                if (signals.TryGetValue(signalName, out var existing))
                {
                    signals[signalName] = new Signal(existing.Name, address, feedbackAddress)
                    {
                        Coordinate = existing.Coordinate,
                        DrivesRight = existing.DrivesRight,
                        IsVisible = existing.IsVisible
                    };
                }
                else
                {
                    _logger.LogWarning("Signal {Name} in Signals.txt not found in topology", signalName);
                }
            }
        }

        return signals.Values.ToList();
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
                    errors.Add($"Point {pointCommand.Number} not in Points.txt");
            }

            // Check on-route points exist in topology (skip hidden points)
            foreach (var pointCommand in route.OnRoutePoints)
            {
                if (!topologyPointLabels.Contains(pointCommand.Number) && !hiddenPointNumbers.Contains(pointCommand.Number))
                    _logger.LogWarning("Route {From}-{To}: on-route point {Point} not in Topology.txt",
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

/// <summary>
/// Tracks file paths for a station configuration.
/// </summary>
internal record StationPaths(
    string TopologyPath,
    string PointsPath,
    string TrainRoutesPath,
    string SignalsPath,
    string LabelTranslationsPath,
    bool UseUnifiedFormat);

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
