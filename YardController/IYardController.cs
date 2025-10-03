namespace Tellurian.Trains.YardController;

public interface IYardController
{
    Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken);
}

public class LoggingYardController(ILogger<LoggingYardController> logger) : IYardController
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    public Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken)
    {

        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command received: {Command}", command);
        return Task.CompletedTask;
    }
}
