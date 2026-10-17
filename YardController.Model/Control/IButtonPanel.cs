namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// A grid of keys that are also displays: presses come in, pictures go out. The abstraction is
/// what keeps a particular USB device out of the rest of the application — a panel that is not
/// plugged in is a panel whose keys never report and whose pictures go nowhere, which is the same
/// shape as one that is (Specifications/InputDevices.md §4, §11.1).
/// </summary>
public interface IButtonPanel : IDisposable
{
    /// <summary>True while the hardware is attached and answering.</summary>
    bool IsConnected { get; }

    /// <summary>Keys across the panel. The Stream Deck Module has eight.</summary>
    int Columns { get; }

    /// <summary>Keys down the panel. The Stream Deck Module has four.</summary>
    int Rows { get; }

    /// <summary>Edge of one key image in pixels. The Stream Deck Module wants 96.</summary>
    int KeySize { get; }

    /// <summary>Keys on the panel, which is <see cref="Columns"/> times <see cref="Rows"/>.</summary>
    int KeyCount => Columns * Rows;

    /// <summary>
    /// A key going down or coming up, by index. Keys are numbered row-major from the top left of
    /// the panel as the operator sees it, so an inverted mounting is the panel's business and
    /// never the caller's.
    /// </summary>
    event Action<int, bool>? KeyStateChanged;

    /// <summary>The hardware arriving or going away. A panel may be plugged in after start-up.</summary>
    event Action<bool>? ConnectionChanged;

    /// <summary>
    /// Paints a key from raw pixels, three bytes per pixel in blue-green-red order and
    /// <see cref="KeySize"/> square. Raw pixels rather than an encoded image because the panel
    /// owns the encoding: what a device wants on the wire is the device's business.
    /// </summary>
    void SetKeyImage(int keyIndex, ReadOnlySpan<byte> bgr24);

    /// <summary>
    /// Fills a key with one colour. Preferred for state feedback: it costs one short feature
    /// report and no image encoding at all (§3.2), which is enough to say where a point stands or
    /// what a signal shows.
    /// </summary>
    void SetKeyColour(int keyIndex, byte red, byte green, byte blue);

    /// <summary>Backlight brightness, nought to a hundred.</summary>
    void SetBrightness(byte percent);

    /// <summary>Puts every key out. Used when the station is not being worked.</summary>
    void Clear();
}
