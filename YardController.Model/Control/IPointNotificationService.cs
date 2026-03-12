namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Service for notifying about individual point command results.
/// Used only for single point commands, not for points changed as part of train routes.
/// </summary>
public interface IPointNotificationService
{
    event Action<PointResult>? PointChanged;

    void NotifyPointSet(string stationName, PointCommand point, string message);
    void NotifyPointRejected(string stationName, int pointNumber, string reason);
    void NotifyPointLocked(string stationName, PointCommand point, string reason);
    void NotifyPointAlreadyInPosition(string stationName, PointCommand point, string message);
}

public record PointResult(
    string StationName,
    int PointNumber,
    PointResultType ResultType,
    PointCommand? Point = null,
    string? Message = null);

public enum PointResultType
{
    Set,
    Rejected,
    Locked,
    AlreadyInPosition
}
