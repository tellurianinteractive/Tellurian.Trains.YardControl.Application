using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Protocols.LocoNet;

namespace Tellurian.Trains.YardController;

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

