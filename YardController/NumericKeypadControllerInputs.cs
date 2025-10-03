using System.Linq.Expressions;
using System.Net.Http.Json;
using System.Text;

namespace Tellurian.Trains.YardController;


public enum SwitchState
{
    Undefined = 0,
    Straight = 1,
    Diverging = 2
}

public sealed class NumericKeypadControllerInputs(ILogger<NumericKeypadControllerInputs> logger, IYardController yardController, ITrainPathDataSource trainPathDataSource) : IHostedService, IDisposable
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IYardController _yardController = yardController;
    private readonly ITrainPathDataSource _trainPathDataSource = trainPathDataSource;
    private readonly SwitchLockings _switchLockings = new();
    private Task? _worker;
    private IEnumerable<TrainPathCommand> _trainPathCommands = [];
    private Dictionary<int, int> _switchAddresses = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Starting Numeric Keypad Controller Inputs");
        _switchAddresses = await _trainPathDataSource.GetSwitchAddressesAsync(cancellationToken).ConfigureAwait(false);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("{SwitchAdressesCount} switch adresses read", _switchAddresses.Count);
        _trainPathCommands = await _trainPathDataSource.GetTrainPathCommandsAsync(_switchAddresses, cancellationToken).ConfigureAwait(false);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("{TrainPathCount} Train Path Commands read", _trainPathCommands.Count());

        _worker = InputReader(_cancellationTokenSource.Token);
        if (_worker.IsCompleted || _worker.IsCanceled) return;
        _worker.Start();
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Stopping Numeric Keypad Controller Inputs");
        await _cancellationTokenSource.CancelAsync();
    }

    private async Task InputReader(CancellationToken cancellationToken)
    {
        StringBuilder inputKeys = new(10);
        while (!cancellationToken.IsCancellationRequested)
        {
            var keyInfo = Console.ReadKey(true);
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
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Clearing all train paths");
                _switchLockings.ClearLocks();
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsSwitchCommand)
            {
                var command = inputKeys.CommandString;
                var switchCommand = new SwitchCommand(command[0..^1], _switchAddresses.AddressFrom(command[0..^1]), command[^1].SwitchState);
                if(switchCommand.IsUndefined)
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
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command: {SwitchCommand}", switchCommand);
                await _yardController.SendSwitchCommandAsync(switchCommand, cancellationToken);
            }
            else if (inputKeys.IsTrainPathCommand)
            {
                var command = inputKeys.CommandString;
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Train Path command entered: {TrainPathCommand}", command);
                if (command.Contains(char.SignalDivider))
                {
                    var parts = command[0..^1].Split(char.SignalDivider);
                    for (var i = 0; i < parts.Length - 1; i++)
                    {
                        if (parts[i].Length > 0 && parts[i + 1].Length > 0)
                        {
                            var trainPathCommand = Find(parts[i].ToIntOrZero, parts[i + 1].ToIntOrZero, command[^1].TrainPathState);
                            var result = await TrySetTrainPath(trainPathCommand, cancellationToken);
                            inputKeys.Clear();
                        }
                    }
                }
                else if (command.Length == 5)
                {
                    var trainPathCommand = Find(command[0..2].ToIntOrZero, command[2..4].ToIntOrZero, command[^1].TrainPathState);
                    var result = await TrySetTrainPath(trainPathCommand, cancellationToken);
                    inputKeys.Clear();

                }
                else
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Invalid command length: {CommandLength} characters", command.Length);
                }
            }
            else if (key == '<')
            {
                inputKeys.Clear();
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Command cleared");
            }
        }
    }


    private TrainPathCommand? Find(int fromSignalNumber, int toSignalNumber, TrainPathState state)
    {
        var trainPathCommand = _trainPathCommands.FirstOrDefault(tp => tp.FromSignal == fromSignalNumber && tp.ToSignal == toSignalNumber);
        if (trainPathCommand is null || trainPathCommand.IsUndefined )
        {
            if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("No train path found for from signal {FromSignalNumber} to signal {ToSignalNumber}", fromSignalNumber, toSignalNumber);
            return null;
        }
        return trainPathCommand with { State = state };
    }

    private async Task<bool> TrySetTrainPath(TrainPathCommand? trainPathCommand, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Train path command: {TrainPathCommand}", trainPathCommand);
        if (trainPathCommand is null) return false;
        if (_switchLockings.CanSetTrainPath(trainPathCommand))
        {
            foreach (var switchCommand in trainPathCommand.Switches)
            {
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Switch command {SwitchCommand}", switchCommand);
                await _yardController.SendSwitchCommandAsync(switchCommand, cancellationToken);
            }
            return true;
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("Train path command {TrainPathCommand} is in conflict with locked switches {LockedSwitches}",
                    trainPathCommand, string.Join(", ", _switchLockings.LockedSwitchesFor(trainPathCommand).Select(sc => sc.Address)));
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
                try
                {
                    _worker?.Wait();
                }
                catch (AggregateException ex)
                {
                    _logger.LogError(ex, "Error waiting for worker to finish.");
                }
                _cancellationTokenSource.Dispose();
                _worker?.Dispose();
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}