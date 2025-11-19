namespace Tellurian.Trains.YardController;

public static class KeyInfoExtensions
{
    extension(ConsoleKeyInfo keyInfo)
    {
        public byte[] Bytes
            => keyInfo.KeyChar.Bytes;
    }
    extension(ConsoleKeyInfo keyInfo)
    {
        public string ToHex
            => keyInfo.KeyChar.ToHex;
    }

    extension(ConsoleKeyInfo keyInfo)
    {
        public char? ValidCharOrNull
            => keyInfo.Key switch
            {
                ConsoleKey.D0 or ConsoleKey.NumPad0 => '0',
                ConsoleKey.D1 or ConsoleKey.NumPad1 => '1',
                ConsoleKey.D2 or ConsoleKey.NumPad2 => '2',
                ConsoleKey.D3 or ConsoleKey.NumPad3 => '3',
                ConsoleKey.D4 or ConsoleKey.NumPad4 => '4',
                ConsoleKey.D5 or ConsoleKey.NumPad5 => '5',
                ConsoleKey.D6 or ConsoleKey.NumPad6 => '6',
                ConsoleKey.D7 or ConsoleKey.NumPad7 => '7',
                ConsoleKey.D8 or ConsoleKey.NumPad8 => '8',
                ConsoleKey.D9 or ConsoleKey.NumPad9 => '9',
                ConsoleKey.Multiply => '*',
                ConsoleKey.Add => '+',
                ConsoleKey.Subtract => '-',
                ConsoleKey.Enter => '=',
                ConsoleKey.Backspace => '<',
                ConsoleKey.Decimal => '.',
                ConsoleKey.Divide => '/',
                _ => null,
            };
    }
    extension(char keyChar)
    {
        public ConsoleKey ConsoleKey
            => keyChar switch
            {
                '0' => ConsoleKey.D0,
                '1' => ConsoleKey.D1,
                '2' => ConsoleKey.D2,
                '3' => ConsoleKey.D3,
                '4' => ConsoleKey.D4,
                '5' => ConsoleKey.D5,
                '6' => ConsoleKey.D6,
                '7' => ConsoleKey.D7,
                '8' => ConsoleKey.D8,
                '9' => ConsoleKey.D9,
                '*' => ConsoleKey.Multiply,
                '+' => ConsoleKey.Add,
                '-' => ConsoleKey.Subtract,
                '=' => ConsoleKey.Enter,
                '<' => ConsoleKey.Backspace,
                '.' => ConsoleKey.Decimal,
                '/' => ConsoleKey.Divide,
                _ => ConsoleKey.NoName,
            };
    }
}
