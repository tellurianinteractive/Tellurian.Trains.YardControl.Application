using System.Diagnostics;
using System.Text;
using Tellurian.Trains.YardController.Extensions;

namespace Tellurian.Trains.YardController;

public sealed class NumericKeypadControllerInputs(ILogger<NumericKeypadControllerInputs> logger, IYardController yardController, TrainRouteLockings pointLockings, ITrainRouteDataSource trainPathDataSource, IPointDataSource pointDataSource, IKeyReader keyReader) : BackgroundService, IDisposable
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IYardController _yardController = yardController;
    private readonly IPointDataSource _pointDataSource = pointDataSource;
    private readonly ITrainRouteDataSource _trainPathDataSource = trainPathDataSource;
    private readonly TrainRouteLockings _pointLockings = pointLockings;
    private readonly IKeyReader _keyReader = keyReader;
    private readonly Stopwatch _stopwatch = new();
    private Dictionary<int, Point> _points = [];
    private IEnumerable<TrainRouteCommand> _trainRouteCommands = [];
    private Dictionary<int, TurntableTrack> _turntableTracks = [];

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Starting Numeric Keypad Controller Inputs");
        await LoadConfiguration(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    private async Task LoadConfiguration(CancellationToken cancellationToken)
    {
        var points = await _pointDataSource.GetPointsAsync(cancellationToken).ConfigureAwait(false);
        _points = points.ToDictionary(p => p.Number);
        var turntableTracks = await _pointDataSource.GetTurntableTracksAsync(cancellationToken).ConfigureAwait(false);
        _turntableTracks = turntableTracks.ToDictionary(tt => tt.Number);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("{PointCount} point addresses read", _points.Count());
        _trainRouteCommands = await _trainPathDataSource.GetTrainRouteCommandsAsync(cancellationToken).ConfigureAwait(false);
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("{TrainRouteCount} Train Path Commands read", _trainRouteCommands.Count());
        _trainRouteCommands = _trainRouteCommands.UpdateCommandsWithPointAddresses(_points).ToList();
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
            if (inputKeys.IsClearAllTrainRoutes)
            {
                foreach (var pointCommand in _pointLockings.PointCommands)
                {
                    if (pointCommand.AlsoUnlock)
                        await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
                }
                _pointLockings.ReleaseAllLocks();
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsReloadConfiguration)
            {
                await LoadConfiguration(cancellationToken).ConfigureAwait(false);
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsTurntableCommand)
            {
                var command = inputKeys.CommandString;
                var number = command[1..^1].ToIntOrZero;
                var direction = command.TurntableDirection;
                if (!_turntableTracks.TryGetValue(number, out var turntableTrack))
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("No turntable track with this number: {PointNumber}", number);
                    continue;
                }
                var pointCommand = PointCommand.Create(turntableTrack.Number, direction, [turntableTrack.Address]);
                await _yardController.SendPointSetCommandsAsync(pointCommand, cancellationToken);
            }
            else if (inputKeys.IsPointCommand)
            {
                var command = inputKeys.CommandString;
                var number = command[0..^1].ToIntOrZero;
                if (!_points.ContainsKey(number))
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("No such point number: {PointNumber}", number);
                    inputKeys.Clear();
                    continue;
                }

                var pointCommand = PointCommand.Create(number, command[^1].ToPointPosition, _points.AddressesFor(number));
                if (pointCommand.IsUndefined)
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Invalid point command: {PointCommand}", command);
                    inputKeys.Clear();
                    continue;
                }
                else if (_pointLockings.IsLocked(pointCommand))
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Point command {PointCommand} is not permitted, point is locked.", pointCommand);
                    inputKeys.Clear();
                    continue;
                }
                await _yardController.SendPointSetCommandsAsync(pointCommand, cancellationToken);
            }
            else if (inputKeys.IsTrainRouteCommand)
            {
                var command = inputKeys.CommandString;
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Train route command entered: {TrainRouteCommand}", command);
                if (command.Contains(char.SignalDivider))
                {
                    List<TrainRouteCommand> trainRouteCommands = [];
                    var parts = command[0..^1].Split(char.SignalDivider);
                    for (var i = 0; i < parts.Length - 1; i++)
                    {
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
                            _ = await TrySetTrainRoute(trainRouteCommand, cancellationToken);
                        }
                    }
                }
                else if (command.Length == 5)
                {
                    var trainRouteCommand = FindAndSetState(command[0..2].ToIntOrZero, command[2..4].ToIntOrZero, command[^1].TrainRouteState);
                    _ = await TrySetTrainRoute(trainRouteCommand, cancellationToken);
                }
                else if (command.Length > 1 && command.Length < 5 && command[^1].IsTrainRouteClearCommand)
                {
                    var trainRouteCommand = new TrainRouteCommand(0, command[0..^1].ToIntOrZero, TrainRouteState.Clear, []);
                    _ = await TrySetTrainRoute(trainRouteCommand, cancellationToken);
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

    private async Task<bool> TrySetTrainRoute(TrainRouteCommand? trainRouteCommand, CancellationToken cancellationToken)
    {
        if (trainRouteCommand is null) return false;
        if (trainRouteCommand.IsSet)
        {
            if (_pointLockings.CanReserveLocksFor(trainRouteCommand))
            {
                _pointLockings.ReserveLocks(trainRouteCommand);
                foreach (var pointCommand in trainRouteCommand.PointCommands)
                {
                    await _yardController.SendPointSetCommandsAsync(pointCommand, cancellationToken);
                    if (pointCommand.AlsoLock)
                        await _yardController.SendPointLockCommandsAsync(pointCommand, cancellationToken);
                }
                _pointLockings.CommitLocks(trainRouteCommand);
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Locks taken for train route command {TrainRouteCommand}", trainRouteCommand);

                return true;
            }
            else if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Train route command {TrainRouteCommand} is in conflict with locked points {LockedPoints}",
                    trainRouteCommand, string.Join(", ", _pointLockings.LockedPointsFor(trainRouteCommand).Select(pc => pc.Number)));
            }
        }
        else if (trainRouteCommand.IsClear)
        {
            foreach (var pointCommand in trainRouteCommand.PointCommands)
            {
                if (pointCommand.AlsoUnlock)
                    await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
            }
            _pointLockings.ClearLocks(trainRouteCommand);
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Locks cleard for train route command {TrainRouteCommand}", trainRouteCommand);
            return true;
        }
        return false;
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
