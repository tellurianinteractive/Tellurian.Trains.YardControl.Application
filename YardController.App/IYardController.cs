using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Interfaces.Accessories;
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

        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command received: {Command}", command);
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

public sealed class LocoNetYardController(ICommunicationsChannel communicationsChannel) : IYardController
{
    private readonly ICommunicationsChannel _communicationsChannel = communicationsChannel ?? throw new ArgumentNullException(nameof(communicationsChannel));
    public async Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken)
    {
        foreach (var turnoutCommand in command.ToTurnoutCommands())
        {
            if (cancellationToken.IsCancellationRequested) break;
            await _communicationsChannel.SendAsync(turnoutCommand.GetBytesWithChecksum(), cancellationToken);
        }
    }

    static Position GetPosition(SwitchDirection direction) =>
        direction == SwitchDirection.Straight ? Position.ClosedOrGreen : Position.ThrownOrRed;
}

