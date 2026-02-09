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
/// Does NOT call StartReceiveAsync (LocoNetPointPositionService already does that).
/// </summary>
public sealed class LocoNetSignalStateService : BackgroundService, ISignalStateService, IObserver<CommunicationResult>
{
    private readonly ICommunicationsChannel _channel;
    private readonly ISignalNotificationService _signalNotifications;
    private readonly IYardDataService _yardDataService;
    private readonly ILogger<LocoNetSignalStateService> _logger;
    private readonly ConcurrentDictionary<int, SignalState> _states = new();
    private Dictionary<int, int> _addressToSignalMap = new(); // LocoNet address → signal number
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

    public SignalState GetSignalState(int signalNumber) =>
        _states.TryGetValue(signalNumber, out var state) ? state : SignalState.Stop;

    public IReadOnlyDictionary<int, SignalState> GetAllSignalStates() =>
        _states.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

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
        var map = new Dictionary<int, int>();
        foreach (var signal in _yardDataService.Signals)
        {
            if (signal.Address > 0 && int.TryParse(signal.Name, out var signalNumber))
            {
                var feedbackAddress = signal.FeedbackAddress ?? signal.Address;
                map[feedbackAddress] = signalNumber;
            }
        }
        _addressToSignalMap = map;
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Built signal address map with {Count} entries", map.Count);
    }

    private void OnSignalNotification(SignalCommand command)
    {
        UpdateState(command.SignalNumber, command.State);
    }

    void IObserver<CommunicationResult>.OnNext(CommunicationResult value)
    {
        if (value is not SuccessResult success) return;

        try
        {
            var message = LocoNetMessageFactory.Create(success.Data());

            if (message is SwitchReportNotification report && report.IsOutputStatus && report.CurrentDirection.HasValue)
            {
                var locoNetAddress = report.Address.Number;

                if (_addressToSignalMap.TryGetValue(locoNetAddress, out var signalNumber))
                {
                    var state = report.CurrentDirection.Value == Position.ClosedOrGreen
                        ? SignalState.Go : SignalState.Stop;

                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Signal {Signal} state feedback: {State} (address {Address})",
                            signalNumber, state, locoNetAddress);

                    UpdateState(signalNumber, state);
                }
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(ex, "Error processing LocoNet message for signal state");
        }
    }

    private void UpdateState(int signalNumber, SignalState state)
    {
        _states[signalNumber] = state;
        SignalStateChanged?.Invoke(new SignalStateFeedback(signalNumber, state));
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
