using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Protocols.LocoNet;

namespace Tellurian.Trains.YardController;

public sealed class LocoNetYardController(ICommunicationsChannel communicationsChannel, ILogger<LocoNetYardController> logger) : IYardController
{
    private readonly ICommunicationsChannel _communicationsChannel = communicationsChannel ?? throw new ArgumentNullException(nameof(communicationsChannel));
    private readonly ILogger<LocoNetYardController> _logger = logger;

    public async Task SendPointCommandAsync(PointCommand command, CancellationToken cancellationToken)
    {
        var lockCommands = command.ToLocoNetUnlockCommands();
        var unlockCommand = command.ToLocoNetUnlockCommands();
        foreach (var locoNetCommand in command.ToLocoNetCommands())
        {
            await Task.Delay(100, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point command created: {Command}", locoNetCommand);

            var data = locoNetCommand.GetBytesWithChecksum();
            await _communicationsChannel.SendAsync(data, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point command sent: {Command}", Convert.ToHexString(data));
        }
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Point command executed: {Command}", command);

        foreach (var locoNetCommand in lockCommands)
        {
            await Task.Delay(100, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point lock created: {Command}", locoNetCommand);

            var data = locoNetCommand.GetBytesWithChecksum();
            await _communicationsChannel.SendAsync(data, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point lock sent: {Command}", Convert.ToHexString(data));
        }

        foreach (var locoNetCommand in lockCommands)
        {
            await Task.Delay(100, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point unlock created: {Command}", locoNetCommand);

            var data = locoNetCommand.GetBytesWithChecksum();
            await _communicationsChannel.SendAsync(data, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point unlock sent: {Command}", Convert.ToHexString(data));
        }
    }
}
