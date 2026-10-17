using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed partial class LoggingYardController(
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
        LogPointLockExecuted(command.AsLockOrUnlockCommand);
    }

    public async Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        LogPointCommandExecuted(command);
    }

    public async Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        LogPointUnlockExecuted(command.AsLockOrUnlockCommand);
    }

    public async Task SendPointStateRequestAsync(int address, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        LogPointStateRequest(address);

        // Simulate feedback: find the point owning this address and report a random position
        var point = yardDataService.Points.FirstOrDefault(p =>
            p.StraightAddresses.Select(Math.Abs).Contains(address) ||
            p.DivergingAddresses.Select(Math.Abs).Contains(address));

        if (point is not null)
        {
            // Simulated feedback for the development logger; nothing here depends on unpredictability.
#pragma warning disable CA5394
            var position = _random.Next(2) == 0 ? PointPosition.Straight : PointPosition.Diverging;
#pragma warning restore CA5394
            var addresses = position == PointPosition.Straight ? point.StraightAddresses : point.DivergingAddresses;
            var command = PointCommand.Create(point.Number, position, addresses);
            pointNotifications.NotifyPointSet(yardDataService.CurrentStationName, command, $"Point {point.Number} is {position}");
        }
    }

    public async Task SendRouteCommandAsync(TrainRouteCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        LogRouteCommandExecuted(command.FromSignal, command.ToSignal, command.Address);
    }

    public async Task SendSignalCommandAsync(SignalCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        LogSignalCommandExecuted(command.SignalNumber, command.State);
        signalNotifications.NotifySignalStateChanged(command);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Point lock executed: {Command}")]
    private partial void LogPointLockExecuted(PointCommand command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Point command executed: {Command}")]
    private partial void LogPointCommandExecuted(PointCommand command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Point unlock executed: {Command}")]
    private partial void LogPointUnlockExecuted(PointCommand command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Switch state request for address {Address}")]
    private partial void LogPointStateRequest(int address);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Route command executed: route {From}-{To} address {Address}")]
    private partial void LogRouteCommandExecuted(int from, int to, int? address);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Signal command executed: signal {Signal} {State}")]
    private partial void LogSignalCommandExecuted(int signal, SignalState state);
}
