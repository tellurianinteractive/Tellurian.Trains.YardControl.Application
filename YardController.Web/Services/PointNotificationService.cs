using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public sealed class PointNotificationService : IPointNotificationService
{
    public event Action<PointResult>? PointChanged;

    public void NotifyPointSet(PointCommand point, string message)
    {
        PointChanged?.Invoke(new PointResult(point.Number, PointResultType.Set, point, message));
    }

    public void NotifyPointRejected(int pointNumber, string reason)
    {
        PointChanged?.Invoke(new PointResult(pointNumber, PointResultType.Rejected, Message: reason));
    }

    public void NotifyPointLocked(PointCommand point, string reason)
    {
        PointChanged?.Invoke(new PointResult(point.Number, PointResultType.Locked, point, reason));
    }

    public void NotifyPointAlreadyInPosition(PointCommand point, string message)
    {
        PointChanged?.Invoke(new PointResult(point.Number, PointResultType.AlreadyInPosition, point, message));
    }
}
