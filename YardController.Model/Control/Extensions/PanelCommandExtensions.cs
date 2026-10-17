using System.Globalization;

namespace Tellurian.Trains.YardController.Model.Control.Extensions;

/// <summary>
/// What each command-pad button asks for, and what it reduces to. Ten of the eighteen commands
/// have an equivalent in the keypad grammar and cost only a translation table
/// (Specifications/InputDevices.md §11.3); four more reached one when the train-number lifecycle
/// was built, and the remaining four have no service to reach yet.
/// </summary>
public static class PanelCommandExtensions
{
    private const char Enter = '#';
    private const char Cancel = '\x1b';
    private const char Divider = '.';

    extension(PanelCommand command)
    {
        /// <summary>
        /// The icon drawn on the key, which is also the name a station file's [Buttons] section
        /// uses. One name for the key's identity, its picture and its label keeps the three from
        /// drifting apart.
        /// </summary>
        public string IconName => command switch
        {
            PanelCommand.PointStraight => "point-straight",
            PanelCommand.PointDiverging => "point-diverging",
            PanelCommand.SignalStop => "signal-stop",
            PanelCommand.SignalGo => "signal-go",
            PanelCommand.TrainRouteSet => "route-train-set",
            PanelCommand.TrainRouteCancel => "route-train-cancel",
            PanelCommand.ShuntingRouteSet => "route-shunting-set",
            PanelCommand.ShuntingRouteRelease => "route-shunting-release",
            PanelCommand.TrainApproved => "train-approved",
            PanelCommand.TrainAdvance => "train-advance",
            PanelCommand.ShuntingAreaLock => "area-lock",
            PanelCommand.ShuntingAreaRelease => "area-release",
            PanelCommand.PointLock => "point-lock",
            PanelCommand.PointUnlock => "point-unlock",
            PanelCommand.SignalLockStop => "signal-lock-stop",
            PanelCommand.SignalUnlockStop => "signal-unlock-stop",
            PanelCommand.ButtonsAlternate => "buttons-alternate",
            PanelCommand.ButtonsMain => "buttons-main",
            _ => "",
        };

        /// <summary>
        /// The parameters the command asks for, in the order it asks for them. A route asks for
        /// two signals and takes any number more, the last being the destination; everything else
        /// asks for a fixed list.
        /// </summary>
        public IReadOnlyList<PanelParameter> Parameters => command switch
        {
            PanelCommand.PointStraight or PanelCommand.PointDiverging
                or PanelCommand.PointLock or PanelCommand.PointUnlock => [PanelParameter.Point],

            PanelCommand.SignalStop or PanelCommand.SignalGo
                or PanelCommand.SignalLockStop or PanelCommand.SignalUnlockStop => [PanelParameter.Signal],

            // Setting a route needs both ends: which movement, from where to where.
            PanelCommand.TrainRouteSet or PanelCommand.ShuntingRouteSet
                => [PanelParameter.Signal, PanelParameter.Signal],

            // Tearing one down needs only where it ends. A route is identified by its destination
            // — the keypad's {signal}/ form has always been enough, and the pipeline builds
            // TrainRouteCommand(0, toSignal, …) either way, so a from-signal entered here would be
            // a number the operator typed for nothing.
            PanelCommand.TrainRouteCancel or PanelCommand.ShuntingRouteRelease
                => [PanelParameter.Signal],

            // Accepting a train from the neighbouring station names the signal it comes in at and
            // the train that is coming: the number goes onto the run in from the layout edge.
            PanelCommand.TrainApproved => [PanelParameter.Signal, PanelParameter.TrainNumber],

            PanelCommand.TrainAdvance => [PanelParameter.TrainNumber],

            PanelCommand.ShuntingAreaLock or PanelCommand.ShuntingAreaRelease => [PanelParameter.ShuntingArea],

            _ => [],
        };

        /// <summary>
        /// True where more signals than the two required may be entered, each extra one a via.
        /// Only setting a route runs through anything: a teardown names one signal, and a via on
        /// the way to a destination that is all the command uses would mean nothing.
        /// </summary>
        public bool AcceptsVia => command is PanelCommand.TrainRouteSet or PanelCommand.ShuntingRouteSet;

        /// <summary>
        /// True where the wildcard may stand in for the number, meaning all of them (§6.2). Ten
        /// commands take it; neither "all points diverging" nor "all signals go" is an operation,
        /// so their keys refuse it.
        /// </summary>
        public bool AcceptsWildcard => command is PanelCommand.PointStraight or PanelCommand.SignalStop
            or PanelCommand.TrainRouteCancel or PanelCommand.ShuntingRouteRelease
            or PanelCommand.ShuntingAreaLock or PanelCommand.ShuntingAreaRelease
            or PanelCommand.PointLock or PanelCommand.PointUnlock
            or PanelCommand.SignalLockStop or PanelCommand.SignalUnlockStop;

