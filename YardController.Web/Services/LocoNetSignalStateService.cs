using System.Collections.Concurrent;
using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.Protocols.LocoNet.Notifications;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Production implementation of <see cref="ISignalStateService"/>.
/// Receives LocoNet switch report feedback for signal addresses and tracks signal states.
/// Also subscribes to <see cref="ISignalNotificationService"/> for unaddressed signal updates.
/// Builds address map from ALL stations since addresses are unique across stations.
/// Does NOT call StartReceiveAsync (LocoNetPointPositionService already does that).
/// </summary>
public sealed class LocoNetSignalStateService : BackgroundService, ISignalStateService, IObserver<CommunicationResult>
{
    private readonly ICommunicationsChannel _channel;
    private readonly ISignalNotificationService _signalNotifications;
    private readonly IYardDataService _yardDataService;
    private readonly ILogger<LocoNetSignalStateService> _logger;
    private readonly ConcurrentDictionary<(string StationName, int SignalNumber), SignalState> _states = new();
    private Dictionary<int, (string StationName, int SignalNumber)> _addressToSignalMap = new(); // LocoNet address → (station, signal number)
    private IDisposable? _subscription;

    public LocoNetSignalStateService(
        ICommunicationsChannel channel,
        ISignalNotificationService signalNotifications,
        IYardDataService yardDataService,
        ILogger<LocoNetSignalStateService> logger)
    {
        _channel = channel;
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
        _subscription = _channel.Subscribe(this);

        // Do NOT call StartReceiveAsync - LocoNetPointPositionService already does that
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

    void IObserver<CommunicationResult>.OnNext(CommunicationResult value)
    {
        if (value is not SuccessResult success) return;

        try
        {
            var message = LocoNetMessageFactory.Create(success.Data());

            int? locoNetAddress = null;
            Position? direction = null;

            if (message is AccessoryReportNotification report && report.IsOutputStatus && report.CurrentDirection.HasValue)
            {
                locoNetAddress = report.Address.Number;
                direction = report.CurrentDirection.Value;
            }
            else if (message is SetAccessoryNotification notification)
            {
                locoNetAddress = notification.Address.Number;
                direction = notification.Direction;
            }

            if (locoNetAddress.HasValue && direction.HasValue && _addressToSignalMap.TryGetValue(locoNetAddress.Value, out var mapping))
            {
                var state = direction.Value == Position.ClosedOrGreen
                    ? SignalState.Go : SignalState.Stop;

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Signal {Signal} state feedback: {State} (station {Station}, address {Address})",
                        mapping.SignalNumber, state, mapping.StationName, locoNetAddress.Value);

                UpdateState(mapping.StationName, mapping.SignalNumber, state);
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(ex, "Error processing LocoNet message for signal state");
        }
    }

    private void UpdateState(string stationName, int signalNumber, SignalState state)
    {
        _states[(stationName, signalNumber)] = state;
        SignalStateChanged?.Invoke(new SignalStateFeedback(stationName, signalNumber, state));
    }

    void IObserver<CommunicationResult>.OnError(Exception error)
    {
        if (_logger.IsEnabled(LogLevel.Error))
            _logger.LogError(error, "LocoNet communication error in signal state service");
    }

    void IObserver<CommunicationResult>.OnCompleted()
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("LocoNet communication completed in signal state service");
    }
}
