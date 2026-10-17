namespace Tellurian.Trains.YardController.Model.Control;

public interface ITrainRouteNotificationService
{
    event Action<TrainRouteResult>? RouteChanged;

    void NotifyRouteSet(string stationName, TrainRouteCommand route, string message, OperatorRole? audience = null);
    void NotifyRouteRejected(string stationName, TrainRouteCommand route, string reason, OperatorRole? audience = null);
    void NotifyRouteCancelling(string stationName, TrainRouteCommand route, string message, OperatorRole? audience = null);
    void NotifyRouteCleared(string stationName, TrainRouteCommand route, string message, OperatorRole? audience = null);
    void NotifyAllRoutesCancelling(string stationName, string message);
    void NotifyAllRoutesCleared(string stationName, string message);
    void NotifyRouteQueued(string stationName, TrainRouteCommand route, string message, string? trainNumber = null, OperatorRole? audience = null);
    void NotifyQueuedRouteCancelled(string stationName, TrainRouteCommand route, string message, OperatorRole? audience = null);
}

/// <param name="Audience">
/// Who the message is for; null addresses both. Clearing the whole yard has no audience of its
/// own — it takes down the shunter's routes as much as the dispatcher's, so both are told.
/// </param>
public record TrainRouteResult(
    string StationName,
    TrainRouteCommand Route,
    TrainRouteResultType ResultType,
    string? Message = null,
    string? TrainNumber = null,
    OperatorRole? Audience = null) : IOperatorMessage;

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
