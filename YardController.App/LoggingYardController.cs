using Tellurian.Trains.Protocols.LocoNet;

namespace Tellurian.Trains.YardController;

public sealed class LoggingYardController(ILogger<LoggingYardController> logger) : IYardController
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task SendPointCommandAsync(PointCommand command, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Point command executed: {Command}", command);
            if (command.AlsoLock) _logger.LogInformation("Point lock executed: {Command}", command.AsLockOrUnlockCommand);
            if (command.AlsoUnlock) _logger.LogInformation("Point unlock executed: {Command}", command.AsLockOrUnlockCommand);
        }

        return Task.CompletedTask;
    }
}
