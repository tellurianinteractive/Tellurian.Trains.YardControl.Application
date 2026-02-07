using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public sealed class TrainRouteNotificationService : ITrainRouteNotificationService
{
    public event Action<TrainRouteResult>? RouteChanged;

    public void NotifyRouteSet(TrainRouteCommand route, string message)
    {
        RouteChanged?.Invoke(new TrainRouteResult(route, TrainRouteResultType.Set, message));
    }

    public void NotifyRouteRejected(TrainRouteCommand route, string reason)
    {
        RouteChanged?.Invoke(new TrainRouteResult(route, TrainRouteResultType.Rejected, reason));
    }

    public void NotifyRouteCleared(TrainRouteCommand route, string message)
    {
        RouteChanged?.Invoke(new TrainRouteResult(route, TrainRouteResultType.Cleared, message));
    }

    public void NotifyAllRoutesCleared(string message)
    {
        RouteChanged?.Invoke(new TrainRouteResult(
            new TrainRouteCommand(0, 0, TrainRouteState.Clear, []),
            TrainRouteResultType.AllCleared, message));
    }
}
