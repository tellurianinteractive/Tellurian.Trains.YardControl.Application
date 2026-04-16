using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Services;

namespace YardController.Web.Hardware;

/// <summary>
/// Protocol-agnostic <see cref="IYardController"/> implementation that drives accessories (points,
/// signals, train-route triggers) through the <see cref="IAccessory"/> abstraction. Works with any
/// adapter implementing it — currently the LocoNet and Z21 adapters from Tellurian.Trains.Communications.
/// </summary>
public sealed class AccessoryYardController(
    IAccessory accessory,
    ISignalNotificationService signalNotifications,
    ILogger<AccessoryYardController> logger) : IYardController
{
    private const int InterCommandDelayMs = 100;

    private readonly IAccessory _accessory = accessory ?? throw new ArgumentNullException(nameof(accessory));
    private readonly ISignalNotificationService _signalNotifications = signalNotifications;
    private readonly ILogger<AccessoryYardController> _logger = logger;

    public async Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        foreach (var (address, accessoryCommand) in command.ToLockAccessoryCommands())
        {
            await Task.Delay(InterCommandDelayMs, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Point lock command: {Address} {Command}", address, accessoryCommand);
            await _accessory.SetAccessoryAsync(address, accessoryCommand, cancellationToken);
        }
    }

    public async Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        foreach (var (address, accessoryCommand) in command.ToAccessoryCommands())
        {
            await Task.Delay(InterCommandDelayMs, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Set point command: {Address} {Command}", address, accessoryCommand);
            await _accessory.SetAccessoryAsync(address, accessoryCommand, cancellationToken);
        }
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Set point command executed: {Command}", command);
    }

    public async Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        foreach (var (address, accessoryCommand) in command.ToUnlockAccessoryCommands())
        {
            await Task.Delay(InterCommandDelayMs, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Point unlock command: {Address} {Command}", address, accessoryCommand);
            await _accessory.SetAccessoryAsync(address, accessoryCommand, cancellationToken);
        }
    }

    public async Task SendPointStateRequestAsync(int address, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Accessory state request for {Address}", address);
        await _accessory.QueryAccessoryStateAsync(Address.From((short)address), cancellationToken);
    }

    public async Task SendRouteCommandAsync(TrainRouteCommand command, CancellationToken cancellationToken)
    {
        if (!command.HasAddress) return;
        var routeAddress = Address.From((short)command.Address!.Value);
        if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Route command: route {From}-{To} address {Address}", command.FromSignal, command.ToSignal, command.Address);

        await Task.Delay(InterCommandDelayMs, cancellationToken);
        await _accessory.SetAccessoryAsync(routeAddress, AccessoryCommand.Close(), cancellationToken);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Route command sent: route {From}-{To} address {Address}", command.FromSignal, command.ToSignal, command.Address);
    }

    public async Task SendSignalCommandAsync(SignalCommand command, CancellationToken cancellationToken)
    {
        if (command.HasAddress)
        {
            var signalAddress = Address.From((short)command.Address);
            var accessoryCommand = command.State == SignalState.Go
                ? AccessoryCommand.Close()
                : AccessoryCommand.Throw();
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Signal command: signal {Signal} address {Address} {State}", command.SignalNumber, command.Address, command.State);

            await Task.Delay(InterCommandDelayMs, cancellationToken);
            await _accessory.SetAccessoryAsync(signalAddress, accessoryCommand, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Signal command sent: signal {Signal} {State}", command.SignalNumber, command.State);
        }
        else
        {
            // No address configured — notify directly (no hardware command, no feedback)
            _signalNotifications.NotifySignalStateChanged(command);
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Signal {Signal} {State} (no address, notified directly)", command.SignalNumber, command.State);
        }
    }
}
