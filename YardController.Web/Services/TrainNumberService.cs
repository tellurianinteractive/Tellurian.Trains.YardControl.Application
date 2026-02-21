using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public sealed class TrainNumberService : ITrainNumberService
{
    private readonly Dictionary<int, string> _trainNumbers = new();

    public event Action<TrainNumberChanged>? TrainNumberChanged;

    public void AssignTrainNumber(int signalNumber, string trainNumber)
    {
        _trainNumbers[signalNumber] = trainNumber;
        TrainNumberChanged?.Invoke(new TrainNumberChanged(signalNumber, trainNumber, TrainNumberChangeType.Assigned));
    }

    public void RemoveTrainNumber(int signalNumber)
    {
        if (_trainNumbers.Remove(signalNumber))
            TrainNumberChanged?.Invoke(new TrainNumberChanged(signalNumber, null, TrainNumberChangeType.Removed));
    }

    public void MoveTrainNumber(int fromSignal, int toSignal)
    {
        if (_trainNumbers.Remove(fromSignal, out var trainNumber))
        {
            TrainNumberChanged?.Invoke(new TrainNumberChanged(fromSignal, null, TrainNumberChangeType.Removed));
            _trainNumbers[toSignal] = trainNumber;
            TrainNumberChanged?.Invoke(new TrainNumberChanged(toSignal, trainNumber, TrainNumberChangeType.Assigned));
        }
    }

    public string? GetTrainNumber(int signalNumber) =>
        _trainNumbers.TryGetValue(signalNumber, out var trainNumber) ? trainNumber : null;

    public void ClearAll()
    {
        _trainNumbers.Clear();
        TrainNumberChanged?.Invoke(new TrainNumberChanged(0, null, TrainNumberChangeType.AllCleared));
    }
}
