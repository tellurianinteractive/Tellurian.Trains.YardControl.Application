using Tellurian.Trains.YardController.Model.Control;

namespace Tellurian.Trains.YardController.Panel.Hardware;

/// <summary>
/// A panel that is not there. Absence of the device must never prevent start-up
/// (Specifications/InputDevices.md §4) — a development machine, a build server or a cable that
/// was not plugged in are all ordinary — so the service that would drive one runs against this
/// instead, and the yard is worked from the diagram and the keypad as before.
/// </summary>
public sealed class NullButtonPanel : IButtonPanel
{
    public bool IsConnected => false;

    public int Columns => 8;

    public int Rows => 4;

    public int KeySize => 96;

#pragma warning disable CS0067 // Nothing raises these: a panel that is not there reports nothing.
    public event Action<int, bool>? KeyStateChanged;

    public event Action<bool>? ConnectionChanged;
#pragma warning restore CS0067

    public void SetKeyImage(int keyIndex, ReadOnlySpan<byte> bgr24) { }

    public void SetKeyColour(int keyIndex, byte red, byte green, byte blue) { }

    public void SetBrightness(byte percent) { }

    public void Clear() { }

    public void Dispose() { }
}
