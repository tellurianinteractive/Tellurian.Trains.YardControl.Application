using System.Text;

namespace Tellurian.Trains.YardController.Model.Control.Extensions;

public static class CharExtensions
{
    const string ClearAllTrainRoutes = "//";
    const string CancelAllTrainRoutes = "\x1b\x1b";
    const string StopAllSignals = "**";
    const string AllPointsStraight = "==";
    const char ClearCommand = '<';
    const char ShuntingAreaPrefix = 'A';
    const char SignalPrefix = 'S';
    const char OccupancySectionPrefix = 'T';

    extension(char c)
    {

        public string ToHex
            => BitConverter.ToString(Encoding.UTF8.GetBytes([c]));

        public PointPosition ToPointPosition
            => c switch
            {
                '+' => PointPosition.Straight,
                '-' => PointPosition.Diverging,
                _ => PointPosition.Undefined,
            };

        public bool IsPointCommand
            => c.ToPointPosition != PointPosition.Undefined;

        public TrainRouteState TrainRouteState
            => c switch
            {
                '#' => TrainRouteState.SetMain,
                '*' => TrainRouteState.SetShunting,
                '\x1b' => TrainRouteState.Cancel,
                '/' => TrainRouteState.Clear,
                _ => TrainRouteState.Undefined,
            };
        public bool IsTrainRouteCommand
            => c.TrainRouteState != TrainRouteState.Undefined;

        public bool IsTrainRouteTeardownCommand
            => c == '\x1b' || c == '/';

        public bool IsTrainNumberSeparator => c == '=';

        public static char SignalDivider => '.';
    }

    extension(char? c)
    {
        public bool IsPointCommand
            => c is not null && c.IsTrainRouteCommand;
        public bool IsTrainRouteCommand
            => c is not null && c.IsTrainRouteCommand;
        public bool IsClearCommand
            => c == ClearCommand;
    }

    extension(string? chars)
    {
        public int ToIntOrZero
            => chars is null || chars.Length == 0 ? 0 : int.TryParse(chars, out int value) ? value : 0;
        public bool IsPointCommand
            => chars is not null && chars.Length > 1 && chars[^1].IsPointCommand;
        public bool IsTrainRouteCommand
            => chars is not null && chars.Length > 1 && chars[^1].IsTrainRouteCommand;
        public bool IsWhiteSpace
            => string.IsNullOrWhiteSpace(chars);

        /// <summary>
        /// The edge form of a train-number command, with the terminator already taken off: a dot
        /// beside the signal number stands for the edge of the layout, and the side it sits on
        /// says which side of the signal the edge is on. <c>21.</c> is the track running out past
        /// signal 21, <c>.21</c> the track running in to it. Null for anything else, a composite
        /// route included — that has a signal on both sides of every dot.
        /// </summary>
        public (int Signal, bool Beyond)? ToEdgeSignal()
        {
            if (chars is null || chars.Length < 2) return null;
            var beyond = chars[^1] == char.SignalDivider;
            var digits = beyond ? chars[..^1] : chars[0] == char.SignalDivider ? chars[1..] : null;
            if (digits is null || digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return null;
            return (digits.ToIntOrZero, beyond);
        }

        public (int Address, char? SubPoint) ToAddressWithSubPoint()
        {
            var (address, subPoint, _) = chars.ToAddressWithSubPointAndKind();
            return (address, subPoint);
        }

        public (int Address, char? SubPoint, AccessoryMessageKind MessageKind) ToAddressWithSubPointAndKind()
        {
            if (chars is null or { Length: 0 }) return (0, null, AccessoryMessageKind.Command);

            var remaining = chars;
            var messageKind = (AccessoryMessageKind)0;

            // Strip trailing c/n suffixes (right-to-left, handles cn/nc)
            while (remaining.Length > 0)
            {
                var last = char.ToLowerInvariant(remaining[^1]);
                if (last == 'c')
                {
                    messageKind |= AccessoryMessageKind.Command;
                    remaining = remaining[..^1];
                }
                else if (last == 'n')
                {
                    messageKind |= AccessoryMessageKind.Notification;
                    remaining = remaining[..^1];
                }
                else break;
            }

            // Default to Command if no explicit suffix
            if (messageKind == 0) messageKind = AccessoryMessageKind.Command;

            if (remaining.Length == 0) return (0, null, messageKind);

            // Check for sub-point letter
            var lastChar = remaining[^1];
            if (char.IsLetter(lastChar) && remaining.Length > 1)
                return (remaining[..^1].ToIntOrZero, char.ToLowerInvariant(lastChar), messageKind);

            return (remaining.ToIntOrZero, null, messageKind);
        }
    }

    extension(StringBuilder command)
    {
        public bool IsReloadConfiguration
            => command.Length == 2 && command[0] == '+' && command[1] == '-';
        public bool IsClearAllTrainRoutes
            => command.Length == 2 && command.ToString() == ClearAllTrainRoutes;
        public bool IsCancelAllTrainRoutes
            => command.Length == 2 && command.ToString() == CancelAllTrainRoutes;
        public bool IsAllSignalsStop
            => command.Length == 2 && command.ToString() == StopAllSignals;
        public bool IsAllPointsStraight
            => command.Length == 2 && command.ToString() == AllPointsStraight;
        public bool IsPointCommand
            => command.Length > 1 && !command.HasCommandPrefix && command[^1].IsPointCommand;
        /// <summary>
        /// A shunting area command: <c>A{area}+</c> releases the area to a shunter,
        /// <c>A{area}-</c> takes it back under the interlocking's control.
        /// </summary>
        public bool IsShuntingAreaCommand
            => command.Length > 2 && command[0] == ShuntingAreaPrefix && command[^1].IsPointCommand;
        /// <summary>
        /// A signal command: <c>S{signal}+</c> gives the signal go, <c>S{signal}-</c> puts it to stop.
        /// </summary>
        public bool IsSignalCommand
            => command.Length > 2 && command[0] == SignalPrefix && command[^1].IsPointCommand;
        /// <summary>
        /// An occupancy hold command: <c>T{section}+</c> holds the detection stretch occupied by
        /// hand, <c>T{section}-</c> frees one that is being held.
        /// </summary>
        public bool IsOccupancyHoldCommand
            => command.Length > 2 && command[0] == OccupancySectionPrefix && command[^1].IsPointCommand;
        /// <summary>
        /// <c>T-</c>, with no number: frees every held stretch at once. The board starts an
        /// operating session clear, and a hold outlives the train that caused it.
        /// </summary>
        public bool IsReleaseAllOccupancyHolds
            => command.Length == 2 && command[0] == OccupancySectionPrefix
               && command[1].ToPointPosition == PointPosition.Diverging;
        /// <summary>
        /// A leading letter says the number that follows is not a point's: it names a shunting
        /// area, a signal or a track detection stretch instead.
        /// </summary>
        private bool HasCommandPrefix
            => command.Length > 0
               && (command[0] == ShuntingAreaPrefix || command[0] == SignalPrefix || command[0] == OccupancySectionPrefix);
        public bool IsTurntableCommand =>
            command.Length >= 2 && (command[0] == '+' || command[0] == '-') && command[^1] == '#';
        public bool IsTrainRouteCommand
            => command.Length > 1 && command[^1].IsTrainRouteCommand;
        public bool All(char value, int length = 2) => command.Length == length && command.ToString().All(c => c == value);

        public string CommandString
        {
            get
            {
                var result = command.ToString();
                command.Clear();
                return result;
            }
        }
    }
}
