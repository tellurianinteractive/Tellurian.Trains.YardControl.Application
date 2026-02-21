namespace Tellurian.Trains.YardController.Model.Control;

public interface ITrainNumberService
{
    event Action<TrainNumberChanged>? TrainNumberChanged;
    void AssignTrainNumber(int signalNumber, string trainNumber);
    void RemoveTrainNumber(int signalNumber);
    void MoveTrainNumber(int fromSignal, int toSignal);
    string? GetTrainNumber(int signalNumber);
    void ClearAll();
}

public record TrainNumberChanged(int SignalNumber, string? TrainNumber, TrainNumberChangeType ChangeType);

public enum TrainNumberChangeType { Assigned, Removed, AllCleared }
