namespace Tellurian.Trains.YardController.Extensions;

using System.Text.Json;

public static class KeyInfoExtensions
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        IncludeFields = false,
    };

    internal record ConsoleKeyInfoData(char KeyChar, ConsoleKey Key, ConsoleModifiers Modifiers);

    extension(ConsoleKeyInfoData? data)
    {
        internal ConsoleKeyInfo ToConsoleKeyInfo()
        {
            if (data is null) return ConsoleKeyInfo.Empty;
            return new ConsoleKeyInfo(data.KeyChar, data.Key,
                        Modifier(data.Modifiers, ConsoleModifiers.Shift),
                        Modifier(data.Modifiers, ConsoleModifiers.Alt),
                        Modifier(data.Modifiers, ConsoleModifiers.Control));

            static bool Modifier(ConsoleModifiers modifiers, ConsoleModifiers modifierToCheck)
                => (modifiers & modifierToCheck) == modifierToCheck;

        }
    }

    extension(byte[] buffer)
    {
        public ConsoleKeyInfo Deserialize()
        {
            var jsonReader = new Utf8JsonReader(buffer);
            try
            {
                var data = JsonSerializer.Deserialize<ConsoleKeyInfoData>(ref jsonReader, _jsonOptions);
                return data.ToConsoleKeyInfo();
            }
            catch (Exception)
            {
                return ConsoleKeyInfo.Empty;
            }
        }
    }

    extension(ConsoleKeyInfo keyInfo)
    {
        public static ConsoleKeyInfo Empty => new('\0', ConsoleKey.None, false, false, false);

        public bool IsEmpty => keyInfo.Key == ConsoleKey.None;

        public string Serialize() => JsonSerializer.Serialize(keyInfo, _jsonOptions);



        public static ConsoleKeyInfo Deserialize(string json)
        {
            var data = JsonSerializer.Deserialize<ConsoleKeyInfoData>(json, _jsonOptions);
            return data.ToConsoleKeyInfo();
        }


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
