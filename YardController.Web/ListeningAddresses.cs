using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace YardController.Web;

/// <summary>
/// Reports where the yard control can be reached from a browser. Kestrel binds every network
/// interface (Kestrel:Endpoints in appsettings.json) so a tablet or a second computer on the
/// layout network can work the yard — but a wildcard address is not something an operator can
/// type, so every address that actually reaches this computer is written to the log instead.
/// </summary>
internal static class ListeningAddresses
{
    extension(WebApplication app)
    {
        /// <summary>
        /// Logs the browser URLs the application answers on, once the server has bound its ports.
        /// </summary>
        public void LogListeningAddresses() =>
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                var bound = app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
                var urls = bound.SelectMany(ReachableUrls).ToArray();
                // Guarded by IsEnabled, so the join only runs when the message will be written.
#pragma warning disable CA1873
                if (urls.Length > 0 && app.Logger.IsEnabled(LogLevel.Information)) StartupLog.ListeningOn(app.Logger, string.Join(", ", urls));
#pragma warning restore CA1873
            });
    }

    /// <summary>
    /// The URLs a browser can reach <paramref name="boundAddress"/> at. An address bound to a
    /// single host is already one of them; a wildcard is spelled out as localhost plus every
    /// non-loopback IPv4 address of this computer, which is what a second computer would use.
    /// </summary>
    private static IEnumerable<string> ReachableUrls(string boundAddress)
    {
        var host = boundAddress
            .Replace("*", IPAddress.Any.ToString(), StringComparison.Ordinal)
            .Replace("+", IPAddress.Any.ToString(), StringComparison.Ordinal);
        if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) || !IsWildcard(uri.Host))
        {
            return [boundAddress];
        }
        return [$"{uri.Scheme}://localhost:{uri.Port}",
            .. LocalAddresses().Select(address => $"{uri.Scheme}://{address}:{uri.Port}")];
    }

    private static bool IsWildcard(string host) =>
        IPAddress.TryParse(host.Trim('[', ']'), out var address)
        && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));

    private static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address));
}
