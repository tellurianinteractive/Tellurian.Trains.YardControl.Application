namespace Tellurian.Trains.YardController;

public record TrainRouteCommand(int FromSignal, int ToSignal, TrainRouteState State, IEnumerable<PointCommand> PointCommands)
{
    /// <summary>
    /// Gets points that are part of the physical route.
    /// </summary>
    public IEnumerable<PointCommand> OnRoutePoints => PointCommands.Where(p => p.IsOnRoute);

    /// <summary>
    /// Gets points that are not on the route but are locked for flank protection.
    /// </summary>
    public IEnumerable<PointCommand> OffRoutePoints => PointCommands.Where(p => !p.IsOnRoute);

    public override string ToString() =>
        this.IsUndefined
        ? "Undefined"
        : FromSignal == 0 ? $"-{ToSignal}:{State}"
        : $"{FromSignal}-{ToSignal}: [{string.Join(" → ", PointCommands.Select(p => $"{(p.IsOnRoute ? "" : "x")}{p.Number}{p.Position.Char}"))}]";
};
