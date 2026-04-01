namespace Tellurian.Trains.YardController.Model.Control;

public interface ITrainRouteNotificationService
{
    event Action<TrainRouteResult>? RouteChanged;

    void NotifyRouteSet(string stationName, TrainRouteCommand route, string message);
    void NotifyRouteRejected(string stationName, TrainRouteCommand route, string reason);
    void NotifyRouteCancelling(string stationName, TrainRouteCommand route, string message);
    void NotifyRouteCleared(string stationName, TrainRouteCommand route, string message);
    void NotifyAllRoutesCancelling(string stationName, string message);
    void NotifyAllRoutesCleared(string stationName, string message);
    void NotifyRouteQueued(string stationName, TrainRouteCommand route, string message);
    void NotifyQueuedRouteCancelled(string stationName, TrainRouteCommand route, string message);
}

public record TrainRouteResult(
    string StationName,
    TrainRouteCommand Route,
    TrainRouteResultType ResultType,
    string? Message = null);

public enum TrainRouteResultType
{
    Set,
    Rejected,
    Cancelling,
    Cleared,
    AllCancelling,
    AllCleared,
    Queued,
    QueuedCancelled
}
