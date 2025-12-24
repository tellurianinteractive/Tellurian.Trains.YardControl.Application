using System.Diagnostics;
using System.Text;
using Tellurian.Trains.YardController.Extensions;

namespace Tellurian.Trains.YardController;

public sealed class NumericKeypadControllerInputs(ILogger<NumericKeypadControllerInputs> logger, IYardController yardController, SwitchLockings switchLockings, ITrainPathDataSource trainPathDataSource, ISwitchDataSource switchDataSource, IKeyReader keyReader) : BackgroundService, IDisposable
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IYardController _yardController = yardController;
    private readonly ISwitchDataSource _switchDataSource = switchDataSource;
    private readonly ITrainPathDataSource _trainPathDataSource = trainPathDataSource;
    private readonly SwitchLockings _switchLockings = switchLockings;
    private readonly IKeyReader _keyReader = keyReader;
    private readonly Stopwatch _stopwatch = new();
    private IEnumerable<Switch> _switches = [];
    private IEnumerable<TrainRouteCommand> _trainRouteCommands = [];

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Starting Numeric Keypad Controller Inputs");
        _switches = await _switchDataSource.GetSwitchesAsync(cancellationToken).ConfigureAwait(false);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("{SwitchCount} switch adresses read", _switches.Count());
        _trainRouteCommands = await _trainPathDataSource.GetTrainPathCommandsAsync(cancellationToken).ConfigureAwait(false);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("{TrainPathCount} Train Path Commands read", _trainRouteCommands.Count());
        _trainRouteCommands = _trainRouteCommands.UpdateCommandsWithSwitchAdresses(_switches);
        await base.StartAsync(cancellationToken);
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Stopping Numeric Keypad Controller Inputs");
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Start reading input keys.");
        StringBuilder inputKeys = new(10);
        while (!cancellationToken.IsCancellationRequested)
        {
            _stopwatch.Restart();
            while (_keyReader.KeyNotAvailable && _stopwatch.ElapsedMilliseconds < 250)
            {
                Thread.Sleep(250);
                if (cancellationToken.IsCancellationRequested) return;
                _stopwatch.Restart();
            }
            var keyInfo = _keyReader.ReadKey();
            if (keyInfo.IsEmpty) continue;
            var key = keyInfo.ValidCharOrNull;
            if (key is null) continue;
            inputKeys.Append(key);
            if (inputKeys.IsClearAllTrainPaths)
            {
                _switchLockings.ClearAllLocks();
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsSwitchCommand)
            {
                var command = inputKeys.CommandString;
                var number = command[0..^1].ToIntOrZero;
                var switchCommand = SwitchCommand.Create(number, command[^1].SwitchState, _switches.AddressesFor(number));
                if (switchCommand.IsUndefined)
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Invalid switch command: {SwitchCommand}", command);
                    inputKeys.Clear();
                    continue;
                }
                else if (_switchLockings.IsLocked(switchCommand))
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Switch command {SwitchCommand} is not permitted, switch is locked.", switchCommand);
                    inputKeys.Clear();
                    continue;
                }
                else if (_switchLockings.IsUnchanged(switchCommand))
                {
                    if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command {SwitchCommand} is unchanged, no action taken.", switchCommand);
                    inputKeys.Clear();
                    continue;
                }
                await _yardController.SendSwitchCommandAsync(switchCommand, cancellationToken);
            }
            else if (inputKeys.IsTrainPathCommand)
            {
                var command = inputKeys.CommandString;
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Train route command entered: {TrainRouteCommand}", command);
                if (command.Contains(char.SignalDivider))
                {
                    var parts = command[0..^1].Split(char.SignalDivider);
                    for (var i = 0; i < parts.Length - 1; i++)
                    {
                        List<TrainRouteCommand> trainRouteCommands = [];
                        if (parts[i].Length > 0 && parts[i + 1].Length > 0)
                        {
                            var fromSignalNumber = parts[i].ToIntOrZero;
                            var toSignalNumber = parts[i + 1].ToIntOrZero;
                            var trainRouteCommand = FindAndSetState(fromSignalNumber, toSignalNumber, command[^1].TrainRouteState);
                            if (trainRouteCommand is not null)
                            {
                                trainRouteCommands.Add(trainRouteCommand);
                            }
                            else if (_logger.IsEnabled(LogLevel.Warning))
                                _logger.LogWarning("Part of train route not found between signal {FromSignal} and signal {ToSignal}", fromSignalNumber, toSignalNumber);
                        }
                        if (trainRouteCommands.Count < parts.Length - 1)
                        {
                            if (_logger.IsEnabled(LogLevel.Warning))
                                _logger.LogWarning("Train route command not executed due to not complete: {TrainRouteCommand}", command);
                        }
                        else
                        {
                            foreach (var trainRouteCommand in trainRouteCommands)
                            {
                                _ = await TrySetTrainPath(trainRouteCommand, cancellationToken);
                            }
                        }
                    }
                    inputKeys.Clear();
                }
                else if (command.Length == 5)
                {
                    var trainRouteCommand = FindAndSetState(command[0..2].ToIntOrZero, command[2..4].ToIntOrZero, command[^1].TrainRouteState);
                    _ = await TrySetTrainPath(trainRouteCommand, cancellationToken);
                }
                else if (command.Length > 1 && command.Length < 5 && command[^1].IsTrainRouteClearCommand)
                {
                    var trainRouteCommand = new TrainRouteCommand(0, command[0..^1].ToIntOrZero, TrainRouteState.Clear, []);
                    _ = await TrySetTrainPath(trainRouteCommand, cancellationToken);
                }
                else
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Invalid command length: {CommandLength} characters", command.Length);
                }
            }

            else if (key.IsClearCommand)
            {
                inputKeys.Clear();
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Command cleared");
            }
        }
    }

    private TrainRouteCommand? FindAndSetState(int fromSignalNumber, int toSignalNumber, TrainRouteState state)
    {
        var trainRouteCommand = _trainRouteCommands.FirstOrDefault(tp => tp.FromSignal == fromSignalNumber && tp.ToSignal == toSignalNumber);
        if (trainRouteCommand is null || trainRouteCommand.IsUndefined)
        {
            if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("No train route found for from signal {FromSignalNumber} to signal {ToSignalNumber}", fromSignalNumber, toSignalNumber);
            return null;
        }
        return trainRouteCommand with { State = state };
    }

    private async Task<bool> TrySetTrainPath(TrainRouteCommand? trainRouteCommand, CancellationToken cancellationToken)
    {
        if (trainRouteCommand is null) return false;
        if (_switchLockings.CanReserveLocksFor(trainRouteCommand))
        {
            _switchLockings.ReserveOrClearLocks(trainRouteCommand);
            foreach (var switchCommand in trainRouteCommand.SwitchCommands)
            {
                if (_switchLockings.IsUnchanged(switchCommand))
                {
                    if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command {SwitchCommand} is unchanged, no action taken.", switchCommand);
                    continue;
                }
                await _yardController.SendSwitchCommandAsync(switchCommand, cancellationToken);
            }
            if (trainRouteCommand.IsSet)
                _switchLockings.CommitLocks(trainRouteCommand);
            return true;
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("Train route command {TrainRouteCommand} is in conflict with locked switches {LockedSwitches}",
                    trainRouteCommand, string.Join(", ", _switchLockings.LockedSwitchesFor(trainRouteCommand).Select(sc => sc.Number)));
            return false;
        }
    }


    #region Disposable Support
    private bool disposedValue;
    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _cancellationTokenSource.Dispose();
            }
            disposedValue = true;
        }
    }

    public override void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
        base.Dispose();
    }

    #endregion
}