using System.Collections.Concurrent;
using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.Protocols.LocoNet.Notifications;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Receives LocoNet switch report feedback and tracks point positions.
/// Maps LocoNet accessory addresses back to point numbers using the Points configuration.
/// </summary>
public sealed class LocoNetPointPositionService : BackgroundService, IPointPositionService, IObserver<CommunicationResult>
{
    private readonly ICommunicationsChannel _channel;
    private readonly IYardDataService _yardDataService;
    private readonly ILogger<LocoNetPointPositionService> _logger;
    private readonly ConcurrentDictionary<int, PointPosition> _positions = new();
    private Dictionary<int, (int PointNumber, bool Inverted)> _addressMap = new();
    private IDisposable? _subscription;

    public LocoNetPointPositionService(
        ICommunicationsChannel channel,
        IYardDataService yardDataService,
        ILogger<LocoNetPointPositionService> logger)
    {
        _channel = channel;
        _yardDataService = yardDataService;
        _logger = logger;
    }

    public event Action<PointPositionFeedback>? PositionChanged;

    public PointPosition GetPosition(int pointNumber) =>
        _positions.TryGetValue(pointNumber, out var position) ? position : PointPosition.Undefined;

    public IReadOnlyDictionary<int, PointPosition> GetAllPositions() =>
        _positions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BuildAddressMap();
        _yardDataService.DataChanged += OnDataChanged;
        _subscription = _channel.Subscribe(this);

        try
        {
            await _channel.StartReceiveAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
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
        var map = new Dictionary<int, (int PointNumber, bool Inverted)>();

        foreach (var point in _yardDataService.Points)
        {
            foreach (var address in point.StraightAddresses)
            {
                var absAddress = Math.Abs(address);
                var inverted = address < 0;
                map[absAddress] = (point.Number, inverted);
            }
        }

        _addressMap = map;
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Built LocoNet address map with {Count} entries", map.Count);
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

                if (_addressMap.TryGetValue(locoNetAddress, out var mapping))
                {
                    var position = report.CurrentDirection.Value == Position.ClosedOrGreen
                        ? (mapping.Inverted ? PointPosition.Diverging : PointPosition.Straight)
                        : (mapping.Inverted ? PointPosition.Straight : PointPosition.Diverging);

                    _positions[mapping.PointNumber] = position;

                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Point {Number} position feedback: {Position} (address {Address}, inverted: {Inverted})",
                            mapping.PointNumber, position, locoNetAddress, mapping.Inverted);

                    PositionChanged?.Invoke(new PointPositionFeedback(mapping.PointNumber, position));
                }
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(ex, "Error processing LocoNet message");
        }
    }

    void IObserver<CommunicationResult>.OnError(Exception error)
    {
        if (_logger.IsEnabled(LogLevel.Error))
            _logger.LogError(error, "LocoNet communication error");
    }

    void IObserver<CommunicationResult>.OnCompleted()
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("LocoNet communication completed");
    }
}
