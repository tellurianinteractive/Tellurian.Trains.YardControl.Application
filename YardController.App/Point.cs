namespace Tellurian.Trains.YardController;

public sealed record Point(int Number, int[] StraightAddresses, int[] DivergingAddresses, int LockAddressOffset);
