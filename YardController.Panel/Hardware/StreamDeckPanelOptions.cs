namespace Tellurian.Trains.YardController.Panel.Hardware;

/// <summary>
/// The handful of things about a Stream Deck Module that cannot be settled without the module in
/// front of you. All of them are mounting questions rather than protocol ones, which is why they
/// are configuration and not code.
/// </summary>
public sealed record StreamDeckPanelOptions
{
    /// <summary>
    /// Turns every key image a half turn. The module is an OEM board meant to be built into a
    /// panel, and Elgato document its images as going up rotated (§3.1) — but which way up a
    /// particular panel mounts it is the panel builder's decision, so this is a setting rather
    /// than an assumption.
    /// </summary>
    public bool Rotate180 { get; init; } = true;

    /// <summary>
    /// Counts keys from the opposite corner. A module mounted upside down reports the key at the
    /// operator's top left as the last key, not the first; turning the pictures without turning
    /// the numbering would put every command one press away from where it looks.
    /// </summary>
    public bool InvertKeys { get; init; } = true;

    /// <summary>Backlight brightness, nought to a hundred.</summary>
    public byte Brightness { get; init; } = 80;

    /// <summary>
    /// How often to look for a module that is not there yet. A panel may be plugged in after the
    /// application has started, and an operating session should not need a restart for it.
    /// </summary>
    public int ReconnectSeconds { get; init; } = 5;

    /// <summary>
    /// A particular device by its USB path, where more than one is attached. Null takes the first.
    /// </summary>
    public string? DevicePath { get; init; }
}
