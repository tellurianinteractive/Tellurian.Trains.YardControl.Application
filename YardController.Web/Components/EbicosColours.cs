namespace YardController.Web.Components;

/// <summary>
/// The eight logical symbol colours defined by EBICOS
/// (Trafikverket TRVINFRA-00305 "Rangerstyrsystem" §6.1), plus the display background
/// and the two tones the alignment grid is drawn in.
/// The specification calls these logical colours rather than exact shades, but every
/// specimen image in chapter 6 uses these values, so they are adopted literally.
/// </summary>
public static class EbicosColours
{
    /// <summary>Free track, signal at stop — the normal state of everything.</summary>
    public const string Grey = "#969696";

    /// <summary>Part of a train route, signal showing proceed.</summary>
    public const string Green = "#008000";

    /// <summary>Part of a shunting route, and the object text of every signal.</summary>
    public const string Yellow = "#FFFF00";

    /// <summary>Occupied track, and the destination signal of an active route.</summary>
    public const string Red = "#FF0000";

    /// <summary>Route being released, and the object text of every point.</summary>
    public const string Magenta = "#FF3399";

    /// <summary>Part of a hump route.</summary>
    public const string Orange = "#FF6600";

    /// <summary>Part of a shunting area.</summary>
    public const string Blue = "#3366FF";

    /// <summary>Fault or out of control.</summary>
    public const string White = "#FFFFFF";

    /// <summary>
    /// The display background. The specification states black, with the note that it
    /// should be realised as a dark grey tone.
    /// </summary>
    public const string Background = "#1A1A1A";

    /// <summary>
    /// A passenger platform alongside the track. Outside the specification's palette — a
    /// platform is neither a track nor a symbol, it carries no state and never changes colour —
    /// so it is a shade darker than <see cref="Grey"/>, near enough to read as station
    /// furniture and far enough not to be mistaken for free track.
    /// </summary>
    public const string Platform = "#707070";

    /// <summary>
    /// The alignment grid overlay. Outside the specification's palette — the grid is a
    /// development aid, not part of the display — so it stays darker than every symbol
    /// colour and cannot be mistaken for one.
    /// </summary>
    public const string GridLine = "#333333";

    /// <summary>Row and column numbers on the alignment grid. See <see cref="GridLine"/>.</summary>
    public const string GridLabel = "#444444";
}
