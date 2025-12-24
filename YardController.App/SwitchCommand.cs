using System.Diagnostics.CodeAnalysis;

namespace Tellurian.Trains.YardController;

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
