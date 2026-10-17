namespace YardController.Web.Components;

/// <summary>
/// What a status message is about. EBICOS (Trafikverket TRVINFRA-00305 "Rangerstyrsystem"
/// §6.1–6.3) gives every object on the diagram a colour, and the status line borrows the colour
/// of the object its message regards, so the words and the picture say the same thing.
/// A refusal is the one exception: it is red whatever it regards, because a message saying
/// nothing happened must not read like one saying something did.
/// </summary>
public enum StatusSubject
{
    /// <summary>No message, only the standing hint. Grey, like free track.</summary>
    None,
    /// <summary>A train route. Green (§6.2.1).</summary>
    TrainRoute,
    /// <summary>A shunting route. Yellow (§6.2.1).</summary>
    ShuntingRoute,
    /// <summary>A route being released, or one that has just gone. Magenta (§6.2.1).</summary>
    Releasing,
    /// <summary>A point. Magenta, the colour of every point's object text (§6.3).</summary>
    Point,
    /// <summary>A shunting area. Blue (§6.2.12).</summary>
    ShuntingArea,
    /// <summary>A command that was refused.</summary>
    Refused
}
