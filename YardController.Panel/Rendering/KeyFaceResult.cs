namespace Tellurian.Trains.YardController.Panel.Rendering;

/// <summary>
/// One rendered key face, and what had to happen to its label to make it fit. The label
/// measurements are the point of the record: whether a word fits a 19 mm key is a question of
/// real font metrics, not of counting characters.
/// </summary>
/// <param name="Name">What was drawn — an icon name, a glyph, or empty for a blank key.</param>
/// <param name="Size">Edge of the square face in pixels.</param>
/// <param name="Bgr24">
/// The pixels, three bytes each in blue-green-red order, which is what a panel takes. Raw rather
/// than encoded because the encoding belongs to whatever the face is going to.
/// </param>
/// <param name="Label">The label composed under the icon, or null where the icon stands alone.</param>
/// <param name="LabelPixels">The height the label ended up at, in pixels.</param>
/// <param name="WasShrunk">True where the label had to be made smaller than nominal to fit the key.</param>
/// <param name="Fits">
/// False where fitting the label meant taking it below <see cref="KeyFaceOptions.MinimumLabelHeight"/>,
/// which is to say it fits the key but no longer reads across a room.
/// </param>
public sealed record KeyFaceResult(
    string Name,
    int Size,
    ReadOnlyMemory<byte> Bgr24,
    string? Label,
    float LabelPixels,
    bool WasShrunk,
    bool Fits);
