using Tellurian.Trains.Adapters.Z21;

namespace YardController.Web.Hardware;

/// <summary>
/// Keeps the Z21 broadcast subscription alive. The Z21 drops a client that has sent it nothing for
/// about a minute (Z21 LAN protocol §1.1, "Client Keep-Alive Requirements"), and the client's
/// broadcast flags go with it — so a yard standing idle between point commands quietly stops
/// receiving <c>LAN_X_TURNOUT_INFO</c> and detector broadcasts, and does not start again when the
/// next command re-registers it, because the re-registered client has no flags set.
/// <para>
/// The keep-alive is a re-send of <c>LAN_SET_BROADCASTFLAGS</c> rather than any other command: it
/// is both the activity that keeps the client registered and the thing that restores the
/// subscription if the Z21 has already forgotten it. UDP gives no delivery guarantee, so the
/// interval is half the timeout — a lost packet costs one missed renewal, not the subscription.
/// </para>
/// </summary>
public sealed partial class Z21KeepAliveService(
    Adapter adapter,
    ILogger<Z21KeepAliveService> logger) : BackgroundService
{
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(30);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The adapter is a singleton owned and disposed by the DI container; this service only borrows it.")]
    private readonly Adapter _adapter = adapter;
    private readonly ILogger<Z21KeepAliveService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RenewalInterval);
        LogKeepAliveStarted(RenewalInterval.TotalSeconds);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_adapter.CurrentSubscriptions == BroadcastSubjects.None) continue;
                await _adapter.SubscribeAsync(_adapter.CurrentSubscriptions, stoppingToken);
                LogSubscriptionRenewed(_adapter.CurrentSubscriptions);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Z21 keep-alive started, renewing the broadcast subscription every {Seconds} seconds")]
    private partial void LogKeepAliveStarted(double seconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Renewed Z21 broadcast subscription: {Subjects}")]
    private partial void LogSubscriptionRenewed(BroadcastSubjects subjects);
}
