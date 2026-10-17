namespace YardController.Web;

/// <summary>
/// Source-generated log messages for the application startup sequence in <c>Program.cs</c>,
/// which has no containing type of its own to hold them.
/// </summary>
internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Application starting in {Environment} environment")]
    public static partial void ApplicationStarting(ILogger logger, string environment);

    [LoggerMessage(Level = LogLevel.Information, Message = "Open the yard control in a browser at {Urls}")]
    public static partial void ListeningOn(ILogger logger, string urls);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Configured serial port '{PortName}' not found. Available ports: {AvailablePorts}")]
    public static partial void SerialPortNotFound(ILogger logger, string portName, string availablePorts);
}
