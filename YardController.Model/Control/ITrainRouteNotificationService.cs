namespace Tellurian.Trains.YardController.Model.Control;

public interface ITrainRouteNotificationService
{
    event Action<TrainRouteResult>? RouteChanged;

    void NotifyRouteSet(TrainRouteCommand route, string message);
    void NotifyRouteRejected(TrainRouteCommand route, string reason);
    void NotifyRouteCleared(TrainRouteCommand route, string message);
    void NotifyAllRoutesCleared(string message);
}

public record TrainRouteResult(
    TrainRouteCommand Route,
    TrainRouteResultType ResultType,
    string? Message = null);

public enum TrainRouteResultType
{
    Set,
    Rejected,
    Cleared,
    AllCleared
}
