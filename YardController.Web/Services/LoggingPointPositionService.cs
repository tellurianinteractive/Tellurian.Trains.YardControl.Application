using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Development-mode implementation of <see cref="IPointPositionService"/>.
/// Tracks point positions by listening to point and train route notification events,
/// simulating LocoNet feedback when no hardware is connected.
/// </summary>
public sealed class LoggingPointPositionService : IPointPositionService, IDisposable
{
    private readonly IPointNotificationService _pointNotifications;
    private readonly ITrainRouteNotificationService _routeNotifications;
    private readonly ConcurrentDictionary<int, PointPosition> _positions = new();

    public LoggingPointPositionService(
        IPointNotificationService pointNotifications,
        ITrainRouteNotificationService routeNotifications)
    {
        _pointNotifications = pointNotifications;
        _routeNotifications = routeNotifications;
        _pointNotifications.PointChanged += OnPointChanged;
        _routeNotifications.RouteChanged += OnRouteChanged;
    }

    public event Action<PointPositionFeedback>? PositionChanged;

    public PointPosition GetPosition(int pointNumber) =>
        _positions.TryGetValue(pointNumber, out var position) ? position : PointPosition.Undefined;

    public IReadOnlyDictionary<int, PointPosition> GetAllPositions() =>
        _positions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    private void OnPointChanged(PointResult result)
    {
        if (result.ResultType == PointResultType.Set && result.Point is { } point)
        {
            UpdatePosition(point.Number, point.Position);
        }
    }

    private void OnRouteChanged(TrainRouteResult result)
    {
        if (result.ResultType == TrainRouteResultType.Set)
        {
            foreach (var point in result.Route.PointCommands)
            {
                UpdatePosition(point.Number, point.Position);
            }
        }
    }

    private void UpdatePosition(int pointNumber, PointPosition position)
    {
        _positions[pointNumber] = position;
        PositionChanged?.Invoke(new PointPositionFeedback(pointNumber, position));
    }

    public void Dispose()
    {
        _pointNotifications.PointChanged -= OnPointChanged;
        _routeNotifications.RouteChanged -= OnRouteChanged;
    }
}
