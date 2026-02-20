namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// Type of signal as specified in the topology file.
/// </summary>
public enum SignalType
{
    /// <summary>No type specified.</summary>
    Default,
    /// <summary>Hidden signal (x) - not displayed in the UI.</summary>
    Hidden,
    /// <summary>Outbound main signal (u) - protects the main line leaving the station.</summary>
    OutboundMain,
    /// <summary>Inbound main signal (i) - protects the main line entering the station.</summary>
    InboundMain,
    /// <summary>Main dwarf signal (h).</summary>
    MainDwarf,
    /// <summary>Shunting dwarf signal (d).</summary>
    ShuntingDwarf
}
