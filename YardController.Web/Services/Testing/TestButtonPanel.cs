using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services.Testing;

/// <summary>
/// A panel with no hardware behind it: presses are pushed in from a test, and the pictures that
/// would have gone to the device are kept so they can be looked at.
/// <para>
/// The null panel reports nothing and takes nothing, which is right for an application running
/// without a device but useless for testing the service that drives one. This is the other stand-in.
/// </para>
/// </summary>
public sealed class TestButtonPanel : IButtonPanel
{
    private readonly byte[]?[] _keys;

    public TestButtonPanel() => _keys = new byte[]?[Columns * Rows];

    public bool IsConnected { get; set; } = true;

    public int Columns => 8;

    public int Rows => 4;

    public int KeySize => 96;

    /// <summary>How many times each key has been painted, so a needless repaint can be spotted.</summary>
    public IReadOnlyList<int> PaintCount => _paintCount;
    private readonly int[] _paintCount = new int[32];

    public event Action<int, bool>? KeyStateChanged;

    public event Action<bool>? ConnectionChanged;

    /// <summary>Presses and releases a key, as the hardware would report it.</summary>
    public void Press(int keyIndex)
    {
        KeyStateChanged?.Invoke(keyIndex, true);
        KeyStateChanged?.Invoke(keyIndex, false);
    }

    /// <summary>The panel arriving or going away.</summary>
    public void SetConnected(bool connected)
    {
        IsConnected = connected;
        ConnectionChanged?.Invoke(connected);
    }

    /// <summary>The pixels last painted on a key, or null where it has never been painted.</summary>
    public ReadOnlySpan<byte> Painted(int keyIndex) => _keys[keyIndex];

    public void SetKeyImage(int keyIndex, ReadOnlySpan<byte> bgr24)
    {
        _keys[keyIndex] = bgr24.ToArray();
        _paintCount[keyIndex]++;
    }

    public void SetKeyColour(int keyIndex, byte red, byte green, byte blue)
    {
        _keys[keyIndex] = [blue, green, red];
        _paintCount[keyIndex]++;
    }

    public void SetBrightness(byte percent) { }

    public void Clear() => Array.Clear(_keys);

    public void Dispose() { }
}
