using Tellurian.Trains.YardController;
using Tellurian.Trains.YardController.Extensions;

namespace Tellurian.Trains.YardController.Extensions;

using System.Text;

public static class CharExtensions
{
    const string CancelAllTrainRoutes = "//";
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
                '=' => TrainRouteState.SetMain,
                '*' => TrainRouteState.SetShunting,
                '/' => TrainRouteState.Clear,
                _ => TrainRouteState.Undefined,
            };
        public bool IsTrainRouteCommand
            => c.TrainRouteState != TrainRouteState.Undefined;

        public bool IsTrainRouteClearCommand
            => c == '/';

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
    }

    extension(StringBuilder command)
    {
        public bool IsReloadConfiguration
            => command.Length == 2 && command[0] == '+' && command[1] == '-';
        public bool IsClearAllTrainRoutes
            => command.Length == 2 && command.ToString() == CancelAllTrainRoutes;
        public bool IsPointCommand
            => command.Length > 1 && command[^1].IsPointCommand;
        public bool IsTurntableCommand =>
            command.Length >= 2 && (command[0] == '+' || command[0] == '-') && command[^1] == '=';
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
