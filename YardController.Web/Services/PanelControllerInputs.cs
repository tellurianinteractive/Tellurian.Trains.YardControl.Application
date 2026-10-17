using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using Tellurian.Trains.YardController.Panel.Rendering;
using YardController.Web.Resources;
using YardController.Web.Components;

namespace YardController.Web.Services;

/// <summary>
/// Drives the operator's button panel: key presses in, commands out, key faces back.
/// <para>
/// Everything a command does is done by the existing pipeline. A completed entry is reduced to
/// the characters the keypad grammar already accepts and put through
/// <see cref="IBufferedKeyReader"/>, so all the locking, validation, signalling and queueing in
/// <see cref="NumericKeypadControllerInputs"/> runs unchanged (§11.3). Nothing about a route is
/// decided here.
/// </para>
/// <para>
/// A panel that is not plugged in costs nothing: the null panel reports no keys and takes no
/// pictures, so this service runs and does nothing at all (§4).
/// </para>
/// </summary>
public sealed partial class PanelControllerInputs : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IButtonPanel _panel;
    private readonly IBufferedKeyReader _keyReader;
    private readonly IYardDataService _yardDataService;
    private readonly IEntryStateService _entryStateService;
    private readonly PanelKeyFaces _faces;
    private readonly IPointPositionService _pointPositionService;
    private readonly ISignalStateService _signalStateService;
    private readonly IShuntingAreaService _shuntingAreaService;
    private readonly TrainRouteLockingsManager _lockingsManager;
    private readonly IOperatorLanguageService _languageService;
    private readonly PanelEntry _entry = new();
    private readonly Lock _gate = new();
    private readonly string?[] _painted;
    private string _station = "";
    private ButtonLayout _layout = ButtonLayout.Default;
    private string? _rejection;

    public PanelControllerInputs(
        ILogger<PanelControllerInputs> logger,
        IButtonPanel panel,
        IBufferedKeyReader keyReader,
        IYardDataService yardDataService,
        IEntryStateService entryStateService,
        PanelKeyFaces faces,
        IPointPositionService pointPositionService,
        ISignalStateService signalStateService,
        IShuntingAreaService shuntingAreaService,
        TrainRouteLockingsManager lockingsManager,
        IOperatorLanguageService languageService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _keyReader = keyReader ?? throw new ArgumentNullException(nameof(keyReader));
        _yardDataService = yardDataService ?? throw new ArgumentNullException(nameof(yardDataService));
        _entryStateService = entryStateService ?? throw new ArgumentNullException(nameof(entryStateService));
        _faces = faces ?? throw new ArgumentNullException(nameof(faces));
        _pointPositionService = pointPositionService ?? throw new ArgumentNullException(nameof(pointPositionService));
        _signalStateService = signalStateService ?? throw new ArgumentNullException(nameof(signalStateService));
        _shuntingAreaService = shuntingAreaService ?? throw new ArgumentNullException(nameof(shuntingAreaService));
        _lockingsManager = lockingsManager ?? throw new ArgumentNullException(nameof(lockingsManager));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _painted = new string?[_panel.Columns * _panel.Rows];
    }

    /// <summary>
    /// The language the keys are lettered in. A panel has no request to take a culture from, and
    /// the thread a repaint happens on is whichever one raised the event, so this is asked of the
    /// service holding the operator's choice rather than read from the ambient culture.
    /// </summary>
    private string Language => _languageService.Language;

    /// <summary>
    /// Subscribes and paints. This is <c>StartAsync</c> rather than <c>ExecuteAsync</c> because a
    /// host does not guarantee that <c>ExecuteAsync</c> has got anywhere by the time the service is
    /// considered started — and a key pressed in that gap would go to a panel nothing was listening
    /// to. Getting ready is what starting means.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        LogStarting(Language);
        _panel.KeyStateChanged += OnKeyStateChanged;
        _panel.ConnectionChanged += OnConnectionChanged;
        _yardDataService.DataChanged += OnDataChanged;
        _languageService.LanguageChanged += OnLanguageChanged;

        LoadStation(_yardDataService.CurrentStationName);
        Repaint(force: true);

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Nothing to run: everything past start-up is event-driven, so the service only has to stay
    /// alive to hold its subscriptions.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _panel.KeyStateChanged -= OnKeyStateChanged;
        _panel.ConnectionChanged -= OnConnectionChanged;
        _yardDataService.DataChanged -= OnDataChanged;
        _languageService.LanguageChanged -= OnLanguageChanged;
        _panel.Clear();
        LogStopping();
        return base.StopAsync(cancellationToken);
    }

    private void OnDataChanged(StationDataChange args)
    {
        LoadStation(args.StationName);
        Repaint(force: true);
    }

    /// <summary>
    /// Every key carries a word, so a change of language changes all thirty-two of them at once.
    /// </summary>
    private void OnLanguageChanged(string language)
    {
        LogLanguageChanged(language);
        Repaint(force: true);
    }

    private void OnConnectionChanged(bool connected)
    {
        if (!connected) return;
        // A panel plugged in mid-session comes up blank; everything it should show is here.
        lock (_gate) Array.Clear(_painted);
        Repaint(force: true);
    }

    /// <summary>
    /// Reads the station's numbers so digits can be matched against them, and its button layout so
    /// the keys carry what the station file says they carry.
    /// </summary>
    private void LoadStation(string stationName)
    {
        lock (_gate)
        {
            _station = string.IsNullOrEmpty(stationName) ? _yardDataService.CurrentStationName : stationName;
            _layout = _yardDataService.Buttons;
            _entry.UseNumbers(new PanelNumbers(
                // A signal's name is its number, which is why entry can narrow digits against it.
                Signals: _yardDataService.Signals
                    .Select(s => int.TryParse(s.Name, out var number) ? number : 0)
                    .Where(n => n > 0).ToHashSet(),
                Points: _yardDataService.Points.Where(p => !p.IsAddressOnly).Select(p => p.Number).ToHashSet(),
                ShuntingAreas: _yardDataService.ShuntingAreas.Select(a => a.Number).ToHashSet()));
            LogStationLoaded(_station, _yardDataService.Signals.Count, _yardDataService.Points.Count);
        }
        PublishEntry();
    }

    private void OnKeyStateChanged(int keyIndex, bool isDown)
    {
        // Only the press. A panel key is a button, and the yard has no use for how long it is held.
        if (!isDown) return;

        var column = keyIndex % _panel.Columns;
        var row = keyIndex / _panel.Columns;
        if (row >= ButtonLayout.Rows) return;

        PanelEntryResult result;
        lock (_gate)
        {
            result = column < ButtonLayout.Columns
                ? _entry.ChooseCommand(_layout[_entry.ButtonSet, (row * ButtonLayout.Columns) + column])
                : PressNumeric((row * NumericPad.Columns) + column - ButtonLayout.Columns);
        }

        Act(result);
        PublishEntry();
        Repaint(force: false);
    }

    private PanelEntryResult PressNumeric(int numericIndex)
    {
        var key = NumericPad.At(numericIndex);
        return key == '\0' ? new PanelEntryResult(PanelEntryOutcome.Rejected) : _entry.Press(key);
    }

    private void Act(PanelEntryResult result)
    {
        switch (result.Outcome)
        {
            case PanelEntryOutcome.Completed when result.KeypadKeys is { Length: > 0 } keys:
                // The panel is the dispatcher's: it is the desk the station is worked from, and a
                // shunter is given areas rather than a panel.
                _keyReader.EnqueueKeys(
                    keys.Select(c => new ConsoleKeyInfo(c, c.ConsoleKey, false, false, false)),
                    _station,
                    OperatorRole.Dispatcher);
                // Guarded by IsEnabled, so Readable only runs when the message will be written.
#pragma warning disable CA1873
                if (_logger.IsEnabled(LogLevel.Information)) LogCommandSent(result.Command, Readable(keys));
#pragma warning restore CA1873
                break;

            case PanelEntryOutcome.PassThrough:
                // No command chosen, so the key keeps its postfix meaning and belongs to the
                // keypad grammar (§5.4): the right pad alone behaves as a numeric keypad.
                break;

            case PanelEntryOutcome.Rejected:
                LogPressRefused(result.Rejection);
                // A key that does nothing has to say so. The pad darkens what cannot be pressed,
                // but a press still reaches here — through a mis-hit, or through a key that is lit
                // because it is a real command with nothing behind it yet.
                _rejection = Messages.PanelRefusal(result.Rejection, Language);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// The characters a command reduced to, spelled so they can be read in a log. Two of the
    /// keypad's terminators are control characters and one is a space away from invisible.
    /// </summary>
    private static string Readable(string keys) =>
        keys.Replace("\x1b", "[ESC]", StringComparison.Ordinal)
            .Replace("#", "[ENTER]", StringComparison.Ordinal);

    private void PublishEntry()
    {
        EntryState state;
        lock (_gate)
        {
            // Carried once and then forgotten: a refusal reports the press that caused it, not
            // every state published afterwards.
            var rejection = _rejection;
            _rejection = null;
            var pending = Describe();
            state = new EntryState(
                _station,
                OperatorRole.Dispatcher,
                _entry.Command,
                _entry.ButtonSet,
                [.. _entry.Parameters],
                _entry.Current,
                _entry.IsWildcard,
                _entry.Candidates,
                _entry.IsComplete,
                _entry.RawKeys,
                pending)
            { Rejection = rejection };
        }

        // A refusal must be published even where the entry is otherwise idle — refusing the very
        // first press of a command leaves nothing else to say.
        if (state.IsIdle && state.Rejection is null) _entryStateService.NotifyEntryCleared(state.StationName);
        else _entryStateService.NotifyEntryChanged(state);
    }

    /// <summary>
    /// What is about to happen, in the operator's language. The words come from the same label
    /// table the keys are lettered from, so the line on the diagram reads the way the key the
    /// operator just pressed does.
    /// </summary>
    private string? Describe()
    {
        if (_entry.Command == PanelCommand.None) return null;

        var verb = _faces.Label(_entry.Command, Language) ?? _entry.Command.IconName;
        if (_entry.IsWildcard) return $"{verb} ✳";

        var parameters = _entry.AllParameters;
        if (parameters.Count == 0) return $"{verb} …";
        return _entry.Command.AcceptsVia
            ? $"{verb} {string.Join(" → ", parameters)}"
            : $"{verb} {string.Join(" ", parameters)}";
    }

    /// <summary>
    /// Paints every key that has changed. Each face is identified by what decides it, so a key
    /// whose picture and lighting are already right is not sent again: a repaint costs a couple of
    /// short USB transfers per key, and there are thirty-two of them on every press.
    /// </summary>
    private void Repaint(bool force)
    {
        if (!_panel.IsConnected) return;

        var language = Language;
        lock (_gate)
        {
            for (var row = 0; row < _panel.Rows; row++)
            {
                for (var column = 0; column < _panel.Columns; column++)
                {
                    var index = (row * _panel.Columns) + column;
                    if (row >= ButtonLayout.Rows) continue;

                    KeyFaceResult face;
                    string identity;

                    if (column < ButtonLayout.Columns)
                    {
                        var command = _layout[_entry.ButtonSet, (row * ButtonLayout.Columns) + column];

                        // Dimming means "not pressable *now*" — an entry-state cue: once a command
                        // is chosen the others go out (§10.2). It must not also be made to mean
                        // "no pipeline yet", or the alternate set — whose four commands are all
                        // still unbuilt — reads as a panel that has stopped answering. Being
                        // refused on press is what says that, and it says it at the moment it
                        // matters.
                        var lighting = _entry.IsIdle || command == _entry.Command
                            ? KeyLighting.Lit : KeyLighting.Dimmed;
                        var accent = AccentFor(command);
                        identity = $"c{command}{_entry.ButtonSet}{lighting}{language}{accent}";
                        face = _faces.For(command, language, _entry.ButtonSet, lighting, accent);
                    }
                    else
                    {
                        var key = NumericPad.At((row * NumericPad.Columns) + column - ButtonLayout.Columns);
                        var lighting = _entry.IsPressLegal(key) ? KeyLighting.Lit : KeyLighting.Dimmed;
                        identity = $"n{key}{lighting}{language}";
                        face = _faces.For(key, language, lighting);
                    }

                    if (!force && _painted[index] == identity) continue;
                    _painted[index] = identity;
                    _panel.SetKeyImage(index, face.Bgr24.Span);
                }
            }
        }
    }

    /// <summary>
    /// The state of the object the entry currently names, in the colour the diagram draws that
    /// state in (§10.1). Only the chosen command's own key carries it: the pad has command keys
    /// rather than object keys, so there is exactly one key that a named object belongs to.
    /// <para>
    /// Null until the parameter resolves, because until then the command names nothing yet.
    /// </para>
    /// </summary>
    private string? AccentFor(PanelCommand command)
    {
        if (command != _entry.Command || _entry.IsWildcard) return null;

        var parameters = _entry.AllParameters;
        if (parameters.Count == 0) return null;
        if (!int.TryParse(parameters[0], out var first)) return null;

        return command switch
        {
            PanelCommand.SignalStop or PanelCommand.SignalGo when IsSettled(0)
                => _signalStateService.GetSignalState(_station, first) == SignalState.Go
                    ? EbicosColours.Green : EbicosColours.Grey,

            // A point's position has no colour of its own in the symbol set — it is read off the
            // track — so the accent says only whether the interlocking knows where it stands.
            // White is §6.2.1 priority 1, the fault colour, which is what not knowing is.
            PanelCommand.PointStraight or PanelCommand.PointDiverging when IsSettled(0)
                => _pointPositionService.GetPosition(_station, first) == PointPosition.Undefined
                    ? EbicosColours.White : EbicosColours.Grey,

            PanelCommand.ShuntingAreaLock or PanelCommand.ShuntingAreaRelease when IsSettled(0)
                => _shuntingAreaService.GetState(_station, first) == ShuntingAreaState.Released
                    ? EbicosColours.Blue : null,

            PanelCommand.TrainRouteSet or PanelCommand.TrainRouteCancel
                or PanelCommand.ShuntingRouteSet or PanelCommand.ShuntingRouteRelease
                => RouteAccent(parameters),

            _ => null,
        };

        // A number still being typed names nothing: 2 on the way to 21 is not signal 2.
        bool IsSettled(int index) => index < _entry.Parameters.Count
            || (_entry.Current.Length > 0 && _entry.IsComplete);
    }

    /// <summary>
    /// Whether the route the two entered signals name is standing, and in what state. Read off the
    /// locks rather than any cached list, since that is what actually holds a route.
    /// </summary>
    private string? RouteAccent(IReadOnlyList<string> parameters)
    {
        if (parameters.Count < 2) return null;
        if (!int.TryParse(parameters[0], out var from) || !int.TryParse(parameters[^1], out var to)) return null;

        var standing = _lockingsManager.GetForStation(_station).CurrentRoutes
            .FirstOrDefault(r => r.FromSignal == from && r.ToSignal == to);
        if (standing is null) return null;

        return standing.State switch
        {
            TrainRouteState.SetMain => EbicosColours.Green,
            TrainRouteState.SetShunting => EbicosColours.Yellow,
            TrainRouteState.Cancel or TrainRouteState.Clear => EbicosColours.Magenta,
            _ => null,
        };
    }

    [LoggerMessage(EventId = 3200, Level = LogLevel.Information,
        Message = "Panel input starting, keys lettered in {Language}.")]
    private partial void LogStarting(string language);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Information, Message = "Panel input stopping.")]
    private partial void LogStopping();

    [LoggerMessage(EventId = 3202, Level = LogLevel.Information,
        Message = "Panel working {Station}: {Signals} signals, {Points} points.")]
    private partial void LogStationLoaded(string station, int signals, int points);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Information, Message = "Panel sent {Command} as {Keys}")]
    private partial void LogCommandSent(PanelCommand command, string keys);

    [LoggerMessage(EventId = 3204, Level = LogLevel.Debug, Message = "Panel refused a press: {Reason}")]
    private partial void LogPressRefused(PanelEntryRejection reason);

    [LoggerMessage(EventId = 3205, Level = LogLevel.Information, Message = "Panel keys relettered in {Language}.")]
    private partial void LogLanguageChanged(string language);
}
