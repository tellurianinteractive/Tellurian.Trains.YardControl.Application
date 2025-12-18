namespace Tellurian.Trains.YardController;

public sealed record Switch(int Number, int[] Addresses);

public static class SwitchExtensions
{
    extension(Switch sw)
    {
        public bool IsUndefined => sw.Number == 0 || sw.Addresses.Length == 0;
    }

    extension(IEnumerable<Switch> switches)
    {
        public int[] AddressesFor(int switchNumber) =>
            switches.FirstOrDefault(s => s.Number == switchNumber)?.Addresses ?? [];
    }

    extension(string? text)
    {
        public Switch ToSwitch()
        {

            if (text is null or { Length: < 2 }) return new Switch(0, []);
            var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2) goto invalidSwitch;
            var number = parts[0].ToIntOrZero;
            if (number == 0) goto invalidSwitch;
            var addresses = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => a.ToIntOrZero)
                .Where(a => a > 0)
                .ToArray();
            if (addresses.Length == 0) goto invalidSwitch;
            return new Switch(number, addresses);

        invalidSwitch:
            return new Switch(0, []);

        }
    }
}
