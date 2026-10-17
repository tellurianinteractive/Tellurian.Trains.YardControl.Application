using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public sealed class PointNotificationService : IPointNotificationService
{
    public event Action<PointResult>? PointChanged;

    public void NotifyPointSet(string stationName, PointCommand point, string message, OperatorRole? audience = null)
    {
        PointChanged?.Invoke(new PointResult(stationName, point.Number, PointResultType.Set, point, message, audience));
    }

    public void NotifyPointRejected(string stationName, int pointNumber, string reason, OperatorRole? audience = null)
    {
        PointChanged?.Invoke(new PointResult(stationName, pointNumber, PointResultType.Rejected, Message: reason, Audience: audience));
    }

    public void NotifyPointLocked(string stationName, PointCommand point, string reason, OperatorRole? audience = null)
    {
        PointChanged?.Invoke(new PointResult(stationName, point.Number, PointResultType.Locked, point, reason, audience));
    }

    public void NotifyPointAlreadyInPosition(string stationName, PointCommand point, string message, OperatorRole? audience = null)
    {
        PointChanged?.Invoke(new PointResult(stationName, point.Number, PointResultType.AlreadyInPosition, point, message, audience));
    }
}
