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

        public SwitchDirection SwitchState
            => c switch
            {
                '+' => SwitchDirection.Straight,
                '-' => SwitchDirection.Diverging,
                _ => SwitchDirection.Undefined,
            };

        public bool IsSwitchCommand
            => c.SwitchState != SwitchDirection.Undefined;

        public TrainRouteState TrainRouteState
            => c switch
            {
                '=' => TrainRouteState.SetMain,
                '*' => TrainRouteState.SetShunting,
                '/' => TrainRouteState.Clear,
                _ => TrainRouteState.Undefined,
            };
        public bool IsTrainPathCommand
            => c.TrainRouteState != TrainRouteState.Undefined;

        public bool IsTrainRouteClearCommand
            => c == '/';

        public static char SignalDivider => '.';
    }

    extension(char? c)
    {
        public bool IsSwitchCommand
            => c is not null && c.IsTrainPathCommand;
        public bool IsTrainPathCommand
            => c is not null && c.IsTrainPathCommand;
        public bool IsClearCommand
            => c == ClearCommand;
    }

    extension(string? chars)
    {
        public int ToIntOrZero
            => chars is null || chars.Length == 0 ? 0 : int.TryParse(chars, out int value) ? value : 0;
        public bool IsSwitchCommand
            => chars is not null && chars.Length > 1 && chars[^1].IsSwitchCommand;
        public bool IsTrainPathCommand
            => chars is not null && chars.Length > 1 && chars[^1].IsTrainPathCommand;
    }

    extension(StringBuilder command)
    {
        public bool IsClearAllTrainPaths
            => command.Length == 2 && command.ToString() == CancelAllTrainRoutes;
        public bool IsSwitchCommand
            => command.Length > 1 && command[^1].IsSwitchCommand;
        public bool IsTrainPathCommand
            => command.Length > 1 && command[^1].IsTrainPathCommand;
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
