using System.Diagnostics;
using System.Text;

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
    private IEnumerable<TrainPathCommand> _trainPathCommands = [];

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Starting Numeric Keypad Controller Inputs");
        _switches = await _switchDataSource.GetSwitchesAsync(cancellationToken).ConfigureAwait(false);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("{SwitchCount} switch adresses read", _switches.Count());
        _trainPathCommands = await _trainPathDataSource.GetTrainPathCommandsAsync(cancellationToken).ConfigureAwait(false);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("{TrainPathCount} Train Path Commands read", _trainPathCommands.Count());
        _trainPathCommands = _trainPathCommands.UpdateCommandsWithSwitchAdresses(_switches);
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
            if (keyInfo.Key == ConsoleKey.Escape)
            {
                _cancellationTokenSource.Cancel();
                continue;
            }
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
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Train Path command entered: {TrainPathCommand}", command);
                if (command.Contains(char.SignalDivider))
                {
                    var parts = command[0..^1].Split(char.SignalDivider);
                    for (var i = 0; i < parts.Length - 1; i++)
                    {
                        if (parts[i].Length > 0 && parts[i + 1].Length > 0)
                        {
                            var trainPathCommand = Find(parts[i].ToIntOrZero, parts[i + 1].ToIntOrZero, command[^1].TrainPathState);
                            _ = await TrySetTrainPath(trainPathCommand, cancellationToken);
                            inputKeys.Clear();
                        }
                    }
                }
                else if (command.Length == 5)
                {
                    var trainPathCommand = Find(command[0..2].ToIntOrZero, command[2..4].ToIntOrZero, command[^1].TrainPathState);
                    _ = await TrySetTrainPath(trainPathCommand, cancellationToken);
                }
                else if (command.Length > 1 && command.Length < 5 && command[^1].IsTrainsetClearCommand)
                {
                    var trainPathCommand = new TrainPathCommand(0, command[0..^1].ToIntOrZero, TrainPathState.Clear, []);
                    _ = await TrySetTrainPath(trainPathCommand, cancellationToken);
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


    private TrainPathCommand? Find(int fromSignalNumber, int toSignalNumber, TrainPathState state)
    {
        var trainPathCommand = _trainPathCommands.FirstOrDefault(tp => tp.FromSignal == fromSignalNumber && tp.ToSignal == toSignalNumber);
        if (trainPathCommand is null || trainPathCommand.IsUndefined)
        {
            if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("No train path found for from signal {FromSignalNumber} to signal {ToSignalNumber}", fromSignalNumber, toSignalNumber);
            return null;
        }
        return trainPathCommand with { State = state };
    }

    private async Task<bool> TrySetTrainPath(TrainPathCommand? trainPathCommand, CancellationToken cancellationToken)
    {
        if (trainPathCommand is null) return false;
        if (_switchLockings.CanReserveLocksFor(trainPathCommand))
        {
            _switchLockings.ReserveOrClearLocks(trainPathCommand);
            foreach (var switchCommand in trainPathCommand.SwitchCommands)
            {
                if (_switchLockings.IsUnchanged(switchCommand))
                {
                    if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command {SwitchCommand} is unchanged, no action taken.", switchCommand);
                    continue;
                }
                await _yardController.SendSwitchCommandAsync(switchCommand, cancellationToken);
            }
            _switchLockings.CommitLocks(trainPathCommand);
            return true;
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("Train path command {TrainPathCommand} is in conflict with locked switches {LockedSwitches}",
                    trainPathCommand, string.Join(", ", _switchLockings.LockedSwitchesFor(trainPathCommand).Select(sc => sc.Number)));
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