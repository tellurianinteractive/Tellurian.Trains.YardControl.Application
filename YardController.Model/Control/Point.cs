namespace Tellurian.Trains.YardController.Model.Control;

public sealed record Point(
    int Number,
    int[] StraightAddresses,
    int[] DivergingAddresses,
    int LockAddressOffset,
    IReadOnlyDictionary<int, char>? SubPointMap = null,
    bool IsAddressOnly = false,
    bool IsHidden = false,
    IReadOnlyDictionary<int, AccessoryMessageKind>? MessageKinds = null,
    IReadOnlyList<SlaveCommand>? Slaves = null);

/// <summary>
/// A slave command that fires when a master point is set to <see cref="WhenMaster"/>.
/// Used to model coupled positions on single-slip points and similar constraints.
/// </summary>
public sealed record SlaveCommand(PointPosition WhenMaster, int Number, PointPosition Position);
