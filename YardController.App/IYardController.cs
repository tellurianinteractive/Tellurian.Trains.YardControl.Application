using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Protocols.LocoNet;

namespace Tellurian.Trains.YardController;

public interface IYardController
{
    Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken);
}

public sealed class LoggingYardController(ILogger<LoggingYardController> logger) : IYardController
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    public Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command executed: {Command}", command);
        return Task.CompletedTask;
    }
}

public sealed class TestYardController : IYardController
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

public sealed class LocoNetYardController(ICommunicationsChannel communicationsChannel, ILogger<LocoNetYardController> logger) : IYardController
{
    private readonly ICommunicationsChannel _communicationsChannel = communicationsChannel ?? throw new ArgumentNullException(nameof(communicationsChannel));
    private readonly ILogger<LocoNetYardController> _logger = logger;

    public async Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken)
    {
        foreach (var turnoutCommand in command.ToTurnoutCommands())
        {
            if (cancellationToken.IsCancellationRequested) break;
            var data = turnoutCommand.GetBytesWithChecksum();
            await _communicationsChannel.SendAsync(data, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet turnout command sent: {Command}", Convert.ToHexString(data));
        }
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command executed: {Command}", command);
    }
}

