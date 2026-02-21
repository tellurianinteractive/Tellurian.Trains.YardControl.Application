using System.Diagnostics;
using System.Text;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using YardController.Web.Resources;

namespace YardController.Web.Services;

public sealed class NumericKeypadControllerInputs(ILogger<NumericKeypadControllerInputs> logger, IYardController yardController, TrainRouteLockings pointLockings, IYardDataService yardDataService, IKeyReader keyReader, ITrainRouteNotificationService trainRouteNotificationService, IPointNotificationService pointNotificationService, ISignalStateService signalStateService, IPointPositionService pointPositionService, IHostEnvironment hostEnvironment) : BackgroundService, IDisposable
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IYardController _yardController = yardController;
    private readonly IYardDataService _yardDataService = yardDataService;
    private readonly TrainRouteLockings _pointLockings = pointLockings;
    private readonly IKeyReader _keyReader = keyReader;
    private readonly ITrainRouteNotificationService _trainRouteNotificationService = trainRouteNotificationService;
    private readonly IPointNotificationService _pointNotificationService = pointNotificationService;
    private readonly ISignalStateService _signalStateService = signalStateService;
    private readonly IPointPositionService _pointPositionService = pointPositionService;
    private readonly IHostEnvironment _hostEnvironment = hostEnvironment;
    private readonly Stopwatch _stopwatch = new();
    private readonly Dictionary<int, CancellationTokenSource> _pendingReleases = new();
    private Dictionary<int, Point> _points = [];
    private IEnumerable<TrainRouteCommand> _trainRouteCommands = [];
    private Dictionary<int, TurntableTrack> _turntableTracks = [];
    private Dictionary<int, Signal> _signalsByNumber = [];

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
        _signalsByNumber = _yardDataService.Signals
            .Where(s => int.TryParse(s.Name, out _))
            .ToDictionary(s => int.Parse(s.Name));

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{PointCount} point addresses loaded", _points.Count);
            _logger.LogInformation("{TrainRouteCount} train route commands loaded", _trainRouteCommands.Count());
            _logger.LogInformation("{SignalCount} signal configurations loaded", _signalsByNumber.Count);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Stopping Numeric Keypad Controller Inputs");
        _yardDataService.DataChanged -= OnDataChanged;

        // Cancel any pending delayed releases
        foreach (var cts in _pendingReleases.Values) cts.Cancel();
        _pendingReleases.Clear();

        // Release all physical locks by sending unlock commands to hardware
        foreach (var pointCommand in _pointLockings.PointCommands)
        {
            if (pointCommand.AlsoUnlock)
                await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
        }
        _pointLockings.ReleaseAllLocks();

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
                // Cancel any pending delayed releases
                foreach (var cts in _pendingReleases.Values) cts.Cancel();
                _pendingReleases.Clear();

                // Phase 1: Send STOP for all GO signals immediately
                var goSignalNumbers = _pointLockings.CurrentRoutes
                    .SelectMany(r => new[] { r.FromSignal }.Concat(r.IntermediateSignals))
                    .Distinct();
                foreach (var signalNumber in goSignalNumbers)
                {
                    if (_signalsByNumber.TryGetValue(signalNumber, out var signal))
                        await _yardController.SendSignalCommandAsync(
                            new SignalCommand(signalNumber, signal.Address, SignalState.Stop), cancellationToken);
                }

                // Release shunting route locks immediately (no delay) and notify UI
                var shuntingRoutes = _pointLockings.CurrentRoutes
                    .Where(r => r.State == TrainRouteState.SetShunting).ToList();
                foreach (var shuntingRoute in shuntingRoutes)
                {
                    var released = _pointLockings.ClearLocks(shuntingRoute with { State = TrainRouteState.Clear });
                    foreach (var pointCommand in released)
                    {
                        if (pointCommand.AlsoUnlock)
                            await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
                    }
                    _trainRouteNotificationService.NotifyRouteCleared(shuntingRoute,
                        string.Format(Messages.RouteCleared, shuntingRoute.FromSignal, shuntingRoute.ToSignal));
                }

                var delaySeconds = GetLockReleaseDelaySeconds();
                if (delaySeconds > 0 && _pointLockings.CurrentRoutes.Count > 0)
                {
                    // Phase 1: Notify UI to show blue (cancelling state) for remaining main routes
                    _trainRouteNotificationService.NotifyAllRoutesCancelling(Messages.AllRoutesCancelling);
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("All routes cancelling, locks held for {Delay} seconds", delaySeconds);

                    // Phase 2: Release locks after delay
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                            foreach (var pointCommand in _pointLockings.PointCommands)
                            {
                                if (pointCommand.AlsoUnlock)
                                    await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
                            }
                            _pointLockings.ReleaseAllLocks();
                            _trainRouteNotificationService.NotifyAllRoutesCleared(Messages.AllRoutesCleared);
                        }
                        catch (OperationCanceledException) { }
                    }, cancellationToken);
                }
                else
                {
                    // No delay: immediate release (original behaviour)
                    foreach (var pointCommand in _pointLockings.PointCommands)
                    {
                        if (pointCommand.AlsoUnlock)
                            await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
                    }
                    _pointLockings.ReleaseAllLocks();
                    _trainRouteNotificationService.NotifyAllRoutesCleared(Messages.AllRoutesCleared);
                }
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsAllSignalsStop)
            {
                foreach (var (signalNumber, signal) in _signalsByNumber)
                {
                    if (_signalStateService.GetSignalState(signalNumber) == SignalState.Go)
                        await _yardController.SendSignalCommandAsync(
                            new SignalCommand(signalNumber, signal.Address, SignalState.Stop) { FeedbackAddress = signal.FeedbackAddress }, cancellationToken);
                }
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("All signals set to stop");
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
                var isAlreadyInPosition = _pointPositionService.GetPosition(number) == position;
                await _yardController.SendPointSetCommandsAsync(pointCommand, cancellationToken);
                var localizedPosition = Messages.LocalizedPosition(pointCommand.Position);
                if (isAlreadyInPosition)
                    _pointNotificationService.NotifyPointAlreadyInPosition(pointCommand, string.Format(Messages.PointAlreadyInPosition, pointCommand.Number, localizedPosition));
                else
                    _pointNotificationService.NotifyPointSet(pointCommand, string.Format(Messages.PointSet, pointCommand.Number, localizedPosition));
            }
            else if (inputKeys.IsTrainRouteCommand)
            {
                var command = inputKeys.CommandString;
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Train route command entered: {TrainRouteCommand}", command);
                if (command.Contains(char.SignalDivider))
                {
                    var state = command[^1].TrainRouteState;
                    var parts = command[0..^1].Split(char.SignalDivider);

                    if (state.IsTeardown)
                    {
                        // For cancel/clear, just teardown by the final destination signal
                        var toSignalNumber = parts[^1].ToIntOrZero;
                        _ = await TrySetTrainRoute(new TrainRouteCommand(0, toSignalNumber, state, []), cancellationToken);
                    }
                    else
                    {
                        // Find all sub-routes
                        List<TrainRouteCommand> subRoutes = [];
                        for (var i = 0; i < parts.Length - 1; i++)
                        {
                            if (parts[i].Length > 0 && parts[i + 1].Length > 0)
                            {
                                var fromSignalNumber = parts[i].ToIntOrZero;
                                var toSignalNumber = parts[i + 1].ToIntOrZero;
                                var trainRouteCommand = FindAndSetState(fromSignalNumber, toSignalNumber, state);
                                if (trainRouteCommand is not null)
                                    subRoutes.Add(trainRouteCommand);
                                else if (_logger.IsEnabled(LogLevel.Warning))
                                    _logger.LogWarning("Part of train route not found between signal {FromSignal} and signal {ToSignal}", fromSignalNumber, toSignalNumber);
                            }
                        }
                        if (subRoutes.Count < parts.Length - 1)
                        {
                            if (_logger.IsEnabled(LogLevel.Warning))
                                _logger.LogWarning("Train route command not executed due to not complete: {TrainRouteCommand}", command);
                        }
                        else
                        {
                            // Merge sub-routes into a single composite route so clearing by destination works
                            var mergedPointCommands = subRoutes.SelectMany(r => r.PointCommands).Distinct();
                            // Build intermediate signals: include each sub-route's own intermediates
                            // plus junction signals between consecutive sub-routes
                            var intermediateSignalList = new List<int>();
                            for (var i = 0; i < subRoutes.Count; i++)
                            {
                                intermediateSignalList.AddRange(subRoutes[i].IntermediateSignals);
                                if (i < subRoutes.Count - 1)
                                    intermediateSignalList.Add(subRoutes[i].ToSignal);
                            }
                            var intermediateSignals = intermediateSignalList.ToArray();
                            var mergedRoute = new TrainRouteCommand(
                                subRoutes[0].FromSignal,
                                subRoutes[^1].ToSignal,
                                state,
                                mergedPointCommands)
                            {
                                IntermediateSignals = intermediateSignals
                            };
                            _ = await TrySetTrainRoute(mergedRoute, cancellationToken);
                        }
                    }
                }
                else if (command.Length == 5)
                {
                    var trainRouteCommand = FindAndSetState(command[0..2].ToIntOrZero, command[2..4].ToIntOrZero, command[^1].TrainRouteState);
                    _ = await TrySetTrainRoute(trainRouteCommand, cancellationToken);
                }
                else if (command.Length > 1 && command.Length < 5 && command[^1].IsTrainRouteTeardownCommand)
                {
                    var trainRouteCommand = new TrainRouteCommand(0, command[0..^1].ToIntOrZero, command[^1].TrainRouteState, []);
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
                if (trainRouteCommand.HasAddress)
                {
                    await _yardController.SendRouteCommandAsync(trainRouteCommand, cancellationToken);
                }
                else
                {
                    foreach (var pointCommand in trainRouteCommand.PointCommands)
                    {
                        await _yardController.SendPointSetCommandsAsync(pointCommand, cancellationToken);
                        if (pointCommand.AlsoLock)
                            await _yardController.SendPointLockCommandsAsync(pointCommand, cancellationToken);
                    }
                }
                _pointLockings.CommitLocks(trainRouteCommand);

                // Send GO for FROM and intermediate signals
                var goSignals = new List<int>();
                // Don't set InboundMain signal to green for shunting routes
                if (!(trainRouteCommand.State == TrainRouteState.SetShunting
                    && _signalsByNumber.TryGetValue(trainRouteCommand.FromSignal, out var fromSignal)
                    && fromSignal.Type == SignalType.InboundMain))
                    goSignals.Add(trainRouteCommand.FromSignal);
                goSignals.AddRange(trainRouteCommand.IntermediateSignals);
                // For main routes: also set outbound main signal (TO signal) to Go
                if (trainRouteCommand.State == TrainRouteState.SetMain
                    && _signalsByNumber.TryGetValue(trainRouteCommand.ToSignal, out var toSignal)
                    && toSignal.Type == SignalType.OutboundMain)
                    goSignals.Add(trainRouteCommand.ToSignal);

                foreach (var signalNumber in goSignals)
                {
                    if (_signalsByNumber.TryGetValue(signalNumber, out var signal))
                        await _yardController.SendSignalCommandAsync(
                            new SignalCommand(signalNumber, signal.Address, SignalState.Go), cancellationToken);
                }

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
        else if (trainRouteCommand.IsTeardown)
        {
            var fromSignal = trainRouteCommand.FromSignal;
            if (fromSignal == 0)
                fromSignal = _pointLockings.CurrentRoutes.FirstOrDefault(r => r.ToSignal == trainRouteCommand.ToSignal)?.FromSignal ?? 0;

            // Guard against double-cancel: if already pending release, skip
            if (_pendingReleases.ContainsKey(trainRouteCommand.ToSignal))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Route to signal {ToSignal} already cancelling, ignoring duplicate", trainRouteCommand.ToSignal);
                return false;
            }

            // Capture signals to potentially STOP before ClearLocks removes the route
            var existingRoute = _pointLockings.CurrentRoutes.FirstOrDefault(r => r.ToSignal == trainRouteCommand.ToSignal);
            var routeSignals = existingRoute is not null
                ? new List<int> { existingRoute.FromSignal }.Concat(existingRoute.IntermediateSignals).ToList()
                : [];
            // Include outbound main TO signal if it was set to Go (main route only)
            if (existingRoute is not null
                && existingRoute.State == TrainRouteState.SetMain
                && _signalsByNumber.TryGetValue(existingRoute.ToSignal, out var toSignal)
                && toSignal.Type == SignalType.OutboundMain)
                routeSignals.Add(existingRoute.ToSignal);

            // Phase 1: Immediately stop signals
            foreach (var signalNumber in routeSignals)
            {
                if (!IsSignalNeededByOtherRoute(signalNumber, trainRouteCommand.ToSignal) && _signalsByNumber.TryGetValue(signalNumber, out var signal))
                    await _yardController.SendSignalCommandAsync(
                        new SignalCommand(signalNumber, signal.Address, SignalState.Stop), cancellationToken);
            }

            var isShuntingRoute = existingRoute?.State == TrainRouteState.SetShunting;
            var delaySeconds = isShuntingRoute ? 0 : GetLockReleaseDelaySeconds();
            if (delaySeconds > 0)
            {
                // Phase 1: Notify UI to show blue (cancelling state), locks remain held
                _trainRouteNotificationService.NotifyRouteCancelling(
                    trainRouteCommand with { FromSignal = fromSignal },
                    string.Format(Messages.RouteCancelling, fromSignal, trainRouteCommand.ToSignal));
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Route {From}-{To} cancelling, locks held for {Delay} seconds",
                        fromSignal, trainRouteCommand.ToSignal, delaySeconds);

                // Schedule Phase 2 after delay
                var cts = new CancellationTokenSource();
                _pendingReleases[trainRouteCommand.ToSignal] = cts;
                var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken).Token;
                var capturedFromSignal = fromSignal;
                var capturedCommand = trainRouteCommand;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), linkedToken);
                        await ExecutePhase2Release(capturedCommand, capturedFromSignal, cancellationToken);
                    }
                    catch (OperationCanceledException) { }
                }, linkedToken);
            }
            else
            {
                // No delay: immediate Phase 2 (original behaviour, always used for shunting routes)
                await ExecutePhase2Release(trainRouteCommand, fromSignal, cancellationToken);
            }
            return true;
        }
        return false;
    }

    private bool IsSignalNeededByOtherRoute(int signalNumber, int excludeRouteToSignal) =>
        _pointLockings.CurrentRoutes.Any(route =>
            route.ToSignal != excludeRouteToSignal &&
            (route.FromSignal == signalNumber || route.IntermediateSignals.Contains(signalNumber)));

    private async Task ExecutePhase2Release(TrainRouteCommand trainRouteCommand, int fromSignal, CancellationToken cancellationToken)
    {
        var releasedPoints = _pointLockings.ClearLocks(trainRouteCommand);
        foreach (var pointCommand in releasedPoints)
        {
            if (pointCommand.AlsoUnlock)
                await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
        }
        _pendingReleases.Remove(trainRouteCommand.ToSignal);
        _trainRouteNotificationService.NotifyRouteCleared(trainRouteCommand with { FromSignal = fromSignal }, string.Format(Messages.RouteCleared, fromSignal, trainRouteCommand.ToSignal));
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Locks released for train route command {TrainRouteCommand}", trainRouteCommand);
    }

    private int GetLockReleaseDelaySeconds() =>
        _hostEnvironment.IsDevelopment() ? 5 : _yardDataService.LockReleaseDelaySeconds;

    #region Disposable Support
    private bool disposedValue;
    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                foreach (var cts in _pendingReleases.Values) cts.Cancel();
                _pendingReleases.Clear();
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
