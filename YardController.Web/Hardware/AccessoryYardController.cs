using Microsoft.Extensions.Options;
using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Services;
using YardController.Web.Services.Data;

namespace YardController.Web.Hardware;

/// <summary>
/// Protocol-agnostic <see cref="IYardController"/> implementation that drives accessories (points,
/// signals, train-route triggers) through the <see cref="IAccessory"/> abstraction. Works with any
/// adapter implementing it — currently the LocoNet and Z21 adapters from Tellurian.Trains.Communications.
/// </summary>
public sealed partial class AccessoryYardController(
    IAccessory accessory,
    ISignalNotificationService signalNotifications,
    IOptions<CommandStationSettings> commandStationOptions,
    ILogger<AccessoryYardController> logger) : IYardController
{
    private const int MinInterCommandDelayMs = 100;

    private readonly IAccessory _accessory = accessory ?? throw new ArgumentNullException(nameof(accessory));
    private readonly ISignalNotificationService _signalNotifications = signalNotifications;
    private readonly ILogger<AccessoryYardController> _logger = logger;
    private readonly int _interCommandDelayMs = Math.Max(MinInterCommandDelayMs, commandStationOptions.Value.InterCommandDelayMs);

    public async Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        foreach (var (address, accessoryCommand) in command.ToLockAccessoryCommands())
        {
            await Task.Delay(_interCommandDelayMs, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            LogPointLockCommand(address, accessoryCommand);
            await _accessory.SetAccessoryAsync(address, accessoryCommand, cancellationToken);
        }
    }

    public async Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        foreach (var (address, accessoryCommand) in command.ToAccessoryCommands())
        {
            await Task.Delay(_interCommandDelayMs, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            LogSetPointCommand(address, accessoryCommand);
            await _accessory.SetAccessoryAsync(address, accessoryCommand, cancellationToken);
        }
        LogSetPointCommandExecuted(command);
    }

    public async Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        foreach (var (address, accessoryCommand) in command.ToUnlockAccessoryCommands())
        {
            await Task.Delay(_interCommandDelayMs, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            LogPointUnlockCommand(address, accessoryCommand);
            await _accessory.SetAccessoryAsync(address, accessoryCommand, cancellationToken);
        }
    }

    public async Task SendPointStateRequestAsync(int address, CancellationToken cancellationToken)
    {
        LogAccessoryStateRequest(address);
        await _accessory.QueryAccessoryStateAsync(Address.From((short)address), cancellationToken);
    }

    public async Task SendRouteCommandAsync(TrainRouteCommand command, CancellationToken cancellationToken)
    {
        if (!command.HasAddress) return;
        var routeAddress = Address.From((short)command.Address!.Value);
        LogRouteCommand(command.FromSignal, command.ToSignal, command.Address);

        await Task.Delay(_interCommandDelayMs, cancellationToken);
        await _accessory.SetAccessoryAsync(routeAddress, AccessoryCommand.Close(), cancellationToken);
        LogRouteCommandSent(command.FromSignal, command.ToSignal, command.Address);
    }

    public async Task SendSignalCommandAsync(SignalCommand command, CancellationToken cancellationToken)
    {
        if (command.HasAddress)
        {
            var signalAddress = Address.From((short)command.Address);
            var accessoryCommand = command.State == SignalState.Go
                ? AccessoryCommand.Close()
                : AccessoryCommand.Throw();
            LogSignalCommand(command.SignalNumber, command.Address, command.State);

            await Task.Delay(_interCommandDelayMs, cancellationToken);
            await _accessory.SetAccessoryAsync(signalAddress, accessoryCommand, cancellationToken);
            LogSignalCommandSent(command.SignalNumber, command.State);
        }
        else
        {
            // No address configured — notify directly (no hardware command, no feedback)
            _signalNotifications.NotifySignalStateChanged(command);
            LogSignalNotifiedDirectly(command.SignalNumber, command.State);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Point lock command: {Address} {Command}")]
    private partial void LogPointLockCommand(Address address, AccessoryCommand command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Set point command: {Address} {Command}")]
    private partial void LogSetPointCommand(Address address, AccessoryCommand command);

    [LoggerMessage(Level = LogLevel.Information, Message = "Set point command executed: {Command}")]
    private partial void LogSetPointCommandExecuted(PointCommand command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Point unlock command: {Address} {Command}")]
    private partial void LogPointUnlockCommand(Address address, AccessoryCommand command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Accessory state request for {Address}")]
    private partial void LogAccessoryStateRequest(int address);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Route command: route {From}-{To} address {Address}")]
    private partial void LogRouteCommand(int from, int to, int? address);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route command sent: route {From}-{To} address {Address}")]
    private partial void LogRouteCommandSent(int from, int to, int? address);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Signal command: signal {Signal} address {Address} {State}")]
    private partial void LogSignalCommand(int signal, int address, SignalState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Signal command sent: signal {Signal} {State}")]
    private partial void LogSignalCommandSent(int signal, SignalState state);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Signal {Signal} {State} (no address, notified directly)")]
    private partial void LogSignalNotifiedDirectly(int signal, SignalState state);
}
