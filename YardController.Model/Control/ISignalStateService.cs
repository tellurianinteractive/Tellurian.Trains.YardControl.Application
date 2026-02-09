namespace Tellurian.Trains.YardController.Model.Control;

public interface ISignalStateService
{
    event Action<SignalStateFeedback>? SignalStateChanged;
    SignalState GetSignalState(int signalNumber);
    IReadOnlyDictionary<int, SignalState> GetAllSignalStates();
}

public record SignalStateFeedback(int SignalNumber, SignalState State);
