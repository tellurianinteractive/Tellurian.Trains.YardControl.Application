using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed class LoggingYardController(ILogger<LoggingYardController> logger) : IYardController
{
    private readonly ILogger _logger = logger;

    public Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Point lock executed: {Command}", command.AsLockOrUnlockCommand);
        return Task.CompletedTask;
    }

    public Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Point command executed: {Command}", command);
        return Task.CompletedTask;
    }

    public Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Point unlock executed: {Command}", command.AsLockOrUnlockCommand);
        return Task.CompletedTask;
    }
}
