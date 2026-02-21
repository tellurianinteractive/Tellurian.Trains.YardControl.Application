using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed class LoggingYardController(
    ILogger<LoggingYardController> logger,
    IPointNotificationService pointNotifications,
    ISignalNotificationService signalNotifications,
    IYardDataService yardDataService) : IYardController
{
    private readonly ILogger _logger = logger;
    private static readonly Random _random = new();

    public async Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Point lock executed: {Command}", command.AsLockOrUnlockCommand);
    }

    public async Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Point command executed: {Command}", command);
        pointNotifications.NotifyPointSet(command, $"Point {command.Number} set to {command.Position}");
    }

    public async Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Point unlock executed: {Command}", command.AsLockOrUnlockCommand);
    }

    public async Task SendPointStateRequestAsync(int address, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Switch state request for address {Address}", address);

        // Simulate feedback: find the point owning this address and report a random position
        var point = yardDataService.Points.FirstOrDefault(p =>
            p.StraightAddresses.Select(Math.Abs).Contains(address) ||
            p.DivergingAddresses.Select(Math.Abs).Contains(address));

        if (point is not null)
        {
            var position = _random.Next(2) == 0 ? PointPosition.Straight : PointPosition.Diverging;
            var addresses = position == PointPosition.Straight ? point.StraightAddresses : point.DivergingAddresses;
            var command = PointCommand.Create(point.Number, position, addresses);
            pointNotifications.NotifyPointSet(command, $"Point {point.Number} is {position}");
        }
    }

    public async Task SendRouteCommandAsync(TrainRouteCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Route command executed: route {From}-{To} address {Address}", command.FromSignal, command.ToSignal, command.Address);
    }

    public async Task SendSignalCommandAsync(SignalCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Signal command executed: signal {Signal} {State}", command.SignalNumber, command.State);
        signalNotifications.NotifySignalStateChanged(command);
    }
}
