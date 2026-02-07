using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace Tellurian.Trains.YardController.Model.Validation;

/// <summary>
/// Validates train routes against the yard topology.
/// Ensures that a directed path exists between signals through the track graph.
/// </summary>
public class TrainRouteValidator
{
    private readonly ILogger<TrainRouteValidator> _logger;
    private readonly YardTopology _topology;
    private readonly Dictionary<string, List<SignalDefinition>> _signalsByName;

    public TrainRouteValidator(YardTopology topology, ILogger<TrainRouteValidator> logger)
    {
        _topology = topology;
        _logger = logger;
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

        // Try to trace a directed path from FromSignal to ToSignal
        var pathValid = _topology.Graph.FindRoutePath(fromSignal.Coordinate, toSignal.Coordinate, fromSignal.DrivesRight).Count > 0;

        if (!pathValid)
        {
            _logger.LogError(
                "Route validation failed: Cannot trace valid path from {FromSignal} ({FromCoord}) to {ToSignal} ({ToCoord}). Route: {Route}",
                fromSignalName, fromSignal.Coordinate, toSignalName, toSignal.Coordinate, route);
        }

        return pathValid;
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
