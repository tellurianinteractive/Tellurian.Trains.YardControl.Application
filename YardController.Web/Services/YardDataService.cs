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
/// </summary>
public sealed class YardDataService : IYardDataService, IDisposable
{
    private readonly ILogger<YardDataService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TopologyParser _topologyParser;
    private readonly string _topologyPath;
    private readonly string _pointsPath;
    private readonly string _trainRoutesPath;
    private readonly string _signalsPath;
    private readonly string _labelTranslationsPath;

    private readonly FileSystemWatcher _topologyWatcher;
    private readonly FileSystemWatcher _pointsWatcher;
    private readonly FileSystemWatcher _trainRoutesWatcher;
    private readonly FileSystemWatcher? _signalsWatcher;

    private DateTime _lastReload = DateTime.MinValue;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    // Current data
    private YardTopology _topology = YardTopology.Empty;
    private IReadOnlyList<Point> _points = [];
    private IReadOnlyList<TurntableTrack> _turntableTracks = [];
    private IReadOnlyList<TrainRouteCommand> _trainRoutes = [];
    private IReadOnlyList<Signal> _signals = [];
    private int _lockReleaseDelaySeconds;
    private LabelTranslator _labelTranslator = new();
    private ValidationResult? _lastValidationResult;

    /// <summary>
    /// Raised when any data file changes and data is reloaded.
    /// </summary>
    public event Action<DataChangedEventArgs>? DataChanged;

    public YardTopology Topology => _topology;
    public IReadOnlyList<Point> Points => _points;
    public IReadOnlyList<TurntableTrack> TurntableTracks => _turntableTracks;
    public IReadOnlyList<TrainRouteCommand> TrainRoutes => _trainRoutes;
    public IReadOnlyList<Signal> Signals => _signals;
    public int LockReleaseDelaySeconds => _lockReleaseDelaySeconds;
    public LabelTranslator LabelTranslator => _labelTranslator;
    public ValidationResult? LastValidationResult => _lastValidationResult;
    public bool HasValidationErrors => _lastValidationResult?.HasErrors ?? false;

    public YardDataService(
        IOptions<TopologyServiceSettings> topologySettings,
        IOptions<PointDataSourceSettings> pointSettings,
        IOptions<TrainRouteDataSourceSettings> trainRouteSettings,
        IOptions<SignalDataSourceSettings> signalSettings,
        ILogger<YardDataService> logger,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _topologyParser = new TopologyParser(logger);

        _topologyPath = Path.GetFullPath(topologySettings.Value.Path);
        _pointsPath = Path.GetFullPath(pointSettings.Value.Path);
        _trainRoutesPath = Path.GetFullPath(trainRouteSettings.Value.Path);
        _signalsPath = Path.GetFullPath(signalSettings.Value.Path);
        _labelTranslationsPath = Path.Combine(Path.GetDirectoryName(_topologyPath)!, "LabelTranslations.csv");

        _logger.LogInformation("YardDataService paths: Topology={Topology}, Points={Points}, TrainRoutes={TrainRoutes}, Signals={Signals}",
            _topologyPath, _pointsPath, _trainRoutesPath, _signalsPath);

        // Set up file watchers
        _topologyWatcher = CreateWatcher(_topologyPath, "Topology");
        _pointsWatcher = CreateWatcher(_pointsPath, "Points");
        _trainRoutesWatcher = CreateWatcher(_trainRoutesPath, "TrainRoutes");
        if (File.Exists(_signalsPath))
            _signalsWatcher = CreateWatcher(_signalsPath, "Signals");
    }

