namespace YardController.Web.Hardware;

/// <summary>
/// Starts the command-station adapter's receive loop on application startup. The receive loop is
/// what connects the transport (serial or UDP), primes the notification pipeline, and — for the Z21
/// adapter — applies the configured broadcast subscription. Registered only in non-development
/// environments; dev mode uses logging-only stand-ins that need no hardware bootstrap.
/// </summary>
public sealed class CommandStationInitializer(
    Func<CancellationToken, Task> startReceiveAsync,
    ILogger<CommandStationInitializer> logger) : BackgroundService
{
    private readonly Func<CancellationToken, Task> _startReceiveAsync = startReceiveAsync;
    private readonly ILogger<CommandStationInitializer> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Starting command-station adapter receive loop");
            await _startReceiveAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command-station adapter receive loop failed");
            throw;
        }
    }
}
