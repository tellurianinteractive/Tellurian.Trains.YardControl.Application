using System.Collections.Concurrent;
using Tellurian.Trains.Communications.Interfaces;
using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Protocol-agnostic <see cref="ISignalStateService"/>. Consumes <see cref="AccessoryNotification"/>
/// events from the command-station adapter (LocoNet or Z21) for addressed signals, and
/// <see cref="ISignalNotificationService"/> updates for signals without a hardware address.
/// </summary>
public sealed class AccessorySignalStateService : BackgroundService, ISignalStateService, IObserver<Notification>
{
    private readonly IObservable<Notification> _notifications;
    private readonly ISignalNotificationService _signalNotifications;
    private readonly IYardDataService _yardDataService;
    private readonly ILogger<AccessorySignalStateService> _logger;
    private readonly ConcurrentDictionary<(string StationName, int SignalNumber), SignalState> _states = new();
    private Dictionary<int, (string StationName, int SignalNumber)> _addressToSignalMap = new();
    private IDisposable? _subscription;

    public AccessorySignalStateService(
        IObservable<Notification> notifications,
        ISignalNotificationService signalNotifications,
        IYardDataService yardDataService,
        ILogger<AccessorySignalStateService> logger)
    {
        _notifications = notifications;
        _signalNotifications = signalNotifications;
        _yardDataService = yardDataService;
        _logger = logger;
    }

    public event Action<SignalStateFeedback>? SignalStateChanged;

    public SignalState GetSignalState(string stationName, int signalNumber) =>
        _states.TryGetValue((stationName, signalNumber), out var state) ? state : SignalState.Stop;

    public IReadOnlyDictionary<int, SignalState> GetAllSignalStates(string stationName) =>
        _states
            .Where(kvp => kvp.Key.StationName.Equals(stationName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key.SignalNumber, kvp => kvp.Value);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BuildAddressMap();
        _yardDataService.DataChanged += OnDataChanged;
        _signalNotifications.SignalChanged += OnSignalNotification;
        _subscription = _notifications.Subscribe(this);
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _yardDataService.DataChanged -= OnDataChanged;
        _signalNotifications.SignalChanged -= OnSignalNotification;
        _subscription?.Dispose();
        base.Dispose();
    }

    private void OnDataChanged(DataChangedEventArgs args)
    {
        BuildAddressMap();
    }

    private void BuildAddressMap()
    {
        var map = new Dictionary<int, (string StationName, int SignalNumber)>();
        foreach (var stationName in _yardDataService.AvailableStations)
        {
            var stationData = _yardDataService.GetStationData(stationName);
            if (stationData is null) continue;

            foreach (var signal in stationData.Signals)
            {
                if (signal.Address > 0 && int.TryParse(signal.Name, out var signalNumber))
                {
                    var feedbackAddress = signal.FeedbackAddress ?? signal.Address;
                    map[feedbackAddress] = (stationName, signalNumber);
                }
            }
        }
        _addressToSignalMap = map;
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Built signal address map with {Count} entries across {StationCount} stations", map.Count, _yardDataService.AvailableStations.Count);
    }

    private void OnSignalNotification(SignalCommand command)
    {
        // SignalNotificationService doesn't carry station name, so find it from address map
        var stationName = _addressToSignalMap.Values
            .Where(v => v.SignalNumber == command.SignalNumber)
            .Select(v => v.StationName)
            .FirstOrDefault();
        if (stationName is not null)
            UpdateState(stationName, command.SignalNumber, command.State);
        else
        {
            // Unaddressed signal — broadcast to all stations that have this signal number
            foreach (var station in _yardDataService.AvailableStations)
            {
                var stationData = _yardDataService.GetStationData(station);
                if (stationData?.Signals.Any(s => int.TryParse(s.Name, out var n) && n == command.SignalNumber) == true)
                    UpdateState(station, command.SignalNumber, command.State);
            }
        }
    }

    void IObserver<Notification>.OnNext(Notification value)
    {
        if (value is not AccessoryNotification notification) return;

        var hardwareAddress = notification.Address.Number;
        if (!_addressToSignalMap.TryGetValue(hardwareAddress, out var mapping)) return;

        var state = notification.Function == Position.ClosedOrGreen ? SignalState.Go : SignalState.Stop;

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Signal {Signal} state feedback: {State} (station {Station}, address {Address})",
                mapping.SignalNumber, state, mapping.StationName, hardwareAddress);

        UpdateState(mapping.StationName, mapping.SignalNumber, state);
    }

    private void UpdateState(string stationName, int signalNumber, SignalState state)
    {
        _states[(stationName, signalNumber)] = state;
        SignalStateChanged?.Invoke(new SignalStateFeedback(stationName, signalNumber, state));
    }

    void IObserver<Notification>.OnError(Exception error)
    {
        if (_logger.IsEnabled(LogLevel.Error))
            _logger.LogError(error, "Accessory notification stream error in signal state service");
    }

    void IObserver<Notification>.OnCompleted()
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Accessory notification stream completed in signal state service");
    }
}
