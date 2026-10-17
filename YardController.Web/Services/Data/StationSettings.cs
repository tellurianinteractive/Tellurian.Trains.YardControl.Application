namespace YardController.Web.Services.Data;

/// <summary>
/// Which device a station is worked with. The two are mutually exclusive per station: a station
/// is configured for one or the other, and no attempt is made to reconcile input from both
/// (Specifications/InputDevices.md, principle 2).
/// </summary>
public enum InputDevice
{
    /// <summary>A standard USB numeric keypad. The default, and never a prerequisite.</summary>
    NumericKeypad = 0,

    /// <summary>An Elgato Stream Deck Module driven over its USB HID protocol.</summary>
    StreamDeck = 1,
}

public record StationConfig
{
    public string Name { get; init; } = "";
    public string DataFolder { get; init; } = "Data";

    /// <summary>
    /// The device this station is worked with. Absent means the numeric keypad, so a station file
    /// that has never heard of a panel keeps working exactly as it did.
    /// </summary>
    public InputDevice InputDevice { get; init; } = InputDevice.NumericKeypad;
}

public record StationSettings
{
    public System.Collections.ObjectModel.Collection<StationConfig> Stations { get; init; } = [];
}
