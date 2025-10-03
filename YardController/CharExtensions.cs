namespace Tellurian.Trains.YardController;

using System.Text;

public static class CharExtensions
{
    extension(char c)
    {
        public byte[] Bytes
            => Encoding.UTF8.GetBytes([c]);

        public string ToHex
            => BitConverter.ToString(c.Bytes);


        public SwitchState SwitchState
            => c switch
            {
                '+' => SwitchState.Diverging,
                _ => SwitchState.Straight,
            };
        public bool IsSwitchCommand
        => c is '+' or '-';

        public bool IsTrainPathCommand
            => c is '*' or '/' or '=';

        public static char SignalDivider => '.';

    }

    extension(char? c)
    {
        public bool IsSwitchCommand
            => c is not null && c.IsTrainPathCommand;
        public bool IsTrainPathCommand
            => c is not null && c.IsTrainPathCommand;

    }
    extension(char c)
    {
        public TrainPathState TrainPathState
                          => c switch
                          {
                              '*' => TrainPathState.Clear,
                              '/' => TrainPathState.Cancel,
                              _ => TrainPathState.Set,
                          };
    }

    extension(string? chars)
    {
        public int ToIntOrZero
            => chars is null || chars.Length == 0 ? 0 : int.Parse(chars);
        public bool IsSwitchCommand
            => chars is not null && chars.Length > 1 && chars[^1].IsSwitchCommand;
        public bool IsTrainPathCommand
            => chars is not null && chars.Length > 1 && chars[^1].IsTrainPathCommand;
    }

    extension(StringBuilder command)
    {
        public bool IsClearAllTrainPaths
            => command.Length == 1 && command[0] == '/';
        public bool IsSwitchCommand
            => command.Length > 1 && command[^1].IsSwitchCommand;
        public bool IsTrainPathCommand
            => command.Length > 1 && command[^1].IsTrainPathCommand;
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
