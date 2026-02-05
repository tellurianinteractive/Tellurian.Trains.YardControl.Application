namespace Tellurian.Trains.YardController.Model.Control;

public sealed record Point(int Number, int[] StraightAddresses, int[] DivergingAddresses, int LockAddressOffset);
