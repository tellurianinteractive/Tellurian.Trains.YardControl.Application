using Tellurian.Trains.Communications.Channels;
using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet;
using Tellurian.Trains.Protocols.LocoNet.Commands;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.LocoNet;

public sealed class LocoNetYardController(ICommunicationsChannel communicationsChannel, ISignalNotificationService signalNotifications, ILogger<LocoNetYardController> logger) : IYardController
{
    private readonly ICommunicationsChannel _communicationsChannel = communicationsChannel ?? throw new ArgumentNullException(nameof(communicationsChannel));
    private readonly ISignalNotificationService _signalNotifications = signalNotifications;
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

    public async Task SendPointStateRequestAsync(int address, CancellationToken cancellationToken)
    {
        var command = new RequestAccessoryStateCommand(Address.From((short)address));
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet switch state request created for address {Address}", address);

        var data = command.GetBytesWithChecksum();
        await _communicationsChannel.SendAsync(data, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet switch state request sent: {Command}", Convert.ToHexString(data));
    }

    public async Task SendSignalCommandAsync(SignalCommand command, CancellationToken cancellationToken)
    {
        if (command.HasAddress)
        {
            var position = command.State == SignalState.Go ? Position.ClosedOrGreen : Position.ThrownOrRed;
            var locoNetCommand = new SetAccessoryCommand(Address.From((short)command.Address), position, MotorState.On);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("LocoNet signal command created: signal {Signal} address {Address} {State}", command.SignalNumber, command.Address, command.State);

            await Task.Delay(100, cancellationToken);
            var data = locoNetCommand.GetBytesWithChecksum();
            await _communicationsChannel.SendAsync(data, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("LocoNet signal command sent: signal {Signal} {State}", command.SignalNumber, command.State);
        }
        else
        {
            // No address configured - notify directly (no LocoNet command, no hardware feedback)
            _signalNotifications.NotifySignalStateChanged(command);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Signal {Signal} {State} (no address, notified directly)", command.SignalNumber, command.State);
        }
    }
}
