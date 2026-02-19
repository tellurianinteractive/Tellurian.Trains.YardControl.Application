using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Development-mode implementation of <see cref="IPointPositionService"/>.
/// Tracks point positions by listening to point and train route notification events,
/// simulating LocoNet feedback when no hardware is connected.
/// When a point is commanded, also updates other points that share the same LocoNet addresses.
/// </summary>
public sealed class LoggingPointPositionService : IPointPositionService, IDisposable
{
    private readonly IPointNotificationService _pointNotifications;
    private readonly ITrainRouteNotificationService _routeNotifications;
    private readonly IYardDataService _yardDataService;
    private readonly ConcurrentDictionary<int, PointPosition> _positions = new();
    private readonly ConcurrentDictionary<(int PointNumber, char SubPoint), PointPosition> _subPointPositions = new();
    private Dictionary<int, List<(int PointNumber, bool Inverted, char? SubPoint)>> _addressMap = new();

    public LoggingPointPositionService(
        IPointNotificationService pointNotifications,
        ITrainRouteNotificationService routeNotifications,
        IYardDataService yardDataService)
    {
        _pointNotifications = pointNotifications;
        _routeNotifications = routeNotifications;
        _yardDataService = yardDataService;
        _pointNotifications.PointChanged += OnPointChanged;
        _routeNotifications.RouteChanged += OnRouteChanged;
        _yardDataService.DataChanged += OnDataChanged;
        BuildAddressMap();
    }

    public event Action<PointPositionFeedback>? PositionChanged;

    public PointPosition GetPosition(int pointNumber) =>
        _positions.TryGetValue(pointNumber, out var position) ? position : PointPosition.Undefined;

    public PointPosition GetPosition(int pointNumber, char subPoint) =>
        _subPointPositions.TryGetValue((pointNumber, subPoint), out var pos)
            ? pos : GetPosition(pointNumber);

    public IReadOnlyDictionary<int, PointPosition> GetAllPositions() =>
        _positions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    private void OnDataChanged(DataChangedEventArgs args)
    {
        BuildAddressMap();
    }

    private void BuildAddressMap()
    {
        var map = new Dictionary<int, List<(int PointNumber, bool Inverted, char? SubPoint)>>();

        foreach (var point in _yardDataService.Points.Where(p => !p.IsAddressOnly))
        {
            var straightAbsAddresses = new HashSet<int>(point.StraightAddresses.Select(Math.Abs));

            foreach (var address in point.StraightAddresses)
            {
                var absAddress = Math.Abs(address);
                var inverted = address < 0;
                char? subPoint = point.SubPointMap is not null && point.SubPointMap.TryGetValue(absAddress, out var sp) ? sp : null;
                if (!map.TryGetValue(absAddress, out var list))
                {
                    list = [];
                    map[absAddress] = list;
                }
                list.Add((point.Number, inverted, subPoint));
            }

            // Add diverging-only addresses with opposite inverted convention
            foreach (var address in point.DivergingAddresses)
            {
                var absAddress = Math.Abs(address);
                if (straightAbsAddresses.Contains(absAddress)) continue;
                var inverted = address > 0; // opposite of straight convention
                char? subPoint = point.SubPointMap is not null && point.SubPointMap.TryGetValue(absAddress, out var sp) ? sp : null;
                if (!map.TryGetValue(absAddress, out var list))
                {
                    list = [];
                    map[absAddress] = list;
                }
                list.Add((point.Number, inverted, subPoint));
            }
        }

        _addressMap = map;
    }

    private void OnPointChanged(PointResult result)
    {
        if (result.ResultType == PointResultType.Set && result.Point is { } point)
        {
            UpdatePosition(point.Number, point.Position);
            UpdateAffectedPoints(point);
        }
    }

    private void OnRouteChanged(TrainRouteResult result)
    {
        if (result.ResultType == TrainRouteResultType.Set)
        {
            foreach (var point in result.Route.PointCommands)
            {
                UpdatePosition(point.Number, point.Position);
                UpdateAffectedPoints(point);
            }
        }
    }

    /// <summary>
    /// Simulates LocoNet address-level feedback: for each address in the command,
    /// finds other points sharing that address and updates their position.
    /// </summary>
    private void UpdateAffectedPoints(PointCommand command)
    {
        foreach (var address in command.Addresses)
        {
            var absAddress = Math.Abs(address);
            // Determine if this address is being set to closed (green) or thrown (red).
            // Straight + positive address → closed; Straight + negative → thrown
            // Diverging + positive address → thrown; Diverging + negative → closed
            var isClosed = (command.Position == PointPosition.Straight) == (address > 0);

            if (_addressMap.TryGetValue(absAddress, out var mappings))
            {
                foreach (var mapping in mappings)
                {
                    // Skip same point unless it has a sub-point (sub-points of the same number get updated through address propagation)
                    if (mapping.PointNumber == command.Number && !mapping.SubPoint.HasValue) continue;

                    var position = isClosed
                        ? (mapping.Inverted ? PointPosition.Diverging : PointPosition.Straight)
                        : (mapping.Inverted ? PointPosition.Straight : PointPosition.Diverging);

                    if (mapping.SubPoint.HasValue)
                    {
                        _subPointPositions[(mapping.PointNumber, mapping.SubPoint.Value)] = position;
                        PositionChanged?.Invoke(new PointPositionFeedback(mapping.PointNumber, position, mapping.SubPoint));
                    }
                    else
                    {
                        UpdatePosition(mapping.PointNumber, position);
                    }
                }
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
        _yardDataService.DataChanged -= OnDataChanged;
    }
}
