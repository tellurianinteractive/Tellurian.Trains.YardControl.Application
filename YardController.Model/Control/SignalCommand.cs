namespace Tellurian.Trains.YardController.Model.Control;

public sealed record SignalCommand(int SignalNumber, int Address, SignalState State)
{
    public bool HasAddress => Address > 0;
    public int? FeedbackAddress { get; init; }
}
