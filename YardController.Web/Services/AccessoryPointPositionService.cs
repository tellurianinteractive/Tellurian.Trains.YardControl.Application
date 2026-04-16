using System.Collections.Concurrent;
using Tellurian.Trains.Communications.Interfaces;
using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Protocol-agnostic <see cref="IPointPositionService"/>. Subscribes to accessory-state notifications
/// from whichever command-station adapter is wired up (LocoNet or Z21) and maps hardware addresses
/// back to point numbers using the station configuration. Addresses are assumed unique across stations.
/// </summary>
public sealed class AccessoryPointPositionService : BackgroundService, IPointPositionService, IObserver<Notification>
{
    private readonly IObservable<Notification> _notifications;
    private readonly IYardDataService _yardDataService;
    private readonly ILogger<AccessoryPointPositionService> _logger;
    private readonly ConcurrentDictionary<(string StationName, int PointNumber), PointPosition> _positions = new();
    private readonly ConcurrentDictionary<(string StationName, int PointNumber, char SubPoint), PointPosition> _subPointPositions = new();
    private Dictionary<int, List<(string StationName, int PointNumber, bool Inverted, char? SubPoint)>> _addressMap = new();
    private IDisposable? _subscription;

    public AccessoryPointPositionService(
        IObservable<Notification> notifications,
        IYardDataService yardDataService,
        ILogger<AccessoryPointPositionService> logger)
    {
        _notifications = notifications;
        _yardDataService = yardDataService;
        _logger = logger;
    }

    public event Action<PointPositionFeedback>? PositionChanged;

    public PointPosition GetPosition(string stationName, int pointNumber) =>
        _positions.TryGetValue((stationName, pointNumber), out var position) ? position : PointPosition.Undefined;

    public PointPosition GetPosition(string stationName, int pointNumber, char subPoint) =>
        _subPointPositions.TryGetValue((stationName, pointNumber, subPoint), out var pos)
            ? pos : GetPosition(stationName, pointNumber);

    public IReadOnlyDictionary<int, PointPosition> GetAllPositions(string stationName) =>
        _positions
            .Where(kvp => kvp.Key.StationName.Equals(stationName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key.PointNumber, kvp => kvp.Value);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BuildAddressMap();
        _yardDataService.DataChanged += OnDataChanged;
        _subscription = _notifications.Subscribe(this);
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _yardDataService.DataChanged -= OnDataChanged;
        _subscription?.Dispose();
        base.Dispose();
    }

    private void OnDataChanged(DataChangedEventArgs args)
    {
        BuildAddressMap();
    }

    private void BuildAddressMap()
    {
        var map = new Dictionary<int, List<(string StationName, int PointNumber, bool Inverted, char? SubPoint)>>();

        foreach (var stationName in _yardDataService.AvailableStations)
        {
            var stationData = _yardDataService.GetStationData(stationName);
            if (stationData is null) continue;

            foreach (var point in stationData.Points.Where(p => !p.IsAddressOnly))
            {
                var straightAbsAddresses = new HashSet<int>(point.StraightAddresses.Select(Math.Abs));

                foreach (var address in point.StraightAddresses)
                {
                    var absAddress = Math.Abs(address);
                    var inverted = address < 0;
                    char? subPoint = point.SubPointMap is not null && point.SubPointMap.TryGetValue(absAddress, out var sp) ? sp : null;
                    if (!map.TryGetValue(absAddress, out var list))
                    {
                        list = [];
                        map[absAddress] = list;
                    }
                    list.Add((stationName, point.Number, inverted, subPoint));
                }

                // Add diverging-only addresses with opposite inverted convention:
                // For StraightAddresses: positive → closed=Straight, negative → closed=Diverging
                // For DivergingAddresses: positive → closed=Diverging, negative → closed=Straight
                foreach (var address in point.DivergingAddresses)
                {
                    var absAddress = Math.Abs(address);
                    if (straightAbsAddresses.Contains(absAddress)) continue;
                    var inverted = address > 0; // opposite of straight convention
                    char? subPoint = point.SubPointMap is not null && point.SubPointMap.TryGetValue(absAddress, out var sp) ? sp : null;
                    if (!map.TryGetValue(absAddress, out var list))
                    {
                        list = [];
                        map[absAddress] = list;
                    }
                    list.Add((stationName, point.Number, inverted, subPoint));
                }
            }
        }

        _addressMap = map;
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Built accessory address map with {Count} entries across {StationCount} stations", map.Count, _yardDataService.AvailableStations.Count);
    }

    void IObserver<Notification>.OnNext(Notification value)
    {
        if (value is not AccessoryNotification notification) return;

        var hardwareAddress = notification.Address.Number;
        var function = notification.Function;

        if (!_addressMap.TryGetValue(hardwareAddress, out var mappings)) return;

        foreach (var mapping in mappings)
        {
            var position = function == Position.ClosedOrGreen
                ? (mapping.Inverted ? PointPosition.Diverging : PointPosition.Straight)
                : (mapping.Inverted ? PointPosition.Straight : PointPosition.Diverging);

            if (mapping.SubPoint.HasValue)
            {
                _subPointPositions[(mapping.StationName, mapping.PointNumber, mapping.SubPoint.Value)] = position;
            }
            else
            {
                _positions[(mapping.StationName, mapping.PointNumber)] = position;
            }

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Point {Number}{SubPoint} position feedback: {Position} (station {Station}, address {Address}, inverted: {Inverted})",
                    mapping.PointNumber, mapping.SubPoint.HasValue ? mapping.SubPoint.Value : "", position, mapping.StationName, hardwareAddress, mapping.Inverted);

            PositionChanged?.Invoke(new PointPositionFeedback(mapping.StationName, mapping.PointNumber, position, mapping.SubPoint));
        }
    }

    void IObserver<Notification>.OnError(Exception error)
    {
        if (_logger.IsEnabled(LogLevel.Error))
            _logger.LogError(error, "Accessory notification stream error");
    }

    void IObserver<Notification>.OnCompleted()
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Accessory notification stream completed");
    }
}
