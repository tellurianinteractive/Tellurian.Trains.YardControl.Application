namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// What a command-pad button does. Entry on a button panel is command-first
/// (<c>{command}{parameter}⏎</c>, Specifications/InputDevices.md §6.1), which is what makes the
/// intent known — and displayable — from the very first press.
/// <para>
/// The two set-switching entries are commands in the same sense: they are what a key does when
/// pressed. They take no parameter and never reach the yard.
/// </para>
/// </summary>
public enum PanelCommand
{
    /// <summary>A key carrying nothing. Drawn dark and inert; §5.2 leaves eleven of them.</summary>
    None = 0,

    PointStraight,
    PointDiverging,
    SignalStop,
    SignalGo,
    TrainRouteSet,
    TrainRouteCancel,
    ShuntingRouteSet,
    ShuntingRouteRelease,
    TrainApproved,

    /// <summary>
    /// One step of the train-number lifecycle. Which step it turns out to be — arrived at the
    /// route's final signal, departed past the last outbound one, then gone from the station —
    /// is deduced from where the number is standing, so a key per step would be three keys
    /// sending the same characters and three ways for the operator to be told they were wrong.
    /// </summary>
    TrainAdvance,
    ShuntingAreaLock,
    ShuntingAreaRelease,
    PointLock,
    PointUnlock,
    SignalLockStop,
    SignalUnlockStop,

    /// <summary>Switches the command pad to the alternate buttons (§5.3).</summary>
    ButtonsAlternate,

    /// <summary>Switches the command pad back to the main buttons.</summary>
    ButtonsMain,
}
