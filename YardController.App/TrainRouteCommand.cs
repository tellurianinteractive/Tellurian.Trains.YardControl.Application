namespace Tellurian.Trains.YardController;

public record TrainRouteCommand(int FromSignal, int ToSignal, TrainRouteState State, IEnumerable<PointCommand> PointCommands)
{
    public override string ToString() =>
        this.IsUndefined
        ? "Undefined"
        : FromSignal == 0 ? $"-{ToSignal}:{State}"
        : $"{FromSignal}-{ToSignal}: [{string.Join(" → ", PointCommands.Select(p => $"{p.Number}{p.Position.Char}"))}]";
};
