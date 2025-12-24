namespace Tellurian.Trains.YardController;

public record TrainRouteCommand(int FromSignal, int ToSignal, TrainRouteState State, IEnumerable<SwitchCommand> SwitchCommands)
{
    public override string ToString() =>
        this.IsUndefined
        ? "Undefined"
        : FromSignal == 0 ? $"-{ToSignal}:{State}"
        : $"{FromSignal}-{ToSignal}: [{string.Join(" → ", SwitchCommands.Select(s => $"{s.Number}{s.Direction.Char}"))}]";
};
