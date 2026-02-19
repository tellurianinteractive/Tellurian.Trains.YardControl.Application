namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Service for notifying about individual point command results.
/// Used only for single point commands, not for points changed as part of train routes.
/// </summary>
public interface IPointNotificationService
{
    event Action<PointResult>? PointChanged;

    void NotifyPointSet(PointCommand point, string message);
    void NotifyPointRejected(int pointNumber, string reason);
    void NotifyPointLocked(PointCommand point, string reason);
    void NotifyPointAlreadyInPosition(PointCommand point, string message);
}

public record PointResult(
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