        /// <summary>Switching button set is not a yard command; it never leaves the panel.</summary>
        public bool SwitchesButtonSet => command is PanelCommand.ButtonsAlternate or PanelCommand.ButtonsMain;

        /// <summary>
        /// True where the command reaches something that exists. The four locking commands are
        /// declared by the layout and drawn on the alternate buttons, but nothing holds a point
        /// or a signal against the interlocking yet (§12), so pressing one is refused rather than
        /// silently doing nothing.
        /// </summary>
        public bool IsImplemented => command is not (PanelCommand.None
            or PanelCommand.PointLock or PanelCommand.PointUnlock
            or PanelCommand.SignalLockStop or PanelCommand.SignalUnlockStop);

        /// <summary>
        /// True where the wildcard form specifically is there. The two shunting-area keys take a
        /// number today but not "all areas" (§12), and refusing the wildcard is not the same as
        /// refusing the key.
        /// </summary>
        public bool IsWildcardImplemented => command.AcceptsWildcard
            && command.IsImplemented
            && command is not (PanelCommand.ShuntingAreaLock or PanelCommand.ShuntingAreaRelease);

        /// <summary>
        /// The keypad characters for the wildcard form, or null where the command has none. The
        /// three doubled keypad commands live here: they are the wildcard by another spelling.
        /// </summary>
        public string? WildcardKeys => command switch
        {
            PanelCommand.PointStraight => "==",
            PanelCommand.SignalStop => "**",
            PanelCommand.TrainRouteCancel => $"{Cancel}{Cancel}",
            PanelCommand.ShuntingRouteRelease => "//",
            _ => null,
        };

        /// <summary>
        /// The keypad characters this command reduces to, or null where it reaches no pipeline.
        /// The numbers are passed as entered — validating them belongs to the entry that
        /// collected them, not to the translation.
        /// <para>
        /// Routes are always spelled with the dot divider, even for the two-signal case. The
        /// undotted form works only because the keypad parser reads a five-character command as
        /// two two-digit signals, which is an assumption about signal numbering that a panel
        /// knowing the real numbers has no reason to inherit.
        /// </para>
        /// </summary>
        public string? ToKeypadKeys(IReadOnlyList<string> parameters, bool wildcard)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            if (wildcard) return command.WildcardKeys;
            if (!command.IsImplemented) return null;

            var first = parameters.Count > 0 ? parameters[0] : "";
            var signals = string.Join(Divider, parameters);

            return command switch
            {
                PanelCommand.PointStraight => $"{first}+",
                PanelCommand.PointDiverging => $"{first}-",
                PanelCommand.SignalStop => $"S{first}-",
                PanelCommand.SignalGo => $"S{first}+",

                PanelCommand.TrainRouteSet => $"{signals}{Enter}",
                PanelCommand.ShuntingRouteSet => $"{signals}*",

                // Teardown by destination alone, as the keypad's {signal}␛ and {signal}/ do.
                PanelCommand.TrainRouteCancel => $"{first}{Cancel}",
                PanelCommand.ShuntingRouteRelease => $"{first}/",

                // .{signal}={train} puts the number straight onto the stretch running in from the
                // layout edge, which is where a train the neighbouring station has offered stands.
                PanelCommand.TrainApproved when parameters.Count > 1
                    => $"{Divider}{first}={parameters[1]}{Enter}",

                // ={train} is one step of the lifecycle, and which step it takes is deduced from
                // where the number stands: arrived at the route's final signal, then departed past
                // the last outbound one, then gone from the station.
                PanelCommand.TrainAdvance => $"={first}{Enter}",

                PanelCommand.ShuntingAreaLock => $"A{first}-",
                PanelCommand.ShuntingAreaRelease => $"A{first}+",

                _ => null,
            };
        }
    }

    extension(string? name)
    {
        /// <summary>
        /// The command an icon name stands for, which is how a station file's [Buttons] section
        /// spells one. <see cref="PanelCommand.None"/> for a blank key and for anything
        /// unrecognised — a typo leaves a dark key rather than stopping the station loading.
        /// </summary>
        public PanelCommand ToPanelCommand()
        {
            if (string.IsNullOrWhiteSpace(name)) return PanelCommand.None;
            var trimmed = name.Trim();
            if (trimmed is "-" or "—") return PanelCommand.None;
            foreach (var candidate in Enum.GetValues<PanelCommand>())
            {
                if (candidate != PanelCommand.None
                    && string.Equals(candidate.IconName, trimmed, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return PanelCommand.None;
        }
    }

    extension(int number)
    {
        /// <summary>A number as an operator entered it: plain digits, no grouping, no sign.</summary>
        public string ToPanelDigits() => number.ToString(CultureInfo.InvariantCulture);
    }
}
