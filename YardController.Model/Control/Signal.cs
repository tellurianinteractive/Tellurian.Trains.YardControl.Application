namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Represents a signal with its control address and current state.
/// </summary>
/// <param name="Name">Signal name/identifier</param>
/// <param name="Address">LocoNet address for controlling the signal (0 if not configured)</param>
/// <param name="FeedbackAddress">Optional address for reading signal state feedback</param>
public sealed record Signal(string Name, int Address = 0, int? FeedbackAddress = null)
{
    /// <summary>
    /// Current state of the signal.
    /// </summary>
    public SignalState State { get; set; } = SignalState.Stop;

    /// <summary>
    /// Display coordinate from topology (set when combining with SignalDefinition).
    /// </summary>
    public GridCoordinate? Coordinate { get; set; }

    /// <summary>
    /// Driving direction from topology (set when combining with SignalDefinition).
    /// </summary>
    public bool DrivesRight { get; set; }

    /// <summary>
    /// Whether the signal should be visible in the UI.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Signal type from topology definition.
    /// </summary>
    public SignalType Type { get; init; } = SignalType.Default;

    /// <summary>
    /// Display text for the signal (label if set, otherwise name).
    /// </summary>
    public string DisplayText { get; set; } = "";

    /// <summary>
    /// Creates a Signal from a SignalDefinition with default control settings.
    /// </summary>
    public static Signal FromDefinition(SignalDefinition definition) => new(definition.Name)
    {
        Coordinate = definition.Coordinate,
        DrivesRight = definition.DrivesRight,
        IsVisible = definition.IsVisible,
        Type = definition.Type,
        DisplayText = definition.DisplayText
    };
}

/// <summary>
/// Signal state indicating stop or go (proceed).
/// </summary>
public enum SignalState
{
    /// <summary>Stop - signal shows red/halt aspect.</summary>
    Stop,
    /// <summary>Go - signal shows green/proceed aspect.</summary>
    Go
}
