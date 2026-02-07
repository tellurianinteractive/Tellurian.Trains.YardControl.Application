using System.Diagnostics;
using System.Text;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using YardController.Web.Resources;

namespace YardController.Web.Services;

public sealed class NumericKeypadControllerInputs(ILogger<NumericKeypadControllerInputs> logger, IYardController yardController, TrainRouteLockings pointLockings, IYardDataService yardDataService, IKeyReader keyReader, ITrainRouteNotificationService trainRouteNotificationService, IPointNotificationService pointNotificationService) : BackgroundService, IDisposable
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IYardController _yardController = yardController;
    private readonly IYardDataService _yardDataService = yardDataService;
    private readonly TrainRouteLockings _pointLockings = pointLockings;
    private readonly IKeyReader _keyReader = keyReader;
    private readonly ITrainRouteNotificationService _trainRouteNotificationService = trainRouteNotificationService;
    private readonly IPointNotificationService _pointNotificationService = pointNotificationService;
    private readonly Stopwatch _stopwatch = new();
    private Dictionary<int, Point> _points = [];
    private IEnumerable<TrainRouteCommand> _trainRouteCommands = [];
    private Dictionary<int, TurntableTrack> _turntableTracks = [];

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Starting Numeric Keypad Controller Inputs");

        // Subscribe to data changes
        _yardDataService.DataChanged += OnDataChanged;

        // Load initial configuration from YardDataService
        LoadConfigurationFromService();

        return base.StartAsync(cancellationToken);
    }

    private void OnDataChanged(DataChangedEventArgs args)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Data changed, reloading configuration...");

        LoadConfigurationFromService();

        if (args.HasErrors)
        {
            _logger.LogWarning("Data has validation errors: {ErrorCount} load errors, {InvalidRoutes} invalid routes",
                args.LoadErrors.Count, args.ValidationResult?.InvalidRoutes.Count ?? 0);
        }
    }

    private void LoadConfigurationFromService()
    {
        _points = _yardDataService.Points.ToDictionary(p => p.Number);
        _turntableTracks = _yardDataService.TurntableTracks.ToDictionary(tt => tt.Number);
        _trainRouteCommands = _yardDataService.TrainRoutes;

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{PointCount} point addresses loaded", _points.Count);
            _logger.LogInformation("{TrainRouteCount} train route commands loaded", _trainRouteCommands.Count());
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Stopping Numeric Keypad Controller Inputs");
        _yardDataService.DataChanged -= OnDataChanged;
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
                await Task.Delay(100, cancellationToken);
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
                _trainRouteNotificationService.NotifyAllRoutesCleared(Messages.AllRoutesCleared);
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsReloadConfiguration)
            {
                await _yardDataService.ReloadAllAsync().ConfigureAwait(false);
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
                    _pointNotificationService.NotifyPointRejected(number, string.Format(Messages.PointNotFound, number));
                    inputKeys.Clear();
                    continue;
                }

                var position = command[^1].ToPointPosition;
                var pointCommand = PointCommand.Create(number, position, _points.AddressesFor(number, position));
                if (pointCommand.IsUndefined)
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Invalid point command: {PointCommand}", command);
                    _pointNotificationService.NotifyPointRejected(number, string.Format(Messages.PointInvalidCommand, command));
                    inputKeys.Clear();
                    continue;
                }
                else if (_pointLockings.IsLocked(pointCommand))
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _logger.LogWarning("Point command {PointCommand} is not permitted, point is locked.", pointCommand);
                    _pointNotificationService.NotifyPointLocked(pointCommand, string.Format(Messages.PointLocked, pointCommand.Number));
                    inputKeys.Clear();
                    continue;
                }
                await _yardController.SendPointSetCommandsAsync(pointCommand, cancellationToken);
                _pointNotificationService.NotifyPointSet(pointCommand, string.Format(Messages.PointSet, pointCommand.Number, pointCommand.Position));
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
            var notFoundRoute = new TrainRouteCommand(fromSignalNumber, toSignalNumber, state, []);
            _trainRouteNotificationService.NotifyRouteRejected(notFoundRoute, string.Format(Messages.RouteNotFound, fromSignalNumber, toSignalNumber));
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
                _trainRouteNotificationService.NotifyRouteSet(trainRouteCommand, string.Format(Messages.RouteSet, trainRouteCommand.FromSignal, trainRouteCommand.ToSignal));
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Locks taken for train route command {TrainRouteCommand}", trainRouteCommand);

                return true;
            }
            else
            {
                var conflictingPoints = string.Join(", ", _pointLockings.LockedPointsFor(trainRouteCommand).Select(pc => pc.Number));
                _trainRouteNotificationService.NotifyRouteRejected(trainRouteCommand, string.Format(Messages.RouteConflict, conflictingPoints));
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("Train route command {TrainRouteCommand} is in conflict with locked points {LockedPoints}",
                        trainRouteCommand, conflictingPoints);
            }
        }
        else if (trainRouteCommand.IsClear)
        {
            var fromSignal = trainRouteCommand.FromSignal;
            if (fromSignal == 0)
                fromSignal = _pointLockings.CurrentRoutes.FirstOrDefault(r => r.ToSignal == trainRouteCommand.ToSignal)?.FromSignal ?? 0;
            foreach (var pointCommand in trainRouteCommand.PointCommands)
            {
                if (pointCommand.AlsoUnlock)
                    await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
            }
            _pointLockings.ClearLocks(trainRouteCommand);
            _trainRouteNotificationService.NotifyRouteCleared(trainRouteCommand, string.Format(Messages.RouteCleared, fromSignal, trainRouteCommand.ToSignal));
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Locks cleared for train route command {TrainRouteCommand}", trainRouteCommand);
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
