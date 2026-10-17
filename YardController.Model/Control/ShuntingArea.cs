namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Whether a shunting area is under the interlocking's control or handed over to a shunter.
/// </summary>
public enum ShuntingAreaState
{
    /// <summary>Normal state — the interlocking owns the points and draws them grey.</summary>
    Locked,

    /// <summary>
    /// Handed over — the shunter may move the area's points, which TRVINFRA-00305 6.2.12
    /// draws blue ("Ingår i växlingsområde", priority 9, above only the normal grey).
    /// </summary>
    Released
}

/// <summary>
/// A named group of points that can be handed over to a shunter as a unit.
/// </summary>
/// <param name="Number">Area identifier, used by the <c>A{area}+</c> / <c>A{area}-</c> commands.</param>
/// <param name="Name">Display name, translated through the station's [Translations] section.</param>
/// <param name="Points">
/// The area's points in declaration order. A point declared without a position suffix is one the
/// shunter operates; a point declared with <c>+</c> or <c>-</c> is flank protection, set to that
/// position and held locked for as long as the area is released.
/// </param>
public record ShuntingArea(int Number, string Name, IReadOnlyList<PointCommand> Points)
{
    /// <summary>The points the shunter may move — everything without a position suffix.</summary>
    public IEnumerable<int> ShunterPointNumbers =>
        Points.Where(p => p.Position == PointPosition.Undefined).Select(p => p.Number);

    /// <summary>The points held in a fixed position to protect the area.</summary>
    public IEnumerable<PointCommand> FlankProtection =>
        Points.Where(p => p.Position != PointPosition.Undefined);

    public override string ToString() => $"{Number}:{Name}";
}
