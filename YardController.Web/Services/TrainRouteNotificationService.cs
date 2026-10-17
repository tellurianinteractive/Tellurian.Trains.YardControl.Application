using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public sealed class TrainRouteNotificationService : ITrainRouteNotificationService
{
    public event Action<TrainRouteResult>? RouteChanged;

    public void NotifyRouteSet(string stationName, TrainRouteCommand route, string message, OperatorRole? audience = null)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.Set, message, Audience: audience));
    }

    public void NotifyRouteRejected(string stationName, TrainRouteCommand route, string reason, OperatorRole? audience = null)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.Rejected, reason, Audience: audience));
    }

    public void NotifyRouteCancelling(string stationName, TrainRouteCommand route, string message, OperatorRole? audience = null)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.Cancelling, message, Audience: audience));
    }

    public void NotifyRouteCleared(string stationName, TrainRouteCommand route, string message, OperatorRole? audience = null)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.Cleared, message, Audience: audience));
    }

    // Clearing every route at a stroke takes down the shunter's routes as much as the
    // dispatcher's, so both are told whichever of them asked for it.
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

    public void NotifyRouteQueued(string stationName, TrainRouteCommand route, string message, string? trainNumber = null, OperatorRole? audience = null)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.Queued, message, trainNumber, audience));
    }

    public void NotifyQueuedRouteCancelled(string stationName, TrainRouteCommand route, string message, OperatorRole? audience = null)
    {
        RouteChanged?.Invoke(new TrainRouteResult(stationName, route, TrainRouteResultType.QueuedCancelled, message, Audience: audience));
    }
}
