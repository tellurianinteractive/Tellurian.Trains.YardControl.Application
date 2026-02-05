namespace Tellurian.Trains.YardController.Model.Control;

public interface ITrainRouteNotificationService
{
    event Action<TrainRouteResult>? RouteChanged;

    void NotifyRouteSet(TrainRouteCommand route);
    void NotifyRouteRejected(TrainRouteCommand route, string reason);
    void NotifyRouteCleared(TrainRouteCommand route);
    void NotifyAllRoutesCleared();
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
