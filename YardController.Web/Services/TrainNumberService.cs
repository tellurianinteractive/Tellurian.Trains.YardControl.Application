using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public sealed class TrainNumberService : ITrainNumberService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _trainNumbers =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action<TrainNumberChanged>? TrainNumberChanged;

    public void AssignTrainNumber(string stationName, int sectionNumber, string trainNumber)
    {
        if (sectionNumber <= 0 || string.IsNullOrEmpty(trainNumber)) return;

        // A train stands in one place: taking the number in frees wherever it stood before, so a
        // mistyped command cannot leave the same train showing on two stretches at once.
        RemoveExisting(stationName, trainNumber, exceptSection: sectionNumber);

        ForStation(stationName)[sectionNumber] = trainNumber;
        TrainNumberChanged?.Invoke(new TrainNumberChanged(stationName, sectionNumber, trainNumber, TrainNumberChangeType.Assigned));
    }

    public void RemoveTrainNumber(string stationName, int sectionNumber)
    {
        if (ForStation(stationName).TryRemove(sectionNumber, out _))
            TrainNumberChanged?.Invoke(new TrainNumberChanged(stationName, sectionNumber, null, TrainNumberChangeType.Removed));
    }

    public void MoveTrainNumber(string stationName, int fromSection, int toSection)
    {
        if (fromSection == toSection || toSection <= 0) return;
        if (!ForStation(stationName).TryRemove(fromSection, out var trainNumber)) return;

        TrainNumberChanged?.Invoke(new TrainNumberChanged(stationName, fromSection, null, TrainNumberChangeType.Removed));
        ForStation(stationName)[toSection] = trainNumber;
        TrainNumberChanged?.Invoke(new TrainNumberChanged(stationName, toSection, trainNumber, TrainNumberChangeType.Assigned));
    }

    public string? GetTrainNumber(string stationName, int sectionNumber) =>
        ForStation(stationName).TryGetValue(sectionNumber, out var trainNumber) ? trainNumber : null;

    public int? FindSectionByTrainNumber(string stationName, string trainNumber)
    {
        foreach (var (section, number) in ForStation(stationName))
            if (number == trainNumber) return section;
        return null;
    }

    public IReadOnlyDictionary<int, string> GetTrainNumbers(string stationName) =>
        ForStation(stationName).ToDictionary(entry => entry.Key, entry => entry.Value);

    public void ClearAll(string stationName)
    {
        ForStation(stationName).Clear();
        TrainNumberChanged?.Invoke(new TrainNumberChanged(stationName, 0, null, TrainNumberChangeType.AllCleared));
    }

    private ConcurrentDictionary<int, string> ForStation(string stationName) =>
        _trainNumbers.GetOrAdd(stationName ?? "", _ => new ConcurrentDictionary<int, string>());

    private void RemoveExisting(string stationName, string trainNumber, int exceptSection)
    {
        foreach (var (section, number) in ForStation(stationName))
        {
            if (section == exceptSection || number != trainNumber) continue;
            if (ForStation(stationName).TryRemove(section, out _))
                TrainNumberChanged?.Invoke(new TrainNumberChanged(stationName, section, null, TrainNumberChangeType.Removed));
        }
    }
}
