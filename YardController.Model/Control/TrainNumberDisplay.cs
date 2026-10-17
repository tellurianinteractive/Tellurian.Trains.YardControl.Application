namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// How a train number is written on the diagram. Swedish train numbers run to five digits, and a
/// panel shows them all the same width so a column of them lines up and a glance can tell one
/// from another: leading zeroes are written out, and the face is monospaced.
/// </summary>
public static class TrainNumberDisplay
{
    /// <summary>The width every train number is padded to.</summary>
    public const int Digits = 5;

    /// <summary>
    /// The monospaced faces a browser is likely to have, most specific first. Kept here rather
    /// than in the component so the diagram and any other display agree on the face.
    /// </summary>
    public const string FontFamily = "ui-monospace, 'Cascadia Mono', Consolas, 'DejaVu Sans Mono', 'Liberation Mono', monospace";

    /// <summary>
    /// Pads a train number out to <see cref="Digits"/> with leading zeroes. A number already that
    /// long, or longer, is left as it is — truncating it would name a different train.
    /// </summary>
    public static string Format(string? trainNumber) =>
        string.IsNullOrEmpty(trainNumber) ? "" : trainNumber.PadLeft(Digits, '0');
}
