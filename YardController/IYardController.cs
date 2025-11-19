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

public class TestYardController : IYardController
{
    private readonly List<SwitchCommand> _commands = new(50);
    public Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken)
    {
        _commands.Add(command);
        return Task.CompletedTask;
    }
    public IReadOnlyList<SwitchCommand> Commands => _commands;
    public void Clear() => _commands.Clear();
}
