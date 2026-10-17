using HidSharp;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Tellurian.Trains.YardController.Model.Control;

namespace Tellurian.Trains.YardController.Panel.Hardware;

/// <summary>
/// The Stream Deck Module driven over its USB HID protocol, directly through HidSharp. The Elgato
/// Stream Deck application must not be running: it claims the device (§3.3).
/// <para>
/// The module is the XL family's board — eight keys by four, ninety-six pixels square — and speaks
/// that family's protocol, which is small enough to carry here rather than in a library:
/// </para>
/// <list type="bullet">
/// <item>A key picture is a JPEG, cut into output reports of 1024 bytes. Each starts
/// <c>02 07 key last lenLo lenHi seqLo seqHi</c> and carries up to 1016 bytes of the image.</item>
/// <item>Key states arrive as input reports (<c>01</c>), one byte per key from offset 4.</item>
/// <item>Brightness and the return to the logo are feature reports (<c>03 08 percent</c>,
/// <c>03 02</c>).</item>
/// </list>
/// <para>
/// Everything about the mounting is absorbed here. Callers number keys row-major from the top
/// left as the operator sees the panel and hand over pictures the right way up; whether that
/// means turning both round on the way to the wire is this class's business alone.
/// </para>
/// </summary>
public sealed partial class StreamDeckButtonPanel : IButtonPanel
{
    private const int VendorId = 0x0FD9;
    private const int ReportSize = 1024;
    private const int HeaderSize = 8;
    private const int InputReportLength = 512;
    private const int KeyReportOffset = 4;
    private const int FeatureReportLength = 32;
    private const int JpegQuality = 100;

    // The XL (0x006C), the XL v2 (0x008F) and the Module (0x00BA) share one protocol.
    private static readonly int[] ProductIds = [0x006C, 0x008F, 0x00BA];

    private readonly ILogger _logger;
    private readonly StreamDeckPanelOptions _options;
    private readonly Lock _gate = new();
    private readonly Timer _reconnect;
    private readonly bool[] _down = new bool[32];
    private HidStream? _stream;
    private bool _disposed;

