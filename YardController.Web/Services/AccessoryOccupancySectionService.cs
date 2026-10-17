using System.Collections.Concurrent;
using Tellurian.Trains.Communications.Interfaces;
using Tellurian.Trains.Communications.Interfaces.Detectors;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Protocol-agnostic <see cref="IOccupancySectionService"/>. Consumes
/// <see cref="OccupancyNotification"/> events from the command-station adapter — LocoNet
/// <c>OPC_INPUT_REP</c> over serial, or the Z21's LocoNet and CAN detector broadcasts — and maps
/// the feedback address to the station's detection stretches declared on that address. One
/// detector may carry several of them: a throat is one piece of isolated rail, and the stretches
/// running over it are the ways through, which the points rather than the detector tell apart.
/// </summary>
public sealed partial class AccessoryOccupancySectionService : BackgroundService, IOccupancySectionService, IObserver<Notification>
{
    private readonly IObservable<Notification> _notifications;
    private readonly IYardDataService _yardDataService;
    private readonly ILogger<AccessoryOccupancySectionService> _logger;
    private readonly ConcurrentDictionary<(string StationName, int SectionNumber), bool> _occupancies = new();
    private Dictionary<int, List<(string StationName, int SectionNumber)>> _addressToSectionMap = new();
    private IDisposable? _subscription;

    public AccessoryOccupancySectionService(
        IObservable<Notification> notifications,
        IYardDataService yardDataService,
        ILogger<AccessoryOccupancySectionService> logger)
    {
        _notifications = notifications;
        _yardDataService = yardDataService;
        _logger = logger;
    }

    public event Action<OccupancyFeedback>? OccupancyChanged;

    public bool IsOccupied(string stationName, int sectionNumber) =>
        _occupancies.TryGetValue((stationName, sectionNumber), out var occupied) && occupied;

    public IReadOnlyDictionary<int, bool> GetAllOccupancies(string stationName) =>
        _occupancies
            .Where(kvp => kvp.Key.StationName.Equals(stationName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key.SectionNumber, kvp => kvp.Value);

    public void SetOccupied(string stationName, int sectionNumber, bool isOccupied) =>
        UpdateOccupancy(stationName, sectionNumber, isOccupied);

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

    private void OnDataChanged(StationDataChange args)
    {
        BuildAddressMap();
    }

    private void BuildAddressMap()
    {
        var map = new Dictionary<int, List<(string StationName, int SectionNumber)>>();
        foreach (var stationName in _yardDataService.AvailableStations)
        {
            var stationData = _yardDataService.GetStationData(stationName);
            if (stationData is null) continue;

            foreach (var section in stationData.OccupancySections.Where(s => s.HasAddress))
            {
                var address = section.Address!.Value;
                if (!map.TryGetValue(address, out var sections)) map[address] = sections = [];
                sections.Add((stationName, section.Number));
            }
        }
        _addressToSectionMap = map;
        LogAddressMapBuilt(map.Count, _yardDataService.AvailableStations.Count);
    }

    void IObserver<Notification>.OnNext(Notification value)
    {
        if (value is not OccupancyNotification notification) return;
        if (!_addressToSectionMap.TryGetValue(notification.FeedbackAddress, out var sections)) return;

        foreach (var (stationName, sectionNumber) in sections)
        {
            LogOccupancyFeedback(sectionNumber, notification.IsOccupied ? "occupied" : "free", stationName, notification.FeedbackAddress);
            UpdateOccupancy(stationName, sectionNumber, notification.IsOccupied);
        }
    }

    private void UpdateOccupancy(string stationName, int sectionNumber, bool isOccupied)
    {
        _occupancies[(stationName, sectionNumber)] = isOccupied;
        OccupancyChanged?.Invoke(new OccupancyFeedback(stationName, sectionNumber, isOccupied));
    }

    void IObserver<Notification>.OnError(Exception error) => LogNotificationStreamError(error);

    void IObserver<Notification>.OnCompleted() => LogNotificationStreamCompleted();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Built occupancy address map with {Count} entries across {StationCount} stations")]
    private partial void LogAddressMapBuilt(int count, int stationCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Occupancy section {Section} is {State} (station {Station}, address {Address})")]
    private partial void LogOccupancyFeedback(int section, string state, string station, int address);

    [LoggerMessage(Level = LogLevel.Error, Message = "Accessory notification stream error in occupancy section service")]
    private partial void LogNotificationStreamError(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Accessory notification stream completed in occupancy section service")]
    private partial void LogNotificationStreamCompleted();
}
