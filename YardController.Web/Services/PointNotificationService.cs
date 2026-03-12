using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public sealed class PointNotificationService : IPointNotificationService
{
    public event Action<PointResult>? PointChanged;

    public void NotifyPointSet(string stationName, PointCommand point, string message)
    {
        PointChanged?.Invoke(new PointResult(stationName, point.Number, PointResultType.Set, point, message));
    }

    public void NotifyPointRejected(string stationName, int pointNumber, string reason)
    {
        PointChanged?.Invoke(new PointResult(stationName, pointNumber, PointResultType.Rejected, Message: reason));
    }

    public void NotifyPointLocked(string stationName, PointCommand point, string reason)
    {
        PointChanged?.Invoke(new PointResult(stationName, point.Number, PointResultType.Locked, point, reason));
    }

    public void NotifyPointAlreadyInPosition(string stationName, PointCommand point, string message)
    {
        PointChanged?.Invoke(new PointResult(stationName, point.Number, PointResultType.AlreadyInPosition, point, message));
    }
}
