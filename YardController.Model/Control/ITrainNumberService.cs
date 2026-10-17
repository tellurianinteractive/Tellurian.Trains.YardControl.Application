namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Where every train number in the yard stands. A train number sits on a detection stretch, not at
/// a signal: a stretch is a piece of track a train can be on, and that is what the diagram draws
/// the number above. Stretch numbers restart at 1 in every station, so every call names one.
/// </summary>
public interface ITrainNumberService
{
    event Action<TrainNumberChanged>? TrainNumberChanged;
    void AssignTrainNumber(string stationName, int sectionNumber, string trainNumber);
    void RemoveTrainNumber(string stationName, int sectionNumber);
    void MoveTrainNumber(string stationName, int fromSection, int toSection);
    string? GetTrainNumber(string stationName, int sectionNumber);
    int? FindSectionByTrainNumber(string stationName, string trainNumber);

    /// <summary>Every train number standing in a station, by the stretch it stands on.</summary>
    IReadOnlyDictionary<int, string> GetTrainNumbers(string stationName);

    void ClearAll(string stationName);
}

public record TrainNumberChanged(string StationName, int SectionNumber, string? TrainNumber, TrainNumberChangeType ChangeType);

public enum TrainNumberChangeType { Assigned, Removed, AllCleared }
