namespace Tellurian.Trains.YardController;

public static class SwitchDirectionExtensions
{
    extension(SwitchDirection direction)
    {
        public char Char => direction switch
        {
            SwitchDirection.Straight => '+',
            SwitchDirection.Diverging => '-',
            _ => '?'
        };
    }

    extension(char c)
    {
        public SwitchDirection SwitchDirection => c switch
        {
            '+' => SwitchDirection.Straight,
            '-' => SwitchDirection.Diverging,
            _ => SwitchDirection.Undefined
        };
    }
}
