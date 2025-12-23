using System.Diagnostics.CodeAnalysis;
using Tellurian.Trains.Interfaces.Accessories;
using Tellurian.Trains.Protocols.LocoNet.Commands;

namespace Tellurian.Trains.YardController;

public enum SwitchDirection
{
    Undefined = 0,
    Straight = 1,
    Diverging = 2
}

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

public sealed record SwitchCommand(int Number, SwitchDirection Direction) : IEqualityComparer<SwitchCommand>
{
    private readonly List<int> _addresses = [];
    public IEnumerable<int> Addresses => _addresses;
    internal void AddAddresses(int[] addresses)
    {
        foreach (int address in addresses)
        {
            if (_addresses.Contains(address)) continue;
            _addresses.Add(address);
        }
    }

    public override string ToString() => $"{Number}:{Direction} - {string.Join('-', Addresses)}";
    public bool Equals(SwitchCommand? other) =>
        other is not null &&
        other.Number == Number &&
        other.Direction == Direction &&
        other.Addresses.SequenceEqual(Addresses);
    public override int GetHashCode() => HashCode.Combine(Number, Direction, Addresses);
    public bool Equals(SwitchCommand? x, SwitchCommand? y) => x?.Number == y?.Number;
    public int GetHashCode([DisallowNull] SwitchCommand obj) => GetHashCode();
};

public static class SwitchCommandExtensions
{
    extension(SwitchCommand command)
    {
        public static SwitchCommand Undefined => new(0, SwitchDirection.Undefined);

        public bool IsUndefined => command.Direction == SwitchDirection.Undefined;

        internal SwitchLock ToSwitchLock => new(command, false);

        public static SwitchCommand Create(int number, SwitchDirection direction, int[] addresses)
        {
            var cmd = new SwitchCommand(number, direction);
            cmd.AddAddresses(addresses);
            return cmd;
        }

        public static bool Equals(SwitchCommand one, SwitchCommand another) =>
            one.Number == another.Number &&
            one.Direction == another.Direction &&
            one.Addresses.SequenceEqual(another.Addresses);

        public IEnumerable<SetTurnoutCommand> ToTurnoutCommands()
        {
            var position = command.Direction == SwitchDirection.Straight ? Position.ClosedOrGreen : Position.ThrownOrRed;
            foreach (var address in command.Addresses)
            {
                yield return new SetTurnoutCommand(Address.From((short)address), position, MotorState.On);
            }
        }
    }

    extension(string? commandText)
    {
        public SwitchCommand ToSwitchCommand()
        {
            if (commandText is null || commandText.Length < 2) return SwitchCommand.Undefined;
            var number = commandText[0..^1].ToIntOrZero;
            return new SwitchCommand(number, commandText.SwitchState);
        }

        private SwitchDirection SwitchState
        {
            get
            {
                if (commandText is null or { Length: < 2 }) return SwitchDirection.Undefined;
                return commandText[^1].SwitchDirection;
            }
        }
    }
}
