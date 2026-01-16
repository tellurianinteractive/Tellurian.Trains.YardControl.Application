namespace Tellurian.Trains.YardController;

public sealed record Point(int Number, int[] Addresses, int LockAddressOffset);

public sealed record TurntableTrack(int Number, int Address);
