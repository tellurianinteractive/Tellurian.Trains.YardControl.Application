using System.Diagnostics.CodeAnalysis;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace Tellurian.Trains.YardController.Model.Control;

public sealed record PointCommand(int Number, PointPosition Position, int? LockAddressOffset = null, bool IsOnRoute = true) : IEqualityComparer<PointCommand>
{
    public const int MinLockAddressOffset = 100;
    private readonly List<int> _addresses = [];
    public IEnumerable<int> Addresses => _addresses;
    public IEnumerable<int> LockAddresses =>
        AlsoLock || AlsoUnlock ? _addresses.Select(x => x + LockAddressOffset!.Value) : [];
    public bool AlsoLock { get; } = LockAddressOffset > 0;
    public bool AlsoUnlock { get; } = LockAddressOffset > 0;
    internal void AddAddresses(int[] addresses)
    {
        foreach (int address in addresses)
        {
            if (_addresses.Contains(address)) continue;
            _addresses.Add(address);
        }
    }

    public override string ToString() => $"{Number}{Position.Char}: {string.Join(' ', Addresses.Select(a => DisplayedAddress(a)))}";

    public bool Equals(PointCommand? other) =>
        other is not null &&
        other.Number == Number &&
        other.Position == Position &&
        other.Addresses.SequenceEqual(Addresses);
    public override int GetHashCode() => HashCode.Combine(Number, Position, Addresses);
    public bool Equals(PointCommand? x, PointCommand? y) => x?.Number == y?.Number;
    public int GetHashCode([DisallowNull] PointCommand obj) => GetHashCode();

    string DisplayedAddress(int address) =>
       Position == PointPosition.Straight ? address > 0 ? $"{int.Abs(address)}+" : $"{int.Abs(address)}-" :
       Position == PointPosition.Diverging ? address > 0 ? $"{int.Abs(address)}-" : $"{int.Abs(address)}+" :
       string.Empty;

};
