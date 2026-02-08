namespace Tellurian.Trains.YardController.Model.Control;

public interface IPointPositionService
{
    event Action<PointPositionFeedback>? PositionChanged;
    PointPosition GetPosition(int pointNumber);
    PointPosition GetPosition(int pointNumber, char subPoint);
    IReadOnlyDictionary<int, PointPosition> GetAllPositions();
}

public record PointPositionFeedback(int PointNumber, PointPosition Position, char? SubPoint = null);
