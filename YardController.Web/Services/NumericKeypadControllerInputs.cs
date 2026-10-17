using System.Diagnostics;
using System.Text;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using YardController.Web.Resources;
using System.Globalization;

namespace YardController.Web.Services;

public sealed partial class NumericKeypadControllerInputs(ILogger<NumericKeypadControllerInputs> logger, IYardController yardController, TrainRouteLockingsManager lockingsManager, IYardDataService yardDataService, IBufferedKeyReader keyReader, ITrainRouteNotificationService trainRouteNotificationService, IPointNotificationService pointNotificationService, ISignalStateService signalStateService, IPointPositionService pointPositionService, IHostEnvironment hostEnvironment, ITrainNumberService trainNumberService, IShuntingAreaService shuntingAreaService, IOccupancyHoldService occupancyHoldService, IEntryStateService entryStateService) : BackgroundService
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IYardController _yardController = yardController;
    private readonly IYardDataService _yardDataService = yardDataService;
    private readonly TrainRouteLockingsManager _lockingsManager = lockingsManager;
    private readonly IBufferedKeyReader _keyReader = keyReader;
    private readonly ITrainRouteNotificationService _trainRouteNotificationService = trainRouteNotificationService;
    private readonly IPointNotificationService _pointNotificationService = pointNotificationService;
    private readonly ISignalStateService _signalStateService = signalStateService;
    private readonly IPointPositionService _pointPositionService = pointPositionService;
    private readonly IHostEnvironment _hostEnvironment = hostEnvironment;
    private readonly ITrainNumberService _trainNumberService = trainNumberService;
    private readonly IShuntingAreaService _shuntingAreaService = shuntingAreaService;
    private readonly IOccupancyHoldService _occupancyHoldService = occupancyHoldService;
    private readonly IEntryStateService _entryStateService = entryStateService;
    private readonly Stopwatch _stopwatch = new();
    private readonly Dictionary<int, CancellationTokenSource> _pendingReleases = new();
    private Dictionary<int, Point> _points = [];
    private IReadOnlyList<TrainRouteCommand> _trainRouteCommands = [];
    private Dictionary<int, TurntableTrack> _turntableTracks = [];
    private Dictionary<int, Signal> _signalsByNumber = [];
    private Dictionary<int, ShuntingArea> _shuntingAreas = [];
    private YardTopology _topology = YardTopology.Empty;
    private IReadOnlyList<OccupancySection> _occupancySections = [];
    private IReadOnlyDictionary<int, OccupancySectionTrack> _occupancySectionTracks = new Dictionary<int, OccupancySectionTrack>();
    private string _currentStation = "";
    private readonly List<QueuedRoute> _queuedRoutes = [];
    private volatile bool _queueDrainPending;
    private string _publishedEntry = "";

    private record QueuedRoute(string StationName, TrainRouteCommand Route, string? TrainNumber, OperatorRole Role);

    private TrainRouteLockings CurrentLockings => _lockingsManager.GetForStation(_currentStation);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        LogStarting();

        // Subscribe to data changes
        _yardDataService.DataChanged += OnDataChanged;

        // Load initial configuration from YardDataService
        LoadConfigurationFromService();

        return base.StartAsync(cancellationToken);
    }

    private void OnDataChanged(StationDataChange args)
    {
        LogDataChanged(args.StationName);

        // Reload if the changed station is the one we're currently working with
        if (args.StationName.Equals(_currentStation, StringComparison.OrdinalIgnoreCase))
            LoadConfigurationForStation(args.StationName);

        if (args.HasErrors)
        {
            LogDataHasValidationErrors(args.LoadErrors.Count, args.ValidationResult?.InvalidRoutes.Count ?? 0);
        }
    }

    private void LoadConfigurationFromService()
    {
        // Load the default/active station on startup
        _currentStation = _yardDataService.CurrentStationName;
        LoadConfigurationForStation(_currentStation);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogStopping();
        _yardDataService.DataChanged -= OnDataChanged;

        // Cancel any pending delayed releases
        foreach (var cts in _pendingReleases.Values) await cts.CancelAsync();
        _pendingReleases.Clear();
        _queuedRoutes.Clear();

        // Release all physical locks across all stations by sending unlock commands to hardware
        foreach (var (_, lockings) in _lockingsManager.All)
        {
            foreach (var pointCommand in lockings.PointCommands)
            {
                if (pointCommand.AlsoUnlock)
                    await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
            }
            lockings.ReleaseAllLocks();
        }

        await base.StopAsync(cancellationToken);
    }

    private void LoadConfigurationForStation(string stationName)
    {
        var stationData = _yardDataService.GetStationData(stationName);
        if (stationData is null) return;
        _currentStation = stationName;
        _points = stationData.Points.ToDictionary(p => p.Number);
        _turntableTracks = stationData.TurntableTracks.ToDictionary(tt => tt.Number);
        _trainRouteCommands = stationData.TrainRoutes;
        _signalsByNumber = stationData.Signals
            .Where(s => int.TryParse(s.Name, out _))
            .ToDictionary(s => int.Parse(s.Name, CultureInfo.InvariantCulture));
        _shuntingAreas = stationData.ShuntingAreas.ToDictionary(a => a.Number);
        _topology = stationData.Topology;
        _occupancySections = stationData.OccupancySections;
        // Resolving every stretch to its track costs a shortest-path search each, so it is done
        // once per load rather than per command.
        _occupancySectionTracks = _topology.MapOccupancySectionTracks(_occupancySections);

        LogConfigurationLoaded(stationName, _points.Count, _trainRouteCommands.Count);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStartReadingKeys();
        StringBuilder inputKeys = new(10);
        string inputStation = "";
        var inputRole = OperatorRole.Dispatcher;
        while (!stoppingToken.IsCancellationRequested)
        {
            // Before the wait, not after it: the wait below restarts its stopwatch on every pass
            // and so does not end until a key arrives, and the moment a key arrives is precisely
            // when the line should not be cleared.
            if (inputKeys.Length == 0 && _publishedEntry.Length > 0) PublishEntryCleared();

            _stopwatch.Restart();
            while (_keyReader.KeyNotAvailable && !_queueDrainPending && _stopwatch.ElapsedMilliseconds < 250)
            {
                await Task.Delay(100, stoppingToken);
                if (stoppingToken.IsCancellationRequested) return;
                _stopwatch.Restart();
            }
            if (_queueDrainPending)
            {
                _queueDrainPending = false;
                await TryExecuteQueuedRoutes(stoppingToken);
            }
            var (keyInfo, stationName, role) = _keyReader.ReadKeyWithStation();
            if (keyInfo.IsEmpty) continue;

            // Use active station as fallback for keys without station context (e.g. console/UDP input)
            if (string.IsNullOrEmpty(stationName))
                stationName = _yardDataService.CurrentStationName;

            // If station or role changes mid-command, clear the buffer and switch
            if (!string.IsNullOrEmpty(inputStation) && !stationName.Equals(inputStation, StringComparison.OrdinalIgnoreCase))
            {
                LogStationChangedMidCommand(inputStation, stationName);
                inputKeys.Clear();
            }
            else if (inputKeys.Length > 0 && role != inputRole)
            {
                LogRoleChangedMidCommand(inputRole, role);
                inputKeys.Clear();
            }
            inputStation = stationName;
            inputRole = role;

            // Ensure we have the right station data loaded
            if (!_currentStation.Equals(stationName, StringComparison.OrdinalIgnoreCase))
                LoadConfigurationForStation(stationName);
            LogKeyReceived(keyInfo.Key, keyInfo.KeyChar, keyInfo.KeyChar);
            var key = keyInfo.ValidCharOrNull;
            if (key is null)
            {
                LogUnrecognizedKey(keyInfo.Key, keyInfo.KeyChar, keyInfo.KeyChar);
                continue;
            }
            inputKeys.Append(key);
            PublishEntry(inputKeys, stationName, role);
            if (inputKeys.IsClearAllTrainRoutes || inputKeys.IsCancelAllTrainRoutes)
            {
                if (IsDispatcherOnly(role)) { inputKeys.Clear(); continue; }
                var isCancelAll = inputKeys.IsCancelAllTrainRoutes;

                // Cancel any pending delayed releases
                foreach (var cts in _pendingReleases.Values) await cts.CancelAsync();
                _pendingReleases.Clear();

                // Phase 1: Send STOP for all GO signals immediately
                var goSignalNumbers = CurrentLockings.CurrentRoutes
                    .SelectMany(r => new[] { r.FromSignal }.Concat(r.IntermediateSignals))
                    .Distinct();
                foreach (var signalNumber in goSignalNumbers)
                {
                    if (_signalsByNumber.TryGetValue(signalNumber, out var signal))
                        await _yardController.SendSignalCommandAsync(
                            new SignalCommand(signalNumber, signal.Address, SignalState.Stop), stoppingToken);
                }

                // Release shunting route locks immediately (no delay) and notify UI
                var shuntingRoutes = CurrentLockings.CurrentRoutes
                    .Where(r => r.State == TrainRouteState.SetShunting).ToList();
                foreach (var shuntingRoute in shuntingRoutes)
                {
                    var released = CurrentLockings.ClearLocks(shuntingRoute with { State = TrainRouteState.Clear });
                    foreach (var pointCommand in released)
                    {
                        if (pointCommand.AlsoUnlock)
                            await _yardController.SendPointUnlockCommandsAsync(pointCommand, stoppingToken);
                    }
                    // Both operators are told: the command is the dispatcher's, the routes it
                    // takes down are the shunter's.
                    _trainRouteNotificationService.NotifyRouteCleared(_currentStation, shuntingRoute,
                        RouteMessages.Cleared(isShunting: true, shuntingRoute.FromSignal, shuntingRoute.ToSignal));
                }

                // Cancel all: remove all train numbers
                // Clear all: remove train numbers only at outbound main destination signals
                if (isCancelAll)
                {
                    _trainNumberService.ClearAll(_currentStation);
                    // Clear all queued routes
                    foreach (var queued in _queuedRoutes.Where(q => q.StationName.Equals(_currentStation, StringComparison.OrdinalIgnoreCase)))
                    {
                        _trainRouteNotificationService.NotifyQueuedRouteCancelled(
                            _currentStation, queued.Route,
                            RouteMessages.QueuedCancelled(queued.Route.State.IsShunting, queued.Route.ToSignal),
                            queued.Role);
                    }
                    _queuedRoutes.RemoveAll(q => q.StationName.Equals(_currentStation, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    foreach (var route in CurrentLockings.CurrentRoutes)
                    {
                        if (_signalsByNumber.TryGetValue(route.ToSignal, out var toSignal)
                            && toSignal.Type == SignalType.OutboundMain
                            && RouteSection(route, route.ToSignal) is { } outboundSection)
                            _trainNumberService.RemoveTrainNumber(_currentStation, outboundSection);
                    }
                }

                var delaySeconds = GetLockReleaseDelaySeconds();
                if (delaySeconds > 0 && CurrentLockings.CurrentRoutes.Count > 0)
                {
                    // Phase 1: Notify UI to show purple (cancelling state) for remaining main routes
                    _trainRouteNotificationService.NotifyAllRoutesCancelling(_currentStation, Messages.AllRoutesCancelling);
                    LogAllRoutesCancelling(delaySeconds);

                    // Phase 2: Release locks after delay
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                            foreach (var pointCommand in CurrentLockings.PointCommands)
                            {
                                if (pointCommand.AlsoUnlock)
                                    await _yardController.SendPointUnlockCommandsAsync(pointCommand, stoppingToken);
                            }
                            CurrentLockings.ReleaseAllLocks();
                            _trainRouteNotificationService.NotifyAllRoutesCleared(_currentStation, Messages.AllRoutesCleared);
                            if (!isCancelAll) _queueDrainPending = true;
                        }
                        catch (OperationCanceledException) { }
                    }, stoppingToken);
                }
                else
                {
                    // No delay: immediate release (original behaviour)
                    foreach (var pointCommand in CurrentLockings.PointCommands)
                    {
                        if (pointCommand.AlsoUnlock)
                            await _yardController.SendPointUnlockCommandsAsync(pointCommand, stoppingToken);
                    }
                    CurrentLockings.ReleaseAllLocks();
                    _trainRouteNotificationService.NotifyAllRoutesCleared(_currentStation, Messages.AllRoutesCleared);
                    if (!isCancelAll) _queueDrainPending = true;
                }
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsAllSignalsStop)
            {
                if (IsDispatcherOnly(role)) { inputKeys.Clear(); continue; }
                foreach (var (signalNumber, signal) in _signalsByNumber)
                {
                    if (_signalStateService.GetSignalState(_currentStation, signalNumber) == SignalState.Go)
                        await _yardController.SendSignalCommandAsync(
                            new SignalCommand(signalNumber, signal.Address, SignalState.Stop) { FeedbackAddress = signal.FeedbackAddress }, stoppingToken);
                }
                LogAllSignalsSetToStop();
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsAllPointsStraight)
            {
                if (IsDispatcherOnly(role)) { inputKeys.Clear(); continue; }
                var releasedToShunter = ReleasedShunterPointNumbers();
                foreach (var (pointNumber, point) in _points)
                {
                    if (point.IsAddressOnly || releasedToShunter.Contains(pointNumber)) continue;
                    var pointCommand = PointCommand.Create(pointNumber, PointPosition.Straight, _points.AddressesFor(pointNumber, PointPosition.Straight));
                    if (!pointCommand.IsUndefined && !CurrentLockings.IsLocked(pointCommand))
                    {
                        await _yardController.SendPointSetCommandsAsync(pointCommand, stoppingToken);
                        var localizedPosition = Messages.LocalizedPosition(pointCommand.Position);
                        _pointNotificationService.NotifyPointSet(_currentStation, pointCommand, string.Format(CultureInfo.CurrentCulture, Messages.PointSet, pointCommand.Number, localizedPosition), role);
                    }
                }
                LogAllPointsSetToStraight();
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsReloadConfiguration)
            {
                if (IsDispatcherOnly(role)) { inputKeys.Clear(); continue; }
                await _yardDataService.ReloadAllAsync().ConfigureAwait(false);
                inputKeys.Clear();
                continue;
            }
            else if (inputKeys.IsTurntableCommand)
            {
                if (IsDispatcherOnly(role)) { inputKeys.Clear(); continue; }
                var command = inputKeys.CommandString;
                var number = command[1..^1].ToIntOrZero;
                var direction = command.TurntableDirection;
                if (!_turntableTracks.TryGetValue(number, out var turntableTrack))
                {
                    LogNoSuchTurntableTrack(number);
                    continue;
                }
                var pointCommand = PointCommand.Create(turntableTrack.Number, direction, [turntableTrack.Address]);
                await _yardController.SendPointSetCommandsAsync(pointCommand, stoppingToken);
            }
            else if (inputKeys.IsShuntingAreaCommand)
            {
                var command = inputKeys.CommandString;
                await HandleShuntingAreaCommand(command, role, stoppingToken);
            }
            else if (inputKeys.IsSignalCommand)
            {
                if (IsDispatcherOnly(role)) { inputKeys.Clear(); continue; }
                var command = inputKeys.CommandString;
                await HandleSignalCommand(command, role, stoppingToken);
            }
            else if (inputKeys.IsReleaseAllOccupancyHolds)
            {
                if (IsDispatcherOnly(role)) { inputKeys.Clear(); continue; }
                inputKeys.Clear();
                ReleaseAllOccupancyHolds(role);
            }
            else if (inputKeys.IsOccupancyHoldCommand)
            {
                if (IsDispatcherOnly(role)) { inputKeys.Clear(); continue; }
                var command = inputKeys.CommandString;
                HandleOccupancyHoldCommand(command, role);
            }
            else if (inputKeys.IsPointCommand)
            {
                var command = inputKeys.CommandString;
                var number = command[0..^1].ToIntOrZero;
                if (!_points.ContainsKey(number))
                {
                    LogNoSuchPointNumber(number);
                    _pointNotificationService.NotifyPointRejected(_currentStation, number, string.Format(CultureInfo.CurrentCulture, Messages.PointNotFound, number), role);
                    inputKeys.Clear();
                    continue;
                }

                if (!IsPointPermitted(number, role, out var pointRejection))
                {
                    LogPointNotPermitted(number, role);
                    _pointNotificationService.NotifyPointRejected(_currentStation, number, pointRejection, role);
                    inputKeys.Clear();
                    continue;
                }

                var position = command[^1].ToPointPosition;
                var pointCommand = PointCommand.Create(number, position, _points.AddressesFor(number, position));
                if (pointCommand.IsUndefined)
                {
                    LogInvalidPointCommand(command);
                    _pointNotificationService.NotifyPointRejected(_currentStation, number, string.Format(CultureInfo.CurrentCulture, Messages.PointInvalidCommand, command), role);
                    inputKeys.Clear();
                    continue;
                }
                else if (CurrentLockings.IsLocked(pointCommand))
                {
                    LogPointCommandLocked(pointCommand);
                    _pointNotificationService.NotifyPointLocked(_currentStation, pointCommand, string.Format(CultureInfo.CurrentCulture, Messages.PointLocked, pointCommand.Number), role);
                    inputKeys.Clear();
                    continue;
                }

                // Expand the slave cascade. Reject the master if cascade is contradictory or a slave is locked at the opposite position.
                IReadOnlyList<PointCommand> cascaded;
                try
                {
                    cascaded = pointCommand.ExpandWithSlaves(_points);
                }
                catch (InvalidPointCascadeException ex)
                {
                    LogSlaveCascadeConflict(pointCommand, ex.Message);
                    _pointNotificationService.NotifyPointRejected(_currentStation, number, ex.Message, role);
                    continue;
                }
                // A point coupled to a released one moves with it, so the dispatcher may not touch it either.
                if (role == OperatorRole.Dispatcher)
                {
                    var released = ReleasedShunterPointNumbers();
                    var releasedSlave = cascaded.Skip(1).FirstOrDefault(c => released.Contains(c.Number));
                    if (releasedSlave is not null)
                    {
                        LogCascadedSlaveReleasedToShunter(releasedSlave, pointCommand);
                        _pointNotificationService.NotifyPointRejected(_currentStation, number, string.Format(CultureInfo.CurrentCulture, Messages.PointReleasedToShunter, releasedSlave.Number), role);
                        inputKeys.Clear();
                        continue;
                    }
                }

                var blockedSlave = cascaded.Skip(1).FirstOrDefault(c => CurrentLockings.IsLocked(c));
                if (blockedSlave is not null)
                {
                    LogCascadedSlaveLocked(blockedSlave, pointCommand);
                    _pointNotificationService.NotifyPointLocked(_currentStation, pointCommand, string.Format(CultureInfo.CurrentCulture, Messages.PointLocked, blockedSlave.Number), role);
                    continue;
                }

                // A crossing carries nothing that moves, so what keeps its two ways apart are the
                // points leading into it. Laying one way while the other is still laid would set
                // both tracks through the same rails, and is refused with the point that is in
                // the way named — the whole cascade is asked, since a coupled point lays a way as
                // surely as the one that was typed.
                if (IsCrossingInTheWay(cascaded, out var crossingRejection))
                {
                    LogPointBlockedByCrossing(pointCommand, crossingRejection);
                    _pointNotificationService.NotifyPointRejected(_currentStation, number, crossingRejection, role);
                    inputKeys.Clear();
                    continue;
                }

                var isAlreadyInPosition = _pointPositionService.GetPosition(_currentStation, number) == position;
                foreach (var pc in cascaded)
                    await _yardController.SendPointSetCommandsAsync(pc, stoppingToken);
                var localizedPosition = Messages.LocalizedPosition(pointCommand.Position);
                if (isAlreadyInPosition)
                    _pointNotificationService.NotifyPointAlreadyInPosition(_currentStation, pointCommand, string.Format(CultureInfo.CurrentCulture, Messages.PointAlreadyInPosition, pointCommand.Number, localizedPosition), role);
                else
                    _pointNotificationService.NotifyPointSet(_currentStation, pointCommand, string.Format(CultureInfo.CurrentCulture, Messages.PointSet, pointCommand.Number, localizedPosition), role);

                // Notify for any cascaded slaves so the UI position service updates them too.
                // (In production, hardware feedback would update them; in dev mode the notification is the only signal.)
                foreach (var slave in cascaded.Skip(1))
                {
                    var slavePosition = Messages.LocalizedPosition(slave.Position);
                    _pointNotificationService.NotifyPointSet(_currentStation, slave, string.Format(CultureInfo.CurrentCulture, Messages.PointSet, slave.Number, slavePosition), role);
                }
            }
            else if (inputKeys.IsTrainRouteCommand)
            {
                var command = inputKeys.CommandString;
                LogTrainRouteCommandEntered(command);

                // Extract train number if command contains '=' separator (e.g. "2133=1234#" or "21.33=1234#")
                string? trainNumber = null;
                var equalsIndex = command.IndexOf('=');
                if (equalsIndex >= 0)
                {
                    trainNumber = command[(equalsIndex + 1)..^1];
                    command = command[..equalsIndex] + command[^1];
                }

                if (equalsIndex >= 0 && IsDispatcherOnly(role)) continue;

                // A dot beside the signal names the stretch running off the layout on that side:
                // {signal}.=[ENTER] runs a departing train out past the signal, and both
                // {signal}.=[trainNumber][ENTER] and .{signal}=[trainNumber][ENTER] put a number
                // straight onto an edge stretch. Read before the composite form, which needs a
                // signal on both sides of every dot.
                if (equalsIndex >= 0 && command[^1].TrainRouteState == TrainRouteState.SetMain
                    && command[0..^1].ToEdgeSignal() is { } edge)
                {
                    await HandleEdgeTrainNumberCommand(edge.Signal, edge.Beyond, trainNumber, stoppingToken);
                }
                else if (command.Contains(char.SignalDivider))
                {
                    var state = command[^1].TrainRouteState;
                    var parts = command[0..^1].Split(char.SignalDivider);

                    if (state.IsTeardown)
                    {
                        // For cancel/clear, just teardown by the final destination signal
                        var toSignalNumber = parts[^1].ToIntOrZero;
                        _ = await TrySetTrainRoute(new TrainRouteCommand(0, toSignalNumber, state, []), role, stoppingToken, trainNumber);
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
                                var trainRouteCommand = FindAndSetState(fromSignalNumber, toSignalNumber, state, role);
                                if (trainRouteCommand is not null)
                                    subRoutes.Add(trainRouteCommand);
                                else
                                    LogPartOfRouteNotFound(fromSignalNumber, toSignalNumber);
                            }
                        }
                        if (subRoutes.Count < parts.Length - 1)
                        {
                            LogIncompleteRouteCommand(command);
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
                            // A shunting signal anywhere along the composite makes the whole
                            // movement a shunting movement, so any shunting sub-route wins.
                            var mergedState = subRoutes.Any(r => r.State == TrainRouteState.SetShunting)
                                ? TrainRouteState.SetShunting
                                : subRoutes[0].State;
                            var mergedRoute = new TrainRouteCommand(
                                subRoutes[0].FromSignal,
                                subRoutes[^1].ToSignal,
                                mergedState,
                                mergedPointCommands)
                            {
                                IntermediateSignals = intermediateSignals
                            };
                            _ = await TrySetTrainRoute(mergedRoute, role, stoppingToken, trainNumber);
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(trainNumber) && command.Length == 1 && command[^1].TrainRouteState == TrainRouteState.SetMain)
                {
                    // =[trainNumber][ENTER] — train-only form: advance train forward, or remove if no next signal
                    await AdvanceTrainNumber(trainNumber, stoppingToken);
                }
                else if (trainNumber is { Length: 0 } && command.Length > 1 && command.Length < 5 && command[^1].TrainRouteState == TrainRouteState.SetMain)
                {
                    // [signal]=[ENTER] — signal-only form: move the train up to the stretch in front of that signal
                    await MoveTrainNumberToSignal(command[0..^1].ToIntOrZero, stoppingToken);
                }
                else if (!string.IsNullOrEmpty(trainNumber) && command.Length > 1 && command.Length < 5 && command[^1].TrainRouteState == TrainRouteState.SetMain)
                {
                    // [signal]=[trainNumber][ENTER] — full form: put a train number that is not running a
                    // route on the stretch in front of a signal, so the diagram shows what stands there.
                    var signalNumber = command[0..^1].ToIntOrZero;
                    if (signalNumber > 0 && _signalsByNumber.ContainsKey(signalNumber)
                        && ResolveSectionLeadingUpTo(signalNumber) is { } section)
                    {
                        _trainNumberService.AssignTrainNumber(_currentStation, section, trainNumber);
                        LogTrainNumberAssigned(trainNumber, section);
                    }
                    else
                    {
                        LogUnknownSignalForTrainNumber(signalNumber);
                    }
                }
                else if (command.Length == 5)
                {
                    var trainRouteCommand = FindAndSetState(command[0..2].ToIntOrZero, command[2..4].ToIntOrZero, command[^1].TrainRouteState, role);
                    _ = await TrySetTrainRoute(trainRouteCommand, role, stoppingToken, trainNumber);
                }
                else if (command.Length > 1 && command.Length < 5 && command[^1].IsTrainRouteTeardownCommand)
                {
                    var trainRouteCommand = new TrainRouteCommand(0, command[0..^1].ToIntOrZero, command[^1].TrainRouteState, []);
                    _ = await TrySetTrainRoute(trainRouteCommand, role, stoppingToken, trainNumber);
                }
                else
                {
                    LogUnrecognizedCommand(command, BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(command)));
                }
            }

            else if (key.IsClearCommand)
            {
                inputKeys.Clear();
                LogCommandCleared();
            }
        }
    }

    /// <summary>
    /// Publishes the characters typed so far (Specifications/InputDevices.md §8.3). The keypad's
    /// grammar is postfix, so until the terminator arrives there is no command to name and no
    /// parameters to list — only the raw characters, which is exactly what a keypad operator wants
    /// to read back off the diagram.
    /// </summary>
    private void PublishEntry(StringBuilder inputKeys, string stationName, OperatorRole role)
    {
        var raw = inputKeys.ToString();
        if (raw == _publishedEntry) return;
        _publishedEntry = raw;
        _entryStateService.NotifyEntryChanged(new EntryState(
            stationName, role, PanelCommand.None, ButtonSet.Main,
            [], "", false, [], false, raw, null));
    }

    private void PublishEntryCleared()
    {
        _publishedEntry = "";
        _entryStateService.NotifyEntryCleared(_currentStation);
    }

    private TrainRouteCommand? FindAndSetState(int fromSignalNumber, int toSignalNumber, TrainRouteState state, OperatorRole role)
    {
        var resolvedState = ResolveRouteState(fromSignalNumber, toSignalNumber, state, role);
        var trainRouteCommand = _trainRouteCommands.FirstOrDefault(tp => tp.FromSignal == fromSignalNumber && tp.ToSignal == toSignalNumber);
        if (trainRouteCommand is null || trainRouteCommand.IsUndefined)
        {
            var notFoundRoute = new TrainRouteCommand(fromSignalNumber, toSignalNumber, resolvedState, []);
            _trainRouteNotificationService.NotifyRouteRejected(_currentStation, notFoundRoute,
                RouteMessages.NotFound(resolvedState.IsShunting, fromSignalNumber, toSignalNumber), role);
            LogNoTrainRouteFound(fromSignalNumber, toSignalNumber);
            return null;
        }
        return trainRouteCommand with { State = resolvedState };
    }

    /// <summary>
    /// A route leaving or ending at a shunting-only signal can never be a main route, and neither
    /// can a route a shunter sets. A main route request is therefore set as a shunting route
    /// instead of being rejected.
    /// </summary>
    private TrainRouteState ResolveRouteState(int fromSignalNumber, int toSignalNumber, TrainRouteState state, OperatorRole role)
    {
        if (state != TrainRouteState.SetMain) return state;
        if (role == OperatorRole.Shunter) return TrainRouteState.SetShunting;
        var shuntingSignalNumber =
            IsShuntingOnly(fromSignalNumber) ? fromSignalNumber :
            IsShuntingOnly(toSignalNumber) ? toSignalNumber :
            0;
        if (shuntingSignalNumber == 0) return state;
        LogShuntingSignalDemotesRoute(shuntingSignalNumber, fromSignalNumber, toSignalNumber);
        return TrainRouteState.SetShunting;
    }

    private bool IsShuntingOnly(int signalNumber) =>
        _signalsByNumber.TryGetValue(signalNumber, out var signal) && signal.Type.IsShuntingOnly;

    /// <summary>The areas that are handed over to a shunter right now.</summary>
    private IReadOnlyList<ShuntingArea> ReleasedAreas() =>
        [.. _shuntingAreas.Values
            .Where(area => _shuntingAreaService.GetState(_currentStation, area.Number) == ShuntingAreaState.Released)];

    /// <summary>The points the shunter may move: those declared in every area that is released.</summary>
    private HashSet<int> ReleasedShunterPointNumbers() => ReleasedAreas().ShunterPoints();

    /// <summary>
    /// Rejects a command that reaches beyond the released shunting areas. Everything except an
    /// area's own points and the routes inside it belongs to the dispatcher alone.
    /// </summary>
    private bool IsDispatcherOnly(OperatorRole role)
    {
        if (role != OperatorRole.Shunter) return false;
        LogCommandNotPermittedForShunter();
        _pointNotificationService.NotifyPointRejected(_currentStation, 0, Messages.CommandNotPermitted, role);
        return true;
    }

    /// <summary>
    /// A released area's points are the shunter's alone: the shunter may move nothing else, and
    /// the dispatcher may no longer move them.
    /// </summary>
    private bool IsPointPermitted(int pointNumber, OperatorRole role, out string rejection)
    {
        var released = ReleasedShunterPointNumbers();
        rejection = role == OperatorRole.Shunter
            ? released.Contains(pointNumber) ? "" : string.Format(CultureInfo.CurrentCulture, Messages.PointNotInShuntingArea, pointNumber)
            : released.Contains(pointNumber) ? string.Format(CultureInfo.CurrentCulture, Messages.PointReleasedToShunter, pointNumber) : "";
        return rejection.Length == 0;
    }

    /// <summary>
    /// Whether a crossing refuses the commands about to be given, and why. A command that lays a
    /// way through a crossing brings that way's conditions with it: every one of them must already
    /// hold, since nothing here is going to move a second point on the operator's behalf. Whether
    /// the point in the way is locked is worth saying — a lock names a route standing over the
    /// crossing, a bare wrong position names a point the operator can simply turn.
    /// </summary>
    private bool IsCrossingInTheWay(IReadOnlyList<PointCommand> commands, out string rejection)
    {
        rejection = "";
        foreach (var command in commands)
        {
            foreach (var (_, condition, _) in _topology.CrossingConditionsInTheWay(command, PositionOfPoint))
            {
                // A condition these very commands satisfy is no condition at all: the cascade that
                // lays the way may be the same one that turns the other point out of it.
                if (commands.Any(c => c.Number == condition.Number && c.Position == condition.Position)) continue;
                rejection = string.Format(CultureInfo.CurrentCulture, 
                    CurrentLockings.IsLocked(condition) ? Messages.PointBlockedByCrossingLocked : Messages.PointBlockedByCrossing,
                    command.Number, Messages.LocalizedPosition(command.Position),
                    condition.Number, Messages.LocalizedPosition(condition.Position));
                return true;
            }
        }
        return false;
    }

    private PointPosition PositionOfPoint(int number) => _pointPositionService.GetPosition(_currentStation, number);

    /// <summary>
    /// The dispatcher may not set a route that touches a released area; the shunter may set only
    /// routes that stay within the released areas, and may tear down only such routes.
    /// </summary>
    private bool IsRoutePermitted(TrainRouteCommand route, OperatorRole role, out string rejection)
    {
        rejection = "";
        var released = ReleasedShunterPointNumbers();
        if (route.IsTeardown)
        {
            if (role == OperatorRole.Dispatcher) return true;
            var existingRoute = CurrentLockings.CurrentRoutes.FirstOrDefault(r => r.ToSignal == route.ToSignal);
            if (existingRoute is null || IsWithinReleasedAreas(existingRoute, out var heldOutside)) return true;
            rejection = NotInShuntingAreaRejection(existingRoute, heldOutside);
            return false;
        }
        if (!route.IsSet) return true;
        if (role == OperatorRole.Shunter)
        {
            if (IsWithinReleasedAreas(route, out var outsidePoint)) return true;
            rejection = NotInShuntingAreaRejection(route, outsidePoint);
            return false;
        }
        var claimed = route.PointCommands.FirstOrDefault(pointCommand => released.Contains(pointCommand.Number));
        if (claimed is null) return true;
        rejection = string.Format(CultureInfo.CurrentCulture, Messages.RouteUsesReleasedPoint, route.FromSignal, route.ToSignal, claimed.Number);
        return false;
    }

    private bool IsWithinReleasedAreas(TrainRouteCommand route, out PointCommand? outsidePoint) =>
        route.IsWithinShuntingAreas(ReleasedAreas(), _points, out outsidePoint);

    /// <summary>
    /// Names the point that put the route out of the shunter's reach where one can be singled out;
    /// otherwise says only that the route is not theirs, because no single point is to blame.
    /// </summary>
    private static string NotInShuntingAreaRejection(TrainRouteCommand route, PointCommand? outsidePoint) =>
        outsidePoint is null
            ? string.Format(CultureInfo.CurrentCulture, Messages.RouteNotInShuntingArea, route.FromSignal, route.ToSignal)
            : string.Format(CultureInfo.CurrentCulture, Messages.RoutePointNotInShuntingArea, route.FromSignal, route.ToSignal, outsidePoint.Number);

    /// <param name="trainHasArrived">
    /// Set only by a teardown that follows a train reported at the route's final signal. The train
    /// is standing there, clear of the points, so the locks are released without LockReleaseDelay.
    /// </param>
    private async Task<bool> TrySetTrainRoute(TrainRouteCommand? trainRouteCommand, OperatorRole role, CancellationToken cancellationToken, string? trainNumber = null, bool trainHasArrived = false)
    {
        if (trainRouteCommand is null) return false;
        if (!IsRoutePermitted(trainRouteCommand, role, out var routeRejection))
        {
            LogRouteNotPermitted(trainRouteCommand, role);
            _trainRouteNotificationService.NotifyRouteRejected(_currentStation, trainRouteCommand, routeRejection, role);
            return false;
        }
        if (trainRouteCommand.IsSet)
        {
            if (CurrentLockings.CanReserveLocksFor(trainRouteCommand))
            {
                CurrentLockings.ReserveLocks(trainRouteCommand);
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
                CurrentLockings.CommitLocks(trainRouteCommand);

                // Send GO for FROM and intermediate signals
                var goSignals = new List<int> { trainRouteCommand.FromSignal };
                goSignals.AddRange(trainRouteCommand.IntermediateSignals);
                // The TO signal ends the movement, so it stays at stop whatever its type.
                // A shunting movement is authorised by the dwarf part of a signal, so every
                // main signal along the route stays at stop — the from signal included.
                if (trainRouteCommand.State == TrainRouteState.SetShunting)
                    goSignals.RemoveAll(number => _signalsByNumber.TryGetValue(number, out var signal) && signal.Type.IsMainOnly);

                foreach (var signalNumber in goSignals)
                {
                    if (_signalsByNumber.TryGetValue(signalNumber, out var signal))
                        await _yardController.SendSignalCommandAsync(
                            new SignalCommand(signalNumber, signal.Address, SignalState.Go), cancellationToken);
                }

                // The train has not moved yet, so its number goes on the stretch in front of the from
                // signal — for a train accepted from the previous station, the run in from the edge.
                // It stays there until the operator marks arrival via /, =#, or =N#.
                if (!string.IsNullOrEmpty(trainNumber) && RouteSection(trainRouteCommand, trainRouteCommand.FromSignal) is { } startSection)
                    _trainNumberService.AssignTrainNumber(_currentStation, startSection, trainNumber);

                _trainRouteNotificationService.NotifyRouteSet(_currentStation, trainRouteCommand,
                    RouteMessages.Set(trainRouteCommand.State.IsShunting, trainRouteCommand.FromSignal, trainRouteCommand.ToSignal), role);
                LogRouteLocksTaken(trainRouteCommand);

                return true;
            }
            else
            {
                // Queue the conflicting route for automatic execution when blocking route clears
                _queuedRoutes.Add(new QueuedRoute(_currentStation, trainRouteCommand, trainNumber, role));
                var effectiveTrainNumber = trainNumber ?? (RouteSection(trainRouteCommand, trainRouteCommand.FromSignal) is { } queuedSection
                    ? _trainNumberService.GetTrainNumber(_currentStation, queuedSection)
                    : null);
                _trainRouteNotificationService.NotifyRouteQueued(
                    _currentStation, trainRouteCommand,
                    RouteMessages.Queued(trainRouteCommand.State.IsShunting, trainRouteCommand.FromSignal, trainRouteCommand.ToSignal),
                    effectiveTrainNumber, role);
                LogRouteQueued(trainRouteCommand);
            }
        }
        else if (trainRouteCommand.IsTeardown)
        {
            // Active route takes precedence over queued: only cancel from queue when no active route ends here
            var existingRoute = CurrentLockings.CurrentRoutes.FirstOrDefault(r => r.ToSignal == trainRouteCommand.ToSignal);
            if (existingRoute is null)
            {
                var queuedIndex = _queuedRoutes.FindIndex(q =>
                    q.StationName.Equals(_currentStation, StringComparison.OrdinalIgnoreCase) &&
                    q.Route.ToSignal == trainRouteCommand.ToSignal);
                if (queuedIndex >= 0)
                {
                    var removed = _queuedRoutes[queuedIndex];
                    _queuedRoutes.RemoveAt(queuedIndex);
                    _trainRouteNotificationService.NotifyQueuedRouteCancelled(
                        _currentStation, removed.Route,
                        RouteMessages.QueuedCancelled(removed.Route.State.IsShunting, removed.Route.ToSignal), role);
                    LogQueuedRouteCancelled(removed.Route.ToSignal);
                    return true;
                }

                // No active or queued route — silently ignore
                LogNoRouteToTearDown(trainRouteCommand.ToSignal);
                return false;
            }

            var fromSignal = trainRouteCommand.FromSignal;
            if (fromSignal == 0)
                fromSignal = existingRoute.FromSignal;

            // Cancel (ESC) undoes the route — remove train number from FromSignal (and any leftover at ToSignal)
            // Clear (/) marks arrival — move train number from FromSignal to ToSignal (including outbound; it stays visible there until next station confirms via =[trainNumber][ENTER])
            var startSection = fromSignal > 0 ? RouteSection(existingRoute, fromSignal) : null;
            var endSection = RouteSection(existingRoute, trainRouteCommand.ToSignal);
            if (trainRouteCommand.State == TrainRouteState.Cancel)
            {
                if (endSection is { } cancelEnd) _trainNumberService.RemoveTrainNumber(_currentStation, cancelEnd);
                if (startSection is { } cancelStart) _trainNumberService.RemoveTrainNumber(_currentStation, cancelStart);
            }
            else if (trainRouteCommand.State == TrainRouteState.Clear && startSection is { } clearStart && endSection is { } clearEnd)
            {
                _trainNumberService.MoveTrainNumber(_currentStation, clearStart, clearEnd);
            }

            // Guard against double-cancel: if already pending release, skip
            if (_pendingReleases.ContainsKey(trainRouteCommand.ToSignal))
            {
                LogRouteAlreadyCancelling(trainRouteCommand.ToSignal);
                return false;
            }

            // Capture signals to potentially STOP before ClearLocks removes the route
            var routeSignals = new List<int> { existingRoute.FromSignal }.Concat(existingRoute.IntermediateSignals).ToList();

            // Phase 1: Immediately stop signals
            foreach (var signalNumber in routeSignals)
            {
                if (!IsSignalNeededByOtherRoute(signalNumber, trainRouteCommand.ToSignal) && _signalsByNumber.TryGetValue(signalNumber, out var signal))
                    await _yardController.SendSignalCommandAsync(
                        new SignalCommand(signalNumber, signal.Address, SignalState.Stop), cancellationToken);
            }

            var isShuntingRoute = existingRoute.State == TrainRouteState.SetShunting;
            // LockReleaseDelay protects a route torn down while a movement may still be running on
            // it, which is every teardown the operator asks for — clearing as much as cancelling,
            // since neither says where the train is. Only a train reported at the route's final
            // signal does: it stands there, clear of the points, so those locks go at once.
            var delaySeconds = isShuntingRoute || trainHasArrived ? 0 : GetLockReleaseDelaySeconds();
            if (delaySeconds > 0)
            {
                // Phase 1: Notify UI to show purple (cancelling state), locks remain held
                _trainRouteNotificationService.NotifyRouteCancelling(_currentStation,
                    trainRouteCommand with { FromSignal = fromSignal },
                    RouteMessages.Cancelling(isShuntingRoute, fromSignal, trainRouteCommand.ToSignal), role);
                LogRouteCancelling(fromSignal, trainRouteCommand.ToSignal, delaySeconds);

                // Schedule Phase 2 after delay
                var cts = new CancellationTokenSource();
                _pendingReleases[trainRouteCommand.ToSignal] = cts;
                // Disposed by the release task below, once it has run or been cancelled.
#pragma warning disable CA2000
                var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
#pragma warning restore CA2000
                var linkedToken = linkedSource.Token;
                var capturedFromSignal = fromSignal;
                var capturedCommand = trainRouteCommand;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), linkedToken);
                        await ExecutePhase2Release(capturedCommand, capturedFromSignal, isShuntingRoute, role, cancellationToken);
                    }
                    catch (OperationCanceledException) { }
                    finally { linkedSource.Dispose(); }
                }, CancellationToken.None);
            }
            else
            {
                // No delay: immediate Phase 2 (original behaviour, always used for shunting routes)
                await ExecutePhase2Release(trainRouteCommand, fromSignal, isShuntingRoute, role, cancellationToken);
            }
            return true;
        }
        return false;
    }

    private bool IsSignalNeededByOtherRoute(int signalNumber, int excludeRouteToSignal) =>
        CurrentLockings.CurrentRoutes.Any(route =>
            route.ToSignal != excludeRouteToSignal &&
            (route.FromSignal == signalNumber || route.IntermediateSignals.Contains(signalNumber)));

    private async Task ExecutePhase2Release(TrainRouteCommand trainRouteCommand, int fromSignal, bool isShuntingRoute, OperatorRole role, CancellationToken cancellationToken)
    {
        var releasedPoints = CurrentLockings.ClearLocks(trainRouteCommand);
        foreach (var pointCommand in releasedPoints)
        {
            if (pointCommand.AlsoUnlock)
                await _yardController.SendPointUnlockCommandsAsync(pointCommand, cancellationToken);
        }
        _pendingReleases.Remove(trainRouteCommand.ToSignal);
        _trainRouteNotificationService.NotifyRouteCleared(_currentStation, trainRouteCommand with { FromSignal = fromSignal },
            RouteMessages.Cleared(isShuntingRoute, fromSignal, trainRouteCommand.ToSignal), role);
        LogRouteLocksReleased(trainRouteCommand);
        _queueDrainPending = true;
    }

    private async Task TryExecuteQueuedRoutes(CancellationToken cancellationToken)
    {
        for (int i = 0; i < _queuedRoutes.Count; i++)
        {
            var queued = _queuedRoutes[i];
            if (!queued.StationName.Equals(_currentStation, StringComparison.OrdinalIgnoreCase))
                continue;
            if (CurrentLockings.CanReserveLocksFor(queued.Route))
            {
                _queuedRoutes.RemoveAt(i);
                i--;
                _trainRouteNotificationService.NotifyQueuedRouteCancelled(
                    _currentStation, queued.Route, "", queued.Role);
                await TrySetTrainRoute(queued.Route, queued.Role, cancellationToken, queued.TrainNumber);
            }
        }
    }

    /// <summary>
    /// Handles <c>A{area}+</c> (release the area to a shunter) and <c>A{area}-</c> (take it back).
    /// Releasing sets and locks the area's flank protection so nothing can run into it while the
    /// shunter works; locking it again releases that protection. Only the dispatcher may release
    /// an area — the shunter may hand one back, which is what the shunter view's button sends.
    /// </summary>
    private async Task HandleShuntingAreaCommand(string command, OperatorRole role, CancellationToken cancellationToken)
    {
        var areaNumber = command[1..^1].ToIntOrZero;
        var release = command[^1].ToPointPosition == PointPosition.Straight;

        if (!_shuntingAreas.TryGetValue(areaNumber, out var area))
        {
            LogNoSuchShuntingArea(areaNumber);
            _pointNotificationService.NotifyPointRejected(_currentStation, areaNumber, string.Format(CultureInfo.CurrentCulture, Messages.ShuntingAreaNotFound, areaNumber), role);
            return;
        }

        if (release && role == OperatorRole.Shunter)
        {
            LogShuntingAreaReleaseIsDispatchers(areaNumber);
            _pointNotificationService.NotifyPointRejected(_currentStation, areaNumber, string.Format(CultureInfo.CurrentCulture, Messages.ShuntingAreaReleaseIsDispatchers, TranslatedAreaName(area)), role);
            return;
        }

        if (release)
        {
            if (_shuntingAreaService.GetState(_currentStation, areaNumber) == ShuntingAreaState.Released) return;

            // The interlocking cannot hand over a point it is still holding for a train route.
            var claimed = area.Points.Select(p => p.Number);
            if (CurrentLockings.IsAnyLockedByRoute(claimed))
            {
                LogShuntingAreaLockedByRoute(areaNumber);
                _pointNotificationService.NotifyPointRejected(_currentStation, areaNumber, string.Format(CultureInfo.CurrentCulture, Messages.ShuntingAreaLockedByRoute, TranslatedAreaName(area)), role);
                return;
            }

            // A point another released area hands to the shunter is not pinned by this one.
            var ownedByReleased = ReleasedShunterPointNumbers();
            ownedByReleased.UnionWith(area.ShunterPointNumbers);
            var flankProtection = area.FlankProtection.Where(flank => !ownedByReleased.Contains(flank.Number)).ToList();
            foreach (var flank in flankProtection)
                await _yardController.SendPointSetCommandsAsync(flank, cancellationToken);
            CurrentLockings.LockForShuntingArea(areaNumber, flankProtection);
            _shuntingAreaService.SetState(_currentStation, areaNumber, ShuntingAreaState.Released);
            CurrentLockings.SetShunterOwnedPoints(ReleasedShunterPointNumbers());

            foreach (var flank in flankProtection)
                _pointNotificationService.NotifyPointSet(_currentStation, flank,
                    string.Format(CultureInfo.CurrentCulture, Messages.PointSet, flank.Number, Messages.LocalizedPosition(flank.Position)), role);
            LogShuntingAreaReleased(areaNumber);
        }
        else
        {
            if (_shuntingAreaService.GetState(_currentStation, areaNumber) == ShuntingAreaState.Locked) return;

            // The area returns to the interlocking in a defined state, so every route touching it
            // goes first — including one the shunter ran across two areas at once.
            var areaPoints = new HashSet<int>(area.ShunterPointNumbers);
            var standingRoutes = CurrentLockings.CurrentRoutes
                .Where(r => r.PointCommands.Any(pointCommand => areaPoints.Contains(pointCommand.Number)))
                .ToList();
            foreach (var route in standingRoutes)
                await TrySetTrainRoute(new TrainRouteCommand(0, route.ToSignal, TrainRouteState.Clear, []), OperatorRole.Dispatcher, cancellationToken);

            foreach (var flank in CurrentLockings.UnlockShuntingArea(areaNumber))
            {
                if (flank.AlsoUnlock) await _yardController.SendPointUnlockCommandsAsync(flank, cancellationToken);
            }
            _shuntingAreaService.SetState(_currentStation, areaNumber, ShuntingAreaState.Locked);
            CurrentLockings.SetShunterOwnedPoints(ReleasedShunterPointNumbers());
            LogShuntingAreaLocked(areaNumber);
        }
    }

    /// <summary>
    /// Handles <c>S{signal}+</c> (give the signal go) and <c>S{signal}-</c> (put it to stop).
    /// It is the same thing a dispatcher does by shift-clicking a signal on the diagram, and it
    /// asks the same of the interlocking: nothing. The signal is set as told, so the command can
    /// answer for a signal no route covers.
    /// </summary>
    private async Task HandleSignalCommand(string command, OperatorRole role, CancellationToken cancellationToken)
    {
        var signalNumber = command[1..^1].ToIntOrZero;
        var state = command[^1].ToPointPosition == PointPosition.Straight ? SignalState.Go : SignalState.Stop;

        if (!_signalsByNumber.TryGetValue(signalNumber, out var signal))
        {
            LogNoSuchSignalNumber(signalNumber);
            _pointNotificationService.NotifyPointRejected(_currentStation, signalNumber, string.Format(CultureInfo.CurrentCulture, Messages.SignalNotFound, signalNumber), role);
            return;
        }

        // A buffer stop is the end of the track, not a signal: §6.2.9 grants it no proceed colour.
        if (state == SignalState.Go && signal.Type.IsBufferStop)
        {
            LogSignalCannotShowProceed(signalNumber);
            _pointNotificationService.NotifyPointRejected(_currentStation, signalNumber, string.Format(CultureInfo.CurrentCulture, Messages.SignalCannotShowProceed, signalNumber), role);
            return;
        }

        await _yardController.SendSignalCommandAsync(
            new SignalCommand(signalNumber, signal.Address, state) { FeedbackAddress = signal.FeedbackAddress }, cancellationToken);
        LogSignalSet(signalNumber, state);
    }

    /// <summary>
    /// Handles <c>T{section}+</c> (hold the detection stretch occupied by hand) and
    /// <c>T{section}-</c> (free one that is being held).
    /// <para>
    /// Holding by hand is what puts a stretch back after a restart, since holds live only as long
    /// as the application does, and it is the only way to say "there is stock here" about track a
    /// detector reports quiet. Both forms need a stretch with a detector behind it: a hold answers
    /// a detector, and where there is none there is nothing to answer and nothing to take back.
    /// </para>
    /// </summary>
    private void HandleOccupancyHoldCommand(string command, OperatorRole role)
    {
        var sectionNumber = command[1..^1].ToIntOrZero;
        var hold = command[^1].ToPointPosition == PointPosition.Straight;

        var section = _occupancySections.FirstOrDefault(s => s.Number == sectionNumber);
        if (section is null)
        {
            LogNoSuchOccupancySection(sectionNumber);
            _pointNotificationService.NotifyPointRejected(_currentStation, sectionNumber, string.Format(CultureInfo.CurrentCulture, Messages.OccupancySectionNotFound, sectionNumber), role);
            return;
        }

        if (!section.HasAddress)
        {
            LogOccupancySectionHasNoDetector(sectionNumber);
            _pointNotificationService.NotifyPointRejected(_currentStation, sectionNumber, string.Format(CultureInfo.CurrentCulture, Messages.OccupancySectionHasNoDetector, sectionNumber), role);
            return;
        }

        if (hold)
        {
            _occupancyHoldService.Hold(_currentStation, sectionNumber);
            _pointNotificationService.NotifyPointSet(_currentStation, PointCommand.Create(sectionNumber, PointPosition.Straight, []), string.Format(CultureInfo.CurrentCulture, Messages.OccupancySectionHeld, sectionNumber), role);
            return;
        }

        if (_occupancyHoldService.Release(_currentStation, sectionNumber))
            _pointNotificationService.NotifyPointSet(_currentStation, PointCommand.Create(sectionNumber, PointPosition.Diverging, []), string.Format(CultureInfo.CurrentCulture, Messages.OccupancySectionReleased, sectionNumber), role);
        else
            _pointNotificationService.NotifyPointRejected(_currentStation, sectionNumber, string.Format(CultureInfo.CurrentCulture, Messages.OccupancySectionNotHeld, sectionNumber), role);
    }

    /// <summary>
    /// Handles <c>T-</c>: frees every held stretch of the station at once. A hold outlives the
    /// train that caused it, so the board is cleared at the start of an operating session rather
    /// than one stretch at a time.
    /// </summary>
    private void ReleaseAllOccupancyHolds(OperatorRole role)
    {
        var released = _occupancyHoldService.ReleaseAll(_currentStation);
        LogOccupancyHoldsReleased(released);
        if (released > 0)
            _pointNotificationService.NotifyPointSet(_currentStation, PointCommand.Create(0, PointPosition.Diverging, []), string.Format(CultureInfo.CurrentCulture, Messages.OccupancySectionsReleased, released), role);
        else
            _pointNotificationService.NotifyPointRejected(_currentStation, 0, Messages.NoOccupancySectionsHeld, role);
    }

    private string TranslatedAreaName(ShuntingArea area)
    {
        var translator = _yardDataService.GetStationData(_currentStation)?.LabelTranslator;
        var name = translator?.Translate(area.Name, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? area.Name;
        return $"{area.Number} {name}";
    }

    private int GetLockReleaseDelaySeconds()
    {
        if (_hostEnvironment.IsDevelopment()) return 5;
        var stationData = _yardDataService.GetStationData(_currentStation);
        return stationData?.LockReleaseDelaySeconds ?? _yardDataService.LockReleaseDelaySeconds;
    }

    /// <summary>
    /// The stretch in front of a signal, resolved through the route that is running over it. Where
    /// the track branches several stretches end at the same signal, and only the route can say
    /// which way through the points the movement came.
    /// </summary>
    private int? RouteSection(TrainRouteCommand route, int signalNumber)
    {
        var links = _topology.RouteLinksInOrder(route).ToHashSet();
        return _topology.SectionLeadingUpTo(
            signalNumber, _occupancySections, _occupancySectionTracks, links.Count > 0 ? links : null,
            _topology.RouteDrivesRight(route));
    }

    /// <summary>
    /// The stretch in front of a signal when no particular route is in hand. Every active route is
    /// asked first; without one the shortest approach stretch is taken.
    /// </summary>
    private int? ResolveSectionLeadingUpTo(int signalNumber)
    {
        foreach (var route in CurrentLockings.CurrentRoutes)
        {
            var links = _topology.RouteLinksInOrder(route).ToHashSet();
            if (links.Count == 0) continue;
            if (_topology.SectionLeadingUpTo(signalNumber, _occupancySections, _occupancySectionTracks, links, _topology.RouteDrivesRight(route)) is { } onRoute
                && _occupancySectionTracks.TryGetValue(onRoute, out var track)
                && track.Links.Count > 0 && track.Links.All(links.Contains))
                return onRoute;
        }
        return _topology.SectionLeadingUpTo(signalNumber, _occupancySections, _occupancySectionTracks);
    }

    /// <summary>
    /// <c>[signal]=</c> — moves a train up to the stretch in front of a signal. The signal has to
    /// lie on the route the train is running and ahead of where it stands: a train number only
    /// ever moves the way the train is going. Reaching the route's final signal is the arrival,
    /// so the route goes with it.
    /// </summary>
    private async Task MoveTrainNumberToSignal(int signalNumber, CancellationToken cancellationToken)
    {
        var trains = _trainNumberService.GetTrainNumbers(_currentStation);
        if (trains.Count == 0)
        {
            LogNoTrainNumberToMove(signalNumber);
            return;
        }

        var signalIsOnARoute = false;
        TrainRouteCommand? arrivedRoute = null;
        foreach (var route in CurrentLockings.CurrentRoutes)
        {
            var orderedLinks = _topology.RouteLinksInOrder(route);
            if (orderedLinks.Count == 0) continue;
            var linkSet = orderedLinks.ToHashSet();

            if (_topology.SectionLeadingUpTo(signalNumber, _occupancySections, _occupancySectionTracks, linkSet, _topology.RouteDrivesRight(route)) is not { } target) continue;
            if (_topology.SectionPositionOnRoute(target, orderedLinks, _occupancySectionTracks) < 0) continue;
            signalIsOnARoute = true;

            if (FindTrainSectionOnRoute(route, orderedLinks, linkSet, trains) is not { } current) continue;
            if (!_topology.IsSectionAheadOnRoute(current, target, orderedLinks, _occupancySectionTracks))
            {
                LogSignalNotAheadOfTrain(signalNumber, current);
                continue;
            }

            _trainNumberService.MoveTrainNumber(_currentStation, current, target);
            LogTrainNumberMoved(current, target);
            // Up to the route's own final signal is the arrival; an intermediate one is not.
            if (signalNumber == route.ToSignal) arrivedRoute = route;
            break;
        }

        if (arrivedRoute is not null) await ClearArrivedRoute(arrivedRoute, cancellationToken);
        else if (!signalIsOnARoute) LogNoRouteEndsAtSignal(signalNumber);
    }

    /// <summary>
    /// Tears down the route a train has just run to the end of. The movement it was set for is
    /// over the moment the train is reported there, so it is cleared at once rather than left
    /// standing until the dispatcher clears it by hand — the same teardown <c>[signal]/</c> does,
    /// but without the lock release delay, since the train is known to be standing clear of the
    /// points. A route already counting down its lock release is left alone.
    /// </summary>
    private async Task ClearArrivedRoute(TrainRouteCommand route, CancellationToken cancellationToken)
    {
        if (_pendingReleases.ContainsKey(route.ToSignal)) return;
        _ = await TrySetTrainRoute(
            new TrainRouteCommand(0, route.ToSignal, TrainRouteState.Clear, []), OperatorRole.Dispatcher, cancellationToken,
            trainHasArrived: true);
    }

    /// <summary>
    /// Where the train running a route stands: on the approach stretch while it still waits at the
    /// from signal, otherwise on the stretch it has reached along the route. The one furthest
    /// along wins, so a number left behind by an earlier movement cannot be picked up instead.
    /// </summary>
    private int? FindTrainSectionOnRoute(
        TrainRouteCommand route,
        IReadOnlyList<TrackLink> orderedLinks,
        IReadOnlySet<TrackLink> routeLinks,
        IReadOnlyDictionary<int, string> trains)
    {
        var onRoute = trains.Keys
            .Where(section => _topology.SectionPositionOnRoute(section, orderedLinks, _occupancySectionTracks) >= 0)
            .OrderByDescending(section => _topology.SectionPositionOnRoute(section, orderedLinks, _occupancySectionTracks))
            .Select(section => (int?)section)
            .FirstOrDefault();
        if (onRoute is not null) return onRoute;

        var approach = _topology.SectionLeadingUpTo(
            route.FromSignal, _occupancySections, _occupancySectionTracks, routeLinks, _topology.RouteDrivesRight(route));
        return approach is not null && trains.ContainsKey(approach.Value) ? approach : null;
    }

    /// <summary>
    /// <c>=[trainNumber]</c> — confirms the train has moved on. It advances to the end of the route
    /// it is running, which is the arrival and ends that route; with no route left it steps out
    /// onto the track leading off the layout, which is where a departed train stands, and ends the
    /// outbound route it left on; and one more press is the next station reporting it arrived,
    /// which takes it off the diagram.
    /// </summary>
    private async Task AdvanceTrainNumber(string trainNumber, CancellationToken cancellationToken)
    {
        if (_trainNumberService.FindSectionByTrainNumber(_currentStation, trainNumber) is not { } currentSection)
        {
            LogTrainNumberNotFound(trainNumber);
            return;
        }

        TrainRouteCommand? arrivedRoute = null;
        TrainRouteCommand? waitingRoute = null;
        foreach (var route in CurrentLockings.CurrentRoutes)
        {
            var links = _topology.RouteLinksInOrder(route).ToHashSet();
            if (links.Count == 0) continue;
            var drivesRight = _topology.RouteDrivesRight(route);
            if (_topology.SectionLeadingUpTo(route.FromSignal, _occupancySections, _occupancySectionTracks, links, drivesRight) != currentSection) continue;
            // The train stands at this route's from signal, so this is the movement it is running.
            waitingRoute ??= route;
            if (_topology.SectionLeadingUpTo(route.ToSignal, _occupancySections, _occupancySectionTracks, links, drivesRight) is not { } destination) continue;
            if (destination == currentSection) continue;

            _trainNumberService.MoveTrainNumber(_currentStation, currentSection, destination);
            LogTrainNumberAdvanced(trainNumber, currentSection, destination);
            arrivedRoute = route;
            break;
        }

        if (arrivedRoute is not null)
        {
            await ClearArrivedRoute(arrivedRoute, cancellationToken);
            return;
        }

        // A train waiting at the from signal of a route it has been given is running that route,
        // and has not left the station whatever the track around it looks like: the stretch it
        // stands on is an edge stretch at every inbound signal. So where that route names no
        // stretch to advance to, the train stays where it stands and the refusal is logged. It is
        // never taken off the diagram instead — a station file whose [OccupancySections] no longer
        // match its signals must not read as a train that has gone.
        if (waitingRoute is not null)
        {
            LogNoDestinationStretchOnRoute(trainNumber, waitingRoute.FromSignal, waitingRoute.ToSignal);
            return;
        }

        // Departed: the train runs out past the last signal onto the stretch leading off the
        // layout. The route it left on is asked for that stretch, since it names the signal the
        // train passed; only without one is the signal looked up from where the train stands.
        var outboundRoute = CurrentLockings.CurrentRoutes
            .FirstOrDefault(route => RouteSection(route, route.ToSignal) == currentSection);
        var departureSection =
            (outboundRoute is null ? null : _topology.SectionBeyond(outboundRoute.ToSignal, _occupancySections, _occupancySectionTracks))
            ?? _topology.DepartureSectionFrom(currentSection, _occupancySections, _occupancySectionTracks);

        if (departureSection is { } departure && departure != currentSection)
        {
            await DepartTrainNumber(trainNumber, currentSection, departure, outboundRoute, cancellationToken);
            return;
        }

        _trainNumberService.RemoveTrainNumber(_currentStation, currentSection);
        LogTrainNumberRemoved(trainNumber, currentSection);
    }

    /// <summary>
    /// The edge forms of the train-number commands. A dot beside the signal is the edge of the
    /// layout, and the side it sits on says which side of the signal the edge is on:
    /// <c>{signal}.</c> is the track running out past it, <c>.{signal}</c> the track running in
    /// to it. Whether such a stretch is there at all is the topology's answer, so a train can
    /// never be sent out through an inbound signal nor accepted through an outbound one.
    /// </summary>
    private async Task HandleEdgeTrainNumberCommand(int signalNumber, bool beyond, string? trainNumber, CancellationToken cancellationToken)
    {
        if (signalNumber <= 0 || !_signalsByNumber.ContainsKey(signalNumber))
        {
            LogUnknownSignalForTrainNumber(signalNumber);
            return;
        }

        var edgeSection = beyond
            ? _topology.SectionBeyond(signalNumber, _occupancySections, _occupancySectionTracks)
            : _topology.SectionFromEdge(signalNumber, _occupancySections, _occupancySectionTracks);

        if (edgeSection is not { } section)
        {
            LogNoEdgeSectionAtSignal(signalNumber);
        }
        else if (!string.IsNullOrEmpty(trainNumber))
        {
            _trainNumberService.AssignTrainNumber(_currentStation, section, trainNumber);
            LogTrainNumberAssigned(trainNumber, section);
        }
        else if (beyond)
        {
            await DepartTrainNumberAtSignal(signalNumber, section, cancellationToken);
        }
        else
        {
            // .{signal}= would move a train back onto the track it came in on, which is the one
            // direction a train number never goes.
            LogEdgeCommandNeedsTrainNumber(signalNumber);
        }
    }

    /// <summary>
    /// <c>{signal}.=</c> — runs the train standing at a signal out past it, onto the stretch that
    /// leads off the layout. The train has to be standing in front of that signal: the command
    /// says where the movement goes, so a number further back is not picked up instead.
    /// </summary>
    private async Task DepartTrainNumberAtSignal(int signalNumber, int departureSection, CancellationToken cancellationToken)
    {
        var outboundRoute = CurrentLockings.CurrentRoutes.FirstOrDefault(route => route.ToSignal == signalNumber);
        var standingOn = outboundRoute is null
            ? ResolveSectionLeadingUpTo(signalNumber)
            : RouteSection(outboundRoute, signalNumber);

        if (standingOn is not { } current
            || _trainNumberService.GetTrainNumber(_currentStation, current) is not { Length: > 0 } trainNumber)
        {
            LogNoTrainNumberToMove(signalNumber);
            return;
        }

        await DepartTrainNumber(trainNumber, current, departureSection, outboundRoute, cancellationToken);
    }

    /// <summary>
    /// Moves a train number out onto the stretch leading off the layout. The route the train left
    /// on was set for that one movement, so reporting it gone ends the route, as an arrival does.
    /// </summary>
    private async Task DepartTrainNumber(string trainNumber, int fromSection, int toSection, TrainRouteCommand? outboundRoute, CancellationToken cancellationToken)
    {
        _trainNumberService.MoveTrainNumber(_currentStation, fromSection, toSection);
        LogTrainNumberDeparted(trainNumber, toSection);
        if (outboundRoute is not null) await ClearArrivedRoute(outboundRoute, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting Numeric Keypad Controller Inputs")]
    private partial void LogStarting();

    [LoggerMessage(Level = LogLevel.Information, Message = "Data changed for station '{Station}', reloading configuration...")]
    private partial void LogDataChanged(string station);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Data has validation errors: {ErrorCount} load errors, {InvalidRoutes} invalid routes")]
    private partial void LogDataHasValidationErrors(int errorCount, int invalidRoutes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping Numeric Keypad Controller Inputs")]
    private partial void LogStopping();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded configuration for station '{Station}': {Points} points, {Routes} routes")]
    private partial void LogConfigurationLoaded(string station, int points, int routes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Start reading input keys.")]
    private partial void LogStartReadingKeys();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Station changed from '{OldStation}' to '{NewStation}' mid-command, clearing input buffer")]
    private partial void LogStationChangedMidCommand(string oldStation, string newStation);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Role changed from '{OldRole}' to '{NewRole}' mid-command, clearing input buffer")]
    private partial void LogRoleChangedMidCommand(OperatorRole oldRole, OperatorRole newRole);

    [LoggerMessage(Level = LogLevel.Information, Message = "Key received: ConsoleKey={Key} KeyChar=0x{KeyChar:X2} ('{KeyCharText}')")]
    private partial void LogKeyReceived(ConsoleKey key, int keyChar, char keyCharText);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unrecognized key: ConsoleKey={Key} KeyChar=0x{KeyChar:X2} ('{KeyCharText}')")]
    private partial void LogUnrecognizedKey(ConsoleKey key, int keyChar, char keyCharText);

    [LoggerMessage(Level = LogLevel.Information, Message = "All routes cancelling, locks held for {Delay} seconds")]
    private partial void LogAllRoutesCancelling(int delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "All signals set to stop")]
    private partial void LogAllSignalsSetToStop();

    [LoggerMessage(Level = LogLevel.Information, Message = "All points set to straight")]
    private partial void LogAllPointsSetToStraight();

    [LoggerMessage(Level = LogLevel.Warning, Message = "No turntable track with this number: {PointNumber}")]
    private partial void LogNoSuchTurntableTrack(int pointNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No such point number: {PointNumber}")]
    private partial void LogNoSuchPointNumber(int pointNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Point {PointNumber} not permitted for {Role}")]
    private partial void LogPointNotPermitted(int pointNumber, OperatorRole role);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid point command: {PointCommand}")]
    private partial void LogInvalidPointCommand(string pointCommand);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Point command {PointCommand} is not permitted, point is locked.")]
    private partial void LogPointCommandLocked(PointCommand pointCommand);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slave cascade conflict for {PointCommand}: {Message}")]
    private partial void LogSlaveCascadeConflict(PointCommand pointCommand, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cascaded slave {Slave} is released to a shunter, rejecting master {Master}")]
    private partial void LogCascadedSlaveReleasedToShunter(PointCommand slave, PointCommand master);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cascaded slave {Slave} is locked, rejecting master {Master}")]
    private partial void LogCascadedSlaveLocked(PointCommand slave, PointCommand master);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Point command {PointCommand} refused by a crossing: {Reason}")]
    private partial void LogPointBlockedByCrossing(PointCommand pointCommand, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Train route command entered: {TrainRouteCommand}")]
    private partial void LogTrainRouteCommandEntered(string trainRouteCommand);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Part of train route not found between signal {FromSignal} and signal {ToSignal}")]
    private partial void LogPartOfRouteNotFound(int fromSignal, int toSignal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Train route command not executed due to not complete: {TrainRouteCommand}")]
    private partial void LogIncompleteRouteCommand(string trainRouteCommand);

    [LoggerMessage(Level = LogLevel.Information, Message = "Train number moved from detection stretch {FromSection} to stretch {ToSection}")]
    private partial void LogTrainNumberMoved(int fromSection, int toSection);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No active route passes signal {ToSignal}, ignoring train number move")]
    private partial void LogNoRouteEndsAtSignal(int toSignal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No train number to move up to signal {ToSignal}")]
    private partial void LogNoTrainNumberToMove(int toSignal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Signal {ToSignal} is not ahead of the train on detection stretch {CurrentSection}, ignoring train number move")]
    private partial void LogSignalNotAheadOfTrain(int toSignal, int currentSection);

    [LoggerMessage(Level = LogLevel.Information, Message = "Train number {TrainNumber} assigned to detection stretch {SectionNumber}")]
    private partial void LogTrainNumberAssigned(string trainNumber, int sectionNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot assign train number to signal {SignalNumber}: unknown, or no detection stretch leads up to it")]
    private partial void LogUnknownSignalForTrainNumber(int signalNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unrecognized command: '{Command}' (hex: {CommandHex})")]
    private partial void LogUnrecognizedCommand(string command, string commandHex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command cleared")]
    private partial void LogCommandCleared();

    [LoggerMessage(Level = LogLevel.Warning, Message = "No train route found for from signal {FromSignalNumber} to signal {ToSignalNumber}")]
    private partial void LogNoTrainRouteFound(int fromSignalNumber, int toSignalNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Signal {ShuntingSignalNumber} is a shunting signal, train route from signal {FromSignalNumber} to signal {ToSignalNumber} is set as a shunting route")]
    private partial void LogShuntingSignalDemotesRoute(int shuntingSignalNumber, int fromSignalNumber, int toSignalNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Command rejected: not permitted for the shunter")]
    private partial void LogCommandNotPermittedForShunter();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Train route {Route} not permitted for {Role}")]
    private partial void LogRouteNotPermitted(TrainRouteCommand route, OperatorRole role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Locks taken for train route command {TrainRouteCommand}")]
    private partial void LogRouteLocksTaken(TrainRouteCommand trainRouteCommand);

    [LoggerMessage(Level = LogLevel.Information, Message = "Train route {TrainRouteCommand} queued due to conflict")]
    private partial void LogRouteQueued(TrainRouteCommand trainRouteCommand);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued route to signal {ToSignal} cancelled")]
    private partial void LogQueuedRouteCancelled(int toSignal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No active or queued route ends at signal {ToSignal}, ignoring teardown command")]
    private partial void LogNoRouteToTearDown(int toSignal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Route to signal {ToSignal} already cancelling, ignoring duplicate")]
    private partial void LogRouteAlreadyCancelling(int toSignal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route {From}-{To} cancelling, locks held for {Delay} seconds")]
    private partial void LogRouteCancelling(int from, int to, int delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Locks released for train route command {TrainRouteCommand}")]
    private partial void LogRouteLocksReleased(TrainRouteCommand trainRouteCommand);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No such signal number: {SignalNumber}")]
    private partial void LogNoSuchSignalNumber(int signalNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Signal {SignalNumber} is a buffer stop and cannot show proceed")]
    private partial void LogSignalCannotShowProceed(int signalNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Signal {SignalNumber} set to {State}")]
    private partial void LogSignalSet(int signalNumber, SignalState state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No such occupancy section: {Section}")]
    private partial void LogNoSuchOccupancySection(int section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Occupancy section {Section} has no feedback address, so there is no detector report to hold")]
    private partial void LogOccupancySectionHasNoDetector(int section);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Released} held occupancy sections were freed")]
    private partial void LogOccupancyHoldsReleased(int released);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No such shunting area: {Area}")]
    private partial void LogNoSuchShuntingArea(int area);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shunting area {Area} can only be released by the dispatcher")]
    private partial void LogShuntingAreaReleaseIsDispatchers(int area);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shunting area {Area} has points locked by an active route")]
    private partial void LogShuntingAreaLockedByRoute(int area);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shunting area {Area} released")]
    private partial void LogShuntingAreaReleased(int area);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shunting area {Area} locked")]
    private partial void LogShuntingAreaLocked(int area);

    [LoggerMessage(Level = LogLevel.Information, Message = "Train number {TrainNumber} advanced from detection stretch {FromSection} to stretch {ToSection}")]
    private partial void LogTrainNumberAdvanced(string trainNumber, int fromSection, int toSection);

    [LoggerMessage(Level = LogLevel.Information, Message = "Train number {TrainNumber} departed onto detection stretch {ToSection}")]
    private partial void LogTrainNumberDeparted(string trainNumber, int toSection);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Train number {TrainNumber} not found on any detection stretch, ignoring advance command")]
    private partial void LogTrainNumberNotFound(string trainNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Train number {TrainNumber} removed from detection stretch {Section} (arrived at next station)")]
    private partial void LogTrainNumberRemoved(string trainNumber, int section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Train number {TrainNumber} is running route {FromSignal}-{ToSignal}, whose destination has no detection stretch; the train is left standing where it is")]
    private partial void LogNoDestinationStretchOnRoute(string trainNumber, int fromSignal, int toSignal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No track leads off the layout on that side of signal {SignalNumber}, ignoring train number command")]
    private partial void LogNoEdgeSectionAtSignal(int signalNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Signal {SignalNumber} with a leading dot needs a train number: a train is only ever moved out to the edge, never back in from it")]
    private partial void LogEdgeCommandNeedsTrainNumber(int signalNumber);

    #region Disposable Support
    private bool _disposed;

    public sealed override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var cts in _pendingReleases.Values) cts.Cancel();
        _pendingReleases.Clear();
        _queuedRoutes.Clear();
        _cancellationTokenSource.Dispose();
        base.Dispose();
    }

    #endregion
}
