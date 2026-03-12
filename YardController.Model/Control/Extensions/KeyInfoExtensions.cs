using System.Text.Json;

namespace Tellurian.Trains.YardController.Model.Control.Extensions;

public static class KeyInfoExtensions
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        IncludeFields = false,
    };

    public record ConsoleKeyInfoData(char KeyChar, ConsoleKey Key, ConsoleModifiers Modifiers);

    extension(ConsoleKeyInfoData? data)
    {
        public ConsoleKeyInfo ToConsoleKeyInfo()
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

        public bool IsEmpty => keyInfo.Key == ConsoleKey.None && keyInfo.KeyChar == '\0';

        public string Serialize() => JsonSerializer.Serialize(keyInfo, _jsonOptions);



        public static ConsoleKeyInfo Deserialize(string json)
        {
            var data = JsonSerializer.Deserialize<ConsoleKeyInfoData>(json, _jsonOptions);
            return data.ToConsoleKeyInfo();
        }


        public char? ValidCharOrNull
            => keyInfo.KeyChar switch
            {
                >= '0' and <= '9' => keyInfo.KeyChar,
                '+' or '-' or '*' or '/' or '.' or '=' => keyInfo.KeyChar,
                _ => keyInfo.Key switch
                {
                    ConsoleKey.NumPad0 => '0',
                    ConsoleKey.NumPad1 => '1',
                    ConsoleKey.NumPad2 => '2',
                    ConsoleKey.NumPad3 => '3',
                    ConsoleKey.NumPad4 => '4',
                    ConsoleKey.NumPad5 => '5',
                    ConsoleKey.NumPad6 => '6',
                    ConsoleKey.NumPad7 => '7',
                    ConsoleKey.NumPad8 => '8',
                    ConsoleKey.NumPad9 => '9',
                    ConsoleKey.Add => '+',
                    ConsoleKey.Subtract => '-',
                    ConsoleKey.Multiply => '*',
                    ConsoleKey.Divide => '/',
                    ConsoleKey.Decimal => '.',
                    ConsoleKey.Enter => '#',
                    ConsoleKey.Backspace => '<',
                    ConsoleKey.Escape => '\x1b',
                    ConsoleKey.OemPlus => '=',
                    _ => null,
                }
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
                '#' => ConsoleKey.Enter,
                '<' => ConsoleKey.Backspace,
                '.' => ConsoleKey.Decimal,
                '/' => ConsoleKey.Divide,
                '\x1b' => ConsoleKey.Escape,
                '=' => ConsoleKey.OemPlus,
                _ => ConsoleKey.NoName,
            };
    }
}
