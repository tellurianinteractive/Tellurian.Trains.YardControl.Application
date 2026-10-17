namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Service for notifying about individual point command results.
/// Used only for single point commands, not for points changed as part of train routes.
/// </summary>
public interface IPointNotificationService
{
    event Action<PointResult>? PointChanged;

    void NotifyPointSet(string stationName, PointCommand point, string message, OperatorRole? audience = null);
    void NotifyPointRejected(string stationName, int pointNumber, string reason, OperatorRole? audience = null);
    void NotifyPointLocked(string stationName, PointCommand point, string reason, OperatorRole? audience = null);
    void NotifyPointAlreadyInPosition(string stationName, PointCommand point, string message, OperatorRole? audience = null);
}

/// <param name="Audience">
/// Who the message is for; null addresses both. A point moved by the hardware itself answers no
/// command and belongs to neither operator alone.
/// </param>
public record PointResult(
    string StationName,
    int PointNumber,
    PointResultType ResultType,
    PointCommand? Point = null,
    string? Message = null,
    OperatorRole? Audience = null) : IOperatorMessage;

public enum PointResultType
{
    Set,
    Rejected,
    Locked,
    AlreadyInPosition
}
