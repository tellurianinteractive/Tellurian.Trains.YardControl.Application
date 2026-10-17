using SkiaSharp;

namespace Tellurian.Trains.YardController.Panel.Rendering;

/// <summary>
/// How a key image is composed. The Stream Deck Module takes a 96 x 96 pixel key
/// (Specifications/InputDevices.md §3.1); everything else here is the layout contract the icons
/// in <c>YardController.Panel/Icons</c> are drawn to.
/// </summary>
public sealed record KeyFaceOptions
{
    /// <summary>Edge of the square key image in pixels. The module wants 96.</summary>
    public int Size { get; init; } = 96;

    /// <summary>
    /// The share of the key an icon keeps when a label is composed under it. Every icon is drawn
    /// for the whole square, so labelling one means scaling it down and pinning it to the top;
    /// without a label it keeps the whole key.
    /// </summary>
    public float IconZone { get; init; } = 0.63f;

    /// <summary>
    /// Nominal label height as a share of the key edge. 0.205 of 96 px is close to a 3 mm cap
    /// height on the module's ~19 mm key face, which is what reading one across a layout room
    /// wants.
    /// </summary>
    public float LabelHeight { get; init; } = 0.205f;

    /// <summary>
    /// The smallest label worth printing, as a share of the key edge. A label that has to be
    /// shrunk past this to fit is reported rather than quietly made unreadable.
    /// </summary>
    public float MinimumLabelHeight { get; init; } = 0.16f;

    /// <summary>Clear space either side of a label, as a share of the key edge.</summary>
    public float SideMargin { get; init; } = 0.05f;

    /// <summary>
    /// Height of a numeric-pad glyph as a share of the key edge. A digit is the whole point of
    /// that key, so it is set far larger than a label.
    /// </summary>
    public float GlyphHeight { get; init; } = 0.52f;

    /// <summary>
    /// Families to try for a label, most wanted first, asked for at <see cref="FontWidth"/>.
    /// These are typographic family names, not the names a font menu shows: a narrow cut is a
    /// width variant inside its family, so Arial Narrow is reached by asking for "Arial"
    /// condensed and never by asking for "Arial Narrow".
    /// <para>
    /// Bahnschrift is Windows' cut of DIN 1451, the face German road and railway signage is
    /// lettered in. It ships with the operating system and carries condensed widths, which makes
    /// it the one candidate here that is both apt and reliably present.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> FontFamilies { get; init; } =
        ["Barlow Semi Condensed", "Bahnschrift", "Arial", "Segoe UI"];

    /// <summary>
    /// Width to ask each family for. A condensed cut fits about a fifth more characters at the
    /// same cap height, which is the difference between the longest German label fitting a 19 mm
    /// key and not. A family with no condensed cut answers with what it has.
    /// </summary>
    public SKFontStyleWidth FontWidth { get; init; } = SKFontStyleWidth.Condensed;

    /// <summary>Weight to ask each family for. A key label is lettering, so it is set bold.</summary>
    public SKFontStyleWeight FontWeight { get; init; } = SKFontStyleWeight.Bold;

    /// <summary>
    /// The key ground. Pure black rather than the diagram's background: an unlit LCD in a bezel
    /// should read as off, where #1A1A1A would read as a lit grey rectangle.
    /// </summary>
    public SKColor Background { get; init; } = SKColors.Black;

    /// <summary>Label colour. White, because a label is lettering on a button, not an object on the diagram.</summary>
    public SKColor LabelColour { get; init; } = SKColors.White;

    /// <summary>
    /// The white hairline that announces the alternate button set on all sixteen keys at once
    /// (§5.3). A wrong-set press is the one new error the two sets can produce, so it is signalled
    /// redundantly.
    /// </summary>
    public bool AlternateHairline { get; init; }

    /// <summary>
    /// How much of full brightness a key keeps. Keys that cannot legally be pressed next are
    /// darkened rather than blanked (§10.2), so the operator can still read what they are.
    /// </summary>
    public float Dimming { get; init; } = 1f;

    /// <summary>
    /// The module is mounted so its images go up a half turn (§3.1). Off for files written to be
    /// looked at, on for pixels going to the device.
    /// </summary>
    public bool Rotate180 { get; init; }

    /// <summary>JPEG quality, where a face is being encoded to a file.</summary>
    public int JpegQuality { get; init; } = 92;

    /// <summary>Write PNG instead of JPEG. For inspection; the device takes raw pixels.</summary>
    public bool Png { get; init; }
}
