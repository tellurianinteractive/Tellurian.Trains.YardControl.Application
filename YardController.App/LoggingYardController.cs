using Tellurian.Trains.Protocols.LocoNet;

namespace Tellurian.Trains.YardController;

public sealed class LoggingYardController(ILogger<LoggingYardController> logger) : IYardController
{
    private readonly ILogger _logger = logger;

    public Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Point lock executed: {Command}", command.AsLockOrUnlockCommand);
        return Task.CompletedTask;
    }

    public Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Point command executed: {Command}", command);
        return Task.CompletedTask;
    }

    public Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Point unlock executed: {Command}", command.AsLockOrUnlockCommand);
        return Task.CompletedTask;
    }
}