    private FileSystemWatcher CreateWatcher(string filePath, string name)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, e) => OnFileChanged(name, e.FullPath);
        return watcher;
    }

    private async void OnFileChanged(string source, string path)
    {
        // Debounce - ignore rapid successive changes
        if ((DateTime.Now - _lastReload).TotalMilliseconds < 500)
        {
            return;
        }

        _logger.LogInformation("{Source} file changed ({Path}), reloading all data...", source, path);

        // Small delay to ensure file is fully written
        await Task.Delay(100);

        await ReloadAllAsync();
    }

    public async Task InitializeAsync()
    {
        await ReloadAllAsync();
    }

    public async Task ReloadAllAsync()
    {
        if (!await _reloadLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("Reload already in progress, skipping");
            return;
        }

        try
        {
            _lastReload = DateTime.Now;
            var errors = new List<string>();

            // Load topology
            try
            {
                _topology = await _topologyParser.ParseFileAsync(_topologyPath);
                _logger.LogInformation("Loaded topology '{Name}': {Points} points, {Signals} signals",
                    _topology.Name, _topology.Points.Count, _topology.Signals.Count);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load topology: {ex.Message}");
                _logger.LogError(ex, "Failed to load topology from {Path}", _topologyPath);
            }

            // Load label translations
            try
            {
                _labelTranslator = await LabelTranslator.LoadFileAsync(_labelTranslationsPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load label translations from {Path}", _labelTranslationsPath);
            }

            // Load points
            try
            {
                (_points, _turntableTracks) = await LoadPointsAsync();
                _logger.LogInformation("Loaded {PointCount} points, {TurntableCount} turntable tracks",
                    _points.Count, _turntableTracks.Count);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load points: {ex.Message}");
                _logger.LogError(ex, "Failed to load points from {Path}", _pointsPath);
            }

            // Load train routes
            try
            {
                _trainRoutes = await LoadTrainRoutesAsync();
                // Update routes with point addresses
                _trainRoutes = _trainRoutes.UpdateCommandsWithPointAddresses(_points.ToDictionary(p => p.Number)).ToList();
                _logger.LogInformation("Loaded {RouteCount} train routes", _trainRoutes.Count);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load train routes: {ex.Message}");
                _logger.LogError(ex, "Failed to load train routes from {Path}", _trainRoutesPath);
            }

            // Load signals
            try
            {
                _signals = await LoadSignalsAsync();
                _logger.LogInformation("Loaded {SignalCount} signal configurations", _signals.Count);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load signals: {ex.Message}");
                _logger.LogError(ex, "Failed to load signals from {Path}", _signalsPath);
            }

            // Validate consistency
            _lastValidationResult = ValidateConsistency();

            // Notify subscribers
            DataChanged?.Invoke(new DataChangedEventArgs(
                _topology,
                _points,
                _turntableTracks,
                _trainRoutes,
                _lastValidationResult,
                errors));
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async Task<(IReadOnlyList<Point>, IReadOnlyList<TurntableTrack>)> LoadPointsAsync()
    {
        if (!File.Exists(_pointsPath))
        {
            _logger.LogWarning("Points file not found: {Path}", _pointsPath);
            return ([], []);
        }

        var lines = await File.ReadAllLinesAsync(_pointsPath);
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
                    for (var addr = start; addr <= end; addr++)
                    {
                        points.Add(new Point(addr, [addr], [addr], lockAddressOffset, IsAddressOnly: true));
                    }
                }
            }
            else if (parts[0].Equals("Turntable", StringComparison.OrdinalIgnoreCase))
            {
                var config = line.Split([':', '-', ';']);
                if (config.Length == 4 &&
                    int.TryParse(config[1], out var startNum) &&
                    int.TryParse(config[2], out var endNum) &&
                    int.TryParse(config[3], out var addrOffset))
                {
                    for (int num = startNum; num <= endNum; num++)
                    {
                        turntableTracks.Add(new TurntableTrack(num, num + addrOffset));
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

    private async Task<IReadOnlyList<TrainRouteCommand>> LoadTrainRoutesAsync()
    {
        if (!File.Exists(_trainRoutesPath))
        {
            _logger.LogWarning("Train routes file not found: {Path}", _trainRoutesPath);
            return [];
        }

        var lines = await File.ReadAllLinesAsync(_trainRoutesPath);
        var commands = new List<TrainRouteCommand>();
        _lockReleaseDelaySeconds = 0;

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
                    _lockReleaseDelaySeconds = delay;
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

        return commands;
    }

    private async Task<IReadOnlyList<Signal>> LoadSignalsAsync()
    {
        // Build signals from topology definitions
        var signals = _topology.Signals
            .Select(Signal.FromDefinition)
            .ToDictionary(s => s.Name);

        // If signals file exists, merge addresses from it
        if (File.Exists(_signalsPath))
        {
            var lines = await File.ReadAllLinesAsync(_signalsPath);
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
                    if (addressParts.Length > 1 && int.TryParse(addressParts[1], out var fb))
                        feedbackAddress = fb;
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

    private ValidationResult ValidateConsistency()
    {
        var validRoutes = new List<TrainRouteCommand>();
        var invalidRoutes = new List<TrainRouteCommand>();

        // Build lookups
        var pointNumbers = new HashSet<int>(_points.Select(p => p.Number));
        var topologyPointLabels = new HashSet<int>(_topology.Points
            .Select(p => int.TryParse(new string(p.Label.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .Where(n => n > 0));
        var signalNames = new HashSet<int>(_topology.Signals
            .Select(s => int.TryParse(s.Name, out var n) ? n : 0)
            .Where(n => n > 0));

        var routeValidator = new TrainRouteValidator(_topology, _loggerFactory.CreateLogger<TrainRouteValidator>());

        foreach (var route in _trainRoutes)
        {
            var errors = new List<string>();

            // Check signals exist in topology
            if (!signalNames.Contains(route.FromSignal))
                errors.Add($"FromSignal {route.FromSignal} not in topology");
            if (!signalNames.Contains(route.ToSignal))
                errors.Add($"ToSignal {route.ToSignal} not in topology");

            // Check points exist in Points.txt
            foreach (var pc in route.PointCommands)
            {
                if (!pointNumbers.Contains(pc.Number))
                    errors.Add($"Point {pc.Number} not in Points.txt");
            }

            // Check on-route points exist in topology
            foreach (var pc in route.OnRoutePoints)
            {
                if (!topologyPointLabels.Contains(pc.Number))
                    _logger.LogWarning("Route {From}-{To}: on-route point {Point} not in Topology.txt",
                        route.FromSignal, route.ToSignal, pc.Number);
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
        _topologyWatcher.Dispose();
        _pointsWatcher.Dispose();
        _trainRoutesWatcher.Dispose();
        _signalsWatcher?.Dispose();
        _reloadLock.Dispose();
    }
}

public record DataChangedEventArgs(
    YardTopology Topology,
    IReadOnlyList<Point> Points,
    IReadOnlyList<TurntableTrack> TurntableTracks,
    IReadOnlyList<TrainRouteCommand> TrainRoutes,
    ValidationResult? ValidationResult,
    IReadOnlyList<string> LoadErrors)
{
    public bool HasErrors => LoadErrors.Count > 0 || (ValidationResult?.HasErrors ?? false);
}
