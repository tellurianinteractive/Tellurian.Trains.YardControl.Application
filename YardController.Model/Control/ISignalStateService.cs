namespace Tellurian.Trains.YardController.Model.Control;

public interface ISignalStateService
{
    event Action<SignalStateFeedback>? SignalStateChanged;
    SignalState GetSignalState(string stationName, int signalNumber);
    IReadOnlyDictionary<int, SignalState> GetAllSignalStates(string stationName);
}

public record SignalStateFeedback(string StationName, int SignalNumber, SignalState State);
