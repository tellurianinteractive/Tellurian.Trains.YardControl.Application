using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.Protocols.LocoNet.Commands;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.LocoNet;

public sealed class LocoNetYardController(ICommunicationsChannel communicationsChannel, ILogger<LocoNetYardController> logger) : IYardController
{
    private readonly ICommunicationsChannel _communicationsChannel = communicationsChannel ?? throw new ArgumentNullException(nameof(communicationsChannel));
    private readonly ILogger<LocoNetYardController> _logger = logger;

    public async Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        var lockCommands = command.ToLocoNetLockCommands();
        foreach (var locoNetCommand in lockCommands)
        {
            await Task.Delay(100, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point lock ccommand reated: {Command}", locoNetCommand);

            var data = locoNetCommand.GetBytesWithChecksum();
            await _communicationsChannel.SendAsync(data, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point lock command sent: {Command}", Convert.ToHexString(data));
        }
    }

    public async Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        foreach (var locoNetCommand in command.ToLocoNetCommands())
        {
            await Task.Delay(100, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet set point command created: {Command}", locoNetCommand);

            var data = locoNetCommand.GetBytesWithChecksum();
            await _communicationsChannel.SendAsync(data, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet set point command sent: {Command}", Convert.ToHexString(data));
        }
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("LocoNet set point command executed: {Command}", command);
    }

    public async Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        var unlockCommands = command.ToLocoNetUnlockCommands();
        foreach (var locoNetCommand in unlockCommands)
        {
            await Task.Delay(100, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point unlock command created: {Command}", locoNetCommand);

            var data = locoNetCommand.GetBytesWithChecksum();
            await _communicationsChannel.SendAsync(data, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet point unlock command sent: {Command}", Convert.ToHexString(data));
        }
    }

    public async Task SendSwitchStateRequestAsync(int address, CancellationToken cancellationToken)
    {
        var command = new RequestSwitchStateCommand(Address.From((short)address));
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet switch state request created for address {Address}", address);

        var data = command.GetBytesWithChecksum();
        await _communicationsChannel.SendAsync(data, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet switch state request sent: {Command}", Convert.ToHexString(data));
    }
}
