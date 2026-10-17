using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Which of the command pad's two sets of buttons is showing. The names say nothing about what
/// the sets carry (Specifications/InputDevices.md D9): today the alternate set happens to hold
/// locking commands, but nothing about the arrangement requires that.
/// </summary>
public enum ButtonSet
{
    Main = 0,
    Alternate = 1,
}

/// <summary>
/// The command pad's two sets of sixteen buttons. Declared per station in the unified station
/// file's [Buttons] section, because laying out thirty-two keys across two sets is done
/// iteratively and the station file already reloads while the application runs.
/// </summary>
public sealed record ButtonLayout
{
    /// <summary>Keys across the command pad.</summary>
    public const int Columns = 4;

    /// <summary>Keys down the command pad.</summary>
    public const int Rows = 4;

    /// <summary>Keys in one button set.</summary>
    public const int Count = Columns * Rows;

    /// <summary>The key that switches button sets, at position 4·4 (§5.1).</summary>
    public const int SwitchKeyIndex = Count - 1;

    private readonly PanelCommand[] _main = new PanelCommand[Count];
    private readonly PanelCommand[] _alternate = new PanelCommand[Count];

    private ButtonLayout(IReadOnlyList<PanelCommand> main, IReadOnlyList<PanelCommand> alternate)
    {
        for (var i = 0; i < Count; i++)
        {
            _main[i] = i < main.Count ? main[i] : PanelCommand.None;
            _alternate[i] = i < alternate.Count ? alternate[i] : PanelCommand.None;
        }
    }

    /// <summary>
    /// The arrangement of §5.1 and §5.2: rows grouped by domain, everything pressed while trains
    /// are running on the main set and everything else on the alternate. Used where a station file
    /// declares no [Buttons] section, so a panel works before anyone has laid one out.
    /// </summary>
    public static ButtonLayout Default { get; } = new(
    [
        PanelCommand.PointStraight,   PanelCommand.PointDiverging,    PanelCommand.SignalStop,        PanelCommand.SignalGo,
        PanelCommand.TrainRouteSet,   PanelCommand.TrainRouteCancel,  PanelCommand.ShuntingRouteSet,  PanelCommand.ShuntingRouteRelease,
        PanelCommand.TrainApproved,   PanelCommand.TrainAdvance,      PanelCommand.None,              PanelCommand.None,
        PanelCommand.ShuntingAreaLock, PanelCommand.ShuntingAreaRelease, PanelCommand.None,           PanelCommand.ButtonsAlternate,
    ],
    [
        PanelCommand.PointLock,       PanelCommand.PointUnlock,       PanelCommand.SignalLockStop,    PanelCommand.SignalUnlockStop,
        PanelCommand.None,            PanelCommand.None,              PanelCommand.None,              PanelCommand.None,
        PanelCommand.None,            PanelCommand.None,              PanelCommand.None,              PanelCommand.None,
        PanelCommand.None,            PanelCommand.None,              PanelCommand.None,              PanelCommand.ButtonsMain,
    ]);

    /// <summary>
    /// Builds a layout from icon names, sixteen per set, reading left to right and top to bottom.
    /// A name that is not an icon leaves the key blank rather than failing the load: a station
    /// file is edited while the application runs, and a typo should darken one key, not the yard.
    /// </summary>
    public static ButtonLayout FromIconNames(IReadOnlyList<string> main, IReadOnlyList<string> alternate)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(alternate);
        return new ButtonLayout(
            [.. main.Select(n => n.ToPanelCommand())],
            [.. alternate.Select(n => n.ToPanelCommand())]);
    }

    /// <summary>The command on a key of a set, or <see cref="PanelCommand.None"/> outside the pad.</summary>
    public PanelCommand this[ButtonSet set, int index] =>
        index < 0 || index >= Count ? PanelCommand.None
        : set == ButtonSet.Alternate ? _alternate[index] : _main[index];

    /// <summary>The sixteen commands of a set, in key order.</summary>
    public IReadOnlyList<PanelCommand> this[ButtonSet set] => set == ButtonSet.Alternate ? _alternate : _main;

    /// <summary>
    /// True where the layout declares any alternate command at all. A station with a single set
    /// of buttons should not spend a key on switching to an empty one.
    /// </summary>
    public bool HasAlternateSet => _alternate.Any(c => c is not PanelCommand.None and not PanelCommand.ButtonsMain);
}
