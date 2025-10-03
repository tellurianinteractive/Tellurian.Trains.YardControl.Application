namespace Tellurian.Trains.YardController;

public record struct SwitchCommand(string? Number, int Address, SwitchState State);

public static class SwitchCommandExtensions
{
    extension(SwitchCommand command)
    {
        public static SwitchCommand Undefined => new("0", 0, SwitchState.Straight);
        public bool IsUndefined => command.Address == 0 || command.State == SwitchState.Undefined;
    }

    extension(string? commandText)
    {
        public SwitchCommand ToSwitchCommand(Dictionary<int,int> switchAdresses) =>
            commandText is null || commandText.Length < 2   
            ? SwitchCommand.Undefined
            : new SwitchCommand(commandText, switchAdresses.AddressFrom(commandText[0..^1]), commandText.SwitchState);

        private int Adress => 
            commandText is null or { Length: < 2 } 
            ? 0 
            : commandText[0..^1].ToIntOrZero;

        private SwitchState SwitchState
        {
            get
            {
                if (commandText is null or { Length: < 2 }) return SwitchState.Undefined;
                return commandText[^1] switch
                {
                    '-' => SwitchState.Straight,
                    '+' => SwitchState.Diverging,
                    _ => SwitchState.Undefined
                };
            }
        }
    }
}
