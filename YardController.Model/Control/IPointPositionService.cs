namespace Tellurian.Trains.YardController.Model.Control;

public interface IPointPositionService
{
    event Action<PointPositionFeedback>? PositionChanged;
    PointPosition GetPosition(string stationName, int pointNumber);
    PointPosition GetPosition(string stationName, int pointNumber, char subPoint);
    IReadOnlyDictionary<int, PointPosition> GetAllPositions(string stationName);
}

public record PointPositionFeedback(string StationName, int PointNumber, PointPosition Position, char? SubPoint = null);
