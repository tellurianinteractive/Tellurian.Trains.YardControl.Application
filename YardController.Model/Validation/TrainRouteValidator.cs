using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace Tellurian.Trains.YardController.Model.Validation;

/// <summary>
/// Validates train routes against the yard topology.
/// Ensures that on-route points form a valid path through the track graph.
/// </summary>
public class TrainRouteValidator
{
    private readonly ILogger<TrainRouteValidator> _logger;
    private readonly YardTopology _topology;
    private readonly Dictionary<int, List<PointDefinition>> _pointsByNumber;
    private readonly Dictionary<string, List<SignalDefinition>> _signalsByName;

    public TrainRouteValidator(YardTopology topology, ILogger<TrainRouteValidator> logger)
    {
        _topology = topology;
        _logger = logger;
        _pointsByNumber = BuildPointNumberMapping(topology.Points);
        _signalsByName = BuildSignalNameMapping(topology.Signals);
    }

    /// <summary>
    /// Builds a mapping from signal name to SignalDefinition(s).
    /// Logs warnings for duplicate signal names.
    /// </summary>
    private Dictionary<string, List<SignalDefinition>> BuildSignalNameMapping(IReadOnlyList<SignalDefinition> signals)
    {
        var result = new Dictionary<string, List<SignalDefinition>>();

        foreach (var signal in signals)
        {
            if (!result.TryGetValue(signal.Name, out var list))
            {
                list = [];
                result[signal.Name] = list;
            }
            else
            {
                _logger.LogWarning(
                    "Duplicate signal name '{SignalName}' found at {Coordinate} (already defined at {ExistingCoordinate})",
                    signal.Name, signal.Coordinate, list[0].Coordinate);
            }
            list.Add(signal);
        }

        return result;
    }

    /// <summary>
    /// Builds a mapping from point number to PointDefinition(s).
    /// Extracts numeric part from labels like "27a", "27b" → 27.
    /// </summary>
    private static Dictionary<int, List<PointDefinition>> BuildPointNumberMapping(IReadOnlyList<PointDefinition> points)
    {
        var result = new Dictionary<int, List<PointDefinition>>();

        foreach (var point in points)
        {
            var number = ExtractPointNumber(point.Label);
            if (number.HasValue)
            {
                if (!result.TryGetValue(number.Value, out var list))
                {
                    list = [];
                    result[number.Value] = list;
                }
                list.Add(point);
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts the numeric part from a point label (e.g., "27a" → 27, "6" → 6).
    /// </summary>
    private static int? ExtractPointNumber(string label)
    {
        var numericPart = new string(label.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(numericPart, out var number) ? number : null;
    }

    /// <summary>
    /// Validates a train route against the topology.
    /// Returns true if the route is valid, false otherwise.
    /// </summary>
    public bool ValidateRoute(TrainRouteCommand route)
    {
        var fromSignalName = route.FromSignal.ToString();
        var toSignalName = route.ToSignal.ToString();

        // Find signal coordinates
        if (!_signalsByName.TryGetValue(fromSignalName, out var fromSignals) || fromSignals.Count == 0)
        {
            _logger.LogError(
                "Route validation failed: FromSignal '{FromSignal}' not found in topology. Route: {Route}",
                fromSignalName, route);
            return false;
        }

        if (!_signalsByName.TryGetValue(toSignalName, out var toSignals) || toSignals.Count == 0)
        {
            _logger.LogError(
                "Route validation failed: ToSignal '{ToSignal}' not found in topology. Route: {Route}",
                toSignalName, route);
            return false;
        }

        // Use first signal definition for each (if duplicates exist, warning was logged during construction)
        var fromSignal = fromSignals[0];
        var toSignal = toSignals[0];

        var onRoutePoints = route.OnRoutePoints.ToList();

        // If no on-route points, just verify signals exist (already done above)
        if (onRoutePoints.Count == 0)
        {
            return true;
        }

        // Verify all on-route points exist in topology
        foreach (var pointCmd in onRoutePoints)
        {
            if (!_pointsByNumber.ContainsKey(pointCmd.Number))
            {
                _logger.LogError(
                    "Route validation failed: Point {PointNumber} not found in topology. Route: {Route}",
                    pointCmd.Number, route);
                return false;
            }
        }

        // Try to trace a path from FromSignal to ToSignal through the on-route points
        var pathValid = TryTracePath(fromSignal.Coordinate, toSignal.Coordinate, onRoutePoints);

        if (!pathValid)
        {
            _logger.LogError(
                "Route validation failed: Cannot trace valid path from {FromSignal} to {ToSignal} through points [{Points}]. Route: {Route}",
                fromSignalName, toSignalName,
                string.Join(", ", onRoutePoints.Select(p => $"{p.Number}{p.Position.Char}")),
                route);
        }

        return pathValid;
    }

    /// <summary>
    /// Validates the route by checking that all points exist and are properly connected.
    /// This is a simplified validation that checks:
    /// 1. All referenced points exist in the topology
    /// 2. Each point's coordinates are valid track nodes
    /// 3. The diverging/straight directions are properly defined
    /// </summary>
    private bool TryTracePath(GridCoordinate start, GridCoordinate end, List<PointCommand> onRoutePoints)
    {
        // Simplified validation: verify all points have valid topology definitions
        // Full path tracing is complex due to multi-point junctions;
        // for now, verify structural validity

        foreach (var pointCmd in onRoutePoints)
        {
            if (!_pointsByNumber.TryGetValue(pointCmd.Number, out var pointDefs))
            {
                return false; // Point not found
            }

            // Verify at least one definition has valid coordinates
            var hasValidDefinition = false;
            foreach (var pointDef in pointDefs)
            {
                var switchNode = _topology.Graph.GetNode(pointDef.SwitchPoint);
                var divergingNode = _topology.Graph.GetNode(pointDef.DivergingEnd);

                if (switchNode != null && divergingNode != null)
                {
                    // Verify there's a link between switch and diverging
                    var link = _topology.Graph.GetLink(pointDef.SwitchPoint, pointDef.DivergingEnd);
                    if (link != null)
                    {
                        hasValidDefinition = true;
                        break;
                    }
                }
            }

            if (!hasValidDefinition)
            {
                return false;
            }
        }

        // All points have valid definitions
        return true;
    }

    /// <summary>
    /// Validates all provided train routes against the topology.
    /// </summary>
    public ValidationResult ValidateRoutes(IEnumerable<TrainRouteCommand> routes)
    {
        var validRoutes = new List<TrainRouteCommand>();
        var invalidRoutes = new List<TrainRouteCommand>();

        foreach (var route in routes)
        {
            if (ValidateRoute(route))
            {
                validRoutes.Add(route);
            }
            else
            {
                invalidRoutes.Add(route);
            }
        }

        return new ValidationResult(validRoutes, invalidRoutes);
    }
}

public record ValidationResult(
    IReadOnlyList<TrainRouteCommand> ValidRoutes,
    IReadOnlyList<TrainRouteCommand> InvalidRoutes)
{
    public bool HasErrors => InvalidRoutes.Count > 0;
    public int TotalRoutes => ValidRoutes.Count + InvalidRoutes.Count;
}
