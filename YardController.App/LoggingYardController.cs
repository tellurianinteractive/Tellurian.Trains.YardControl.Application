using Tellurian.Trains.Protocols.LocoNet;

namespace Tellurian.Trains.YardController;

public sealed class LoggingYardController(ILogger<LoggingYardController> logger) : IYardController
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    public Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command executed: {Command}", command);
        return Task.CompletedTask;
    }
}