    public StreamDeckButtonPanel(StreamDeckPanelOptions options, ILogger<StreamDeckButtonPanel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var period = TimeSpan.FromSeconds(Math.Max(1, options.ReconnectSeconds));
        _reconnect = new Timer(_ => TryConnect(), null, TimeSpan.Zero, period);
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate) return _stream is not null;
        }
    }

    public int Columns => 8;

    public int Rows => 4;

    public int KeySize => 96;

    public event Action<int, bool>? KeyStateChanged;

    public event Action<bool>? ConnectionChanged;

    public void SetKeyImage(int keyIndex, ReadOnlySpan<byte> bgr24)
    {
        var expected = KeySize * KeySize * 3;
        if (bgr24.Length != expected)
        {
            LogWrongImageSize(keyIndex, bgr24.Length, expected);
            return;
        }

        // The module shows its pictures a half turn from the way they are sent, so a picture is
        // turned once on the wire. Rotate180 is the mounting's own half turn on top of that, and
        // the two cancel: the default, a module mounted upside down, sends the pixels as given.
        var pixels = KeySize * KeySize;
        var turn = !_options.Rotate180;
        var bgra = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            var source = (turn ? pixels - 1 - i : i) * 3;
            var target = i * 4;
            bgra[target] = bgr24[source];
            bgra[target + 1] = bgr24[source + 1];
            bgra[target + 2] = bgr24[source + 2];
            bgra[target + 3] = 255;
        }

        Send(keyIndex, EncodeJpeg(bgra));
    }

    /// <summary>
    /// A key filled with one colour. The module has no fill-with-colour report, so this goes out
    /// as an image like any other — but a single-colour picture compresses to almost nothing, and
    /// a half turn of it changes nothing, so it is sent as it is.
    /// </summary>
    public void SetKeyColour(int keyIndex, byte red, byte green, byte blue)
    {
        var pixels = KeySize * KeySize;
        var bgra = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            bgra[i * 4] = blue;
            bgra[i * 4 + 1] = green;
            bgra[i * 4 + 2] = red;
            bgra[i * 4 + 3] = 255;
        }

        Send(keyIndex, EncodeJpeg(bgra));
    }

    public void SetBrightness(byte percent)
    {
        var report = new byte[FeatureReportLength];
        report[0] = 0x03;
        report[1] = 0x08;
        report[2] = Math.Clamp(percent, (byte)0, (byte)100);
        WriteFeature(report);
    }

    public void Clear()
    {
        for (var i = 0; i < Columns * Rows; i++) SetKeyColour(i, 0, 0, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reconnect.Dispose();

        // Leave the module showing its logo rather than the last key faces of a session that is over.
        var logo = new byte[FeatureReportLength];
        logo[0] = 0x03;
        logo[1] = 0x02;
        WriteFeature(logo);

        lock (_gate)
        {
            Detach();
        }
    }

    /// <summary>
    /// Keys as the operator sees them against keys as the module reports them. Inverting is a
    /// half turn of the grid, which for a flat row-major numbering is simply counting from the
    /// other end.
    /// </summary>
    private int ToHardwareKey(int keyIndex) =>
        _options.InvertKeys ? (Columns * Rows) - 1 - keyIndex : keyIndex;

    /// <summary>
    /// Four-four-four, not Skia's default of quartering the colour detail: key faces are small
    /// coloured type on a flat ground, and the default would smear its edges.
    /// </summary>
    private byte[] EncodeJpeg(byte[] bgra)
    {
        var info = new SKImageInfo(KeySize, KeySize, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var pixmap = new SKPixmap(info, System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(bgra, 0));
        using var output = new MemoryStream();
        using (var stream = new SKManagedWStream(output))
        {
            var options = new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore);
            pixmap.Encode(stream, options);
        }
        GC.KeepAlive(bgra);
        return output.ToArray();
    }

    private void Send(int keyIndex, byte[] jpeg)
    {
        if (keyIndex < 0 || keyIndex >= Columns * Rows) return;
        var key = (byte)ToHardwareKey(keyIndex);

        HidStream? lost = null;
        lock (_gate)
        {
            var stream = _stream;
            if (stream is null) return;
            try
            {
                var payload = ReportSize - HeaderSize;
                var sent = 0;
                for (var sequence = 0; sent < jpeg.Length; sequence++)
                {
                    var length = Math.Min(payload, jpeg.Length - sent);
                    var report = new byte[ReportSize];
                    report[0] = 0x02;
                    report[1] = 0x07;
                    report[2] = key;
                    report[3] = (byte)(sent + length >= jpeg.Length ? 1 : 0);
                    report[4] = (byte)(length & 0xFF);
                    report[5] = (byte)(length >> 8);
                    report[6] = (byte)(sequence & 0xFF);
                    report[7] = (byte)(sequence >> 8);
                    Array.Copy(jpeg, sent, report, HeaderSize, length);
                    stream.Write(report);
                    sent += length;
                }
            }
            catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException)
            {
                LogSendFailed(e.Message);
                lost = stream;
            }
        }

        if (lost is not null) Lose(lost);
    }

    private void WriteFeature(byte[] report)
    {
        HidStream? lost = null;
        lock (_gate)
        {
            var stream = _stream;
            if (stream is null) return;
            try
            {
                stream.SetFeature(report);
            }
            catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException)
            {
                LogSendFailed(e.Message);
                lost = stream;
            }
        }

        if (lost is not null) Lose(lost);
    }

    private void TryConnect()
    {
        if (_disposed) return;

        // What actually happened is decided inside the lock and reported outside it: raising an
        // event while holding the gate invites a subscriber to call back in and deadlock.
        var opened = false;

        lock (_gate)
        {
            if (_stream is not null) return;

            try
            {
                var device = DeviceList.Local.GetHidDevices()
                    .FirstOrDefault(d => d.VendorID == VendorId
                        && ProductIds.Contains(d.ProductID)
                        && (_options.DevicePath is not { Length: > 0 } path || d.DevicePath == path));

                // Ordinary: the module is not plugged in. Looked for again on the next tick, and
                // reported as nothing at all — an absent panel is not a panel arriving.
                if (device is null || !device.TryOpen(out var stream)) return;

                stream.ReadTimeout = Timeout.Infinite;
                Array.Clear(_down);
                _stream = stream;
                _ = Task.Factory.StartNew(() => ReadLoop(stream), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                opened = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogOpenFailed(e.Message);
            }
        }

        if (!opened) return;

        SetBrightness(Math.Clamp(_options.Brightness, (byte)0, (byte)100));
        LogConnected(Columns * Rows, _options.Rotate180, _options.InvertKeys);
        ConnectionChanged?.Invoke(true);
    }

    private void ReadLoop(HidStream stream)
    {
        var buffer = new byte[InputReportLength];
        try
        {
            while (true)
            {
                var read = stream.Read(buffer);
                if (read > KeyReportOffset && buffer[0] == 0x01) ProcessKeys(buffer, read);
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException)
        {
            // Unplugged, or closed by Dispose: either way this stream is finished.
        }

        Lose(stream);
    }

    private void ProcessKeys(byte[] report, int length)
    {
        var count = Math.Min(Columns * Rows, length - KeyReportOffset);
        for (var key = 0; key < count; key++)
        {
            var isDown = report[KeyReportOffset + key] != 0;
            if (_down[key] == isDown) continue;
            _down[key] = isDown;
            KeyStateChanged?.Invoke(ToHardwareKey(key), isDown);
        }
    }

    /// <summary>A stream that failed. Only the first caller for a given stream reports it.</summary>
    private void Lose(HidStream stream)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_stream, stream)) return;
            Detach();
        }

        if (_disposed) return;
        LogDisconnected();
        ConnectionChanged?.Invoke(false);
    }

    private void Detach()
    {
        if (_stream is null) return;
        var stream = _stream;
        _stream = null;
        try
        {
            stream.Dispose();
        }
        catch (IOException)
        {
            // Already gone with the cable.
        }
    }

    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "Stream Deck Module connected: {Keys} keys, images turned {Rotated}, keys counted from the far corner {Inverted}.")]
    private partial void LogConnected(int keys, bool rotated, bool inverted);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning, Message = "Stream Deck Module disconnected.")]
    private partial void LogDisconnected();

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning, Message = "Could not open the Stream Deck Module: {Reason}")]
    private partial void LogOpenFailed(string reason);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning, Message = "Could not send to the Stream Deck Module: {Reason}")]
    private partial void LogSendFailed(string reason);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Warning,
        Message = "Key {Key} image was {Actual} bytes, expected {Expected}.")]
    private partial void LogWrongImageSize(int key, int actual, int expected);
}
