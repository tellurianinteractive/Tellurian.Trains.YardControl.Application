using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public sealed class TrainRouteNotificationService : ITrainRouteNotificationService
{
    public event Action<TrainRouteResult>? RouteChanged;

    public void NotifyRouteSet(string stationName, TrainRouteCommand route, string message)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.Set, message));
    }

    public void NotifyRouteRejected(string stationName, TrainRouteCommand route, string reason)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.Rejected, reason));
    }

    public void NotifyRouteCancelling(string stationName, TrainRouteCommand route, string message)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.Cancelling, message));
    }

    public void NotifyRouteCleared(string stationName, TrainRouteCommand route, string message)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.Cleared, message));
    }

    public void NotifyAllRoutesCancelling(string stationName, string message)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName,
            new TrainRouteCommand(0, 0, TrainRouteState.Clear, []),
            TrainRouteResultType.AllCancelling, message));
    }

    public void NotifyAllRoutesCleared(string stationName, string message)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName,
            new TrainRouteCommand(0, 0, TrainRouteState.Clear, []),
            TrainRouteResultType.AllCleared, message));
    }
}
