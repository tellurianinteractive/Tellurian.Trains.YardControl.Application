namespace YardController.Web.Services.Data;

public sealed record CommandStationSettings
{
    public string Type { get; init; } = "Serial";
    public SerialPortSettings SerialPort { get; init; } = new();
    public Z21Settings Z21 { get; init; } = new();

    /// <summary>
    /// Minimum delay in milliseconds between consecutive accessory commands sent to the
    /// command station. Floored at 100 ms — lower values are clamped up to protect decoders
    /// and the command-station send queue.
    /// </summary>
    public int InterCommandDelayMs { get; init; } = 100;
}

public sealed record Z21Settings
{
    public string Address { get; init; } = "192.168.0.111";
    public int CommandPort { get; init; } = 21105;
    public int FeedbackPort { get; init; } = 21106;
}
