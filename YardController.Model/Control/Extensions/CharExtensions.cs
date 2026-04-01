using System.Text;

namespace Tellurian.Trains.YardController.Model.Control.Extensions;

public static class CharExtensions
{
    const string ClearAllTrainRoutes = "//";
    const string CancelAllTrainRoutes = "\x1b\x1b";
    const string StopAllSignals = "**";
    const string AllPointsStraight = "==";
    const char ClearCommand = '<';

    extension(char c)
    {

        public byte[] Bytes
            => Encoding.UTF8.GetBytes([c]);

        public string ToHex
            => BitConverter.ToString(c.Bytes);

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
                var last = char.ToLower(remaining[^1]);
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
                return (remaining[..^1].ToIntOrZero, char.ToLower(lastChar), messageKind);

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
            => command.Length > 1 && command[^1].IsPointCommand;
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
