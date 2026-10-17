using System.Globalization;
using Tellurian.Trains.YardController.Model;

namespace YardController.Tests;

/// <summary>
/// The station files the application ships with, checked against themselves. Everything here is
/// something the parser cannot refuse — a stale coordinate or a mistyped signal number reads as
/// perfectly good syntax — and every one of them was found the hard way, on the diagram.
/// </summary>
[TestClass]
public class StationFileTests
{
    private static string StationPath(string fileName) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.Web", "Data", fileName));

    /// <summary>
    /// The detection stretches are deduced once and then kept, so a signal added or moved
    /// afterwards leaves them describing the yard as it used to be. A signal no stretch ends at
    /// is a signal nothing can say a train stands at: the route to it has no destination, and
    /// the train number that should move there has nowhere to go.
    /// </summary>
    [TestMethod]
    [DataRow("Munkeröd.txt")]
    [DataRow("Steinsnes.txt")]
    public void EverySignalHasADetectionStretchLeadingUpToIt(string fileName)
    {
        var station = new UnifiedStationParser().Parse(File.ReadAllText(StationPath(fileName)));
        Assert.IsNotEmpty(station.OccupancySections, $"{fileName} declares detection stretches");

        var ends = station.OccupancySections
            .SelectMany(section => new[] { section.From, section.To })
            .ToHashSet();
        var orphans = station.Topology.Signals
            .Where(signal => station.Topology.Graph.GetNode(signal.Coordinate) is not null)
            .Where(signal => !ends.Contains(signal.Coordinate))
            .ToList();

        foreach (var signal in orphans)
            Console.WriteLine($"Signal {signal.Name} at {signal.Coordinate} has no detection stretch ending there");

        Assert.IsEmpty(orphans,
            $"The [OccupancySections] of {fileName} no longer match its [Signals]; regenerate them");
    }

    /// <summary>
    /// A composite route is spelled as the sub-routes it is made of, and a sub-route that is not
    /// declared is silently left out rather than refused — the composite is still built, minus
    /// the points of the part that went missing. So every part has to exist, and no route may
    /// run from a signal to itself, which is what a mistyped signal number looks like.
    /// </summary>
    [TestMethod]
    [DataRow("Munkeröd.txt")]
    [DataRow("Steinsnes.txt")]
    public void EveryCompositeRouteIsMadeOfDeclaredSubRoutes(string fileName)
    {
        var routeLines = RouteLines(File.ReadAllLines(StationPath(fileName))).ToList();
        var declared = routeLines.Select(line => line.Pair).ToHashSet();
        var faults = new List<string>();

        foreach (var (pair, payload) in routeLines)
        {
            if (pair.From == pair.To) faults.Add($"Route {pair.From}-{pair.To} runs from a signal to itself");
            if (payload is null || !payload.Contains('.', StringComparison.Ordinal)) continue;

            var vias = payload.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (vias.Length < 2 || !vias.All(via => int.TryParse(via, NumberStyles.None, CultureInfo.InvariantCulture, out _))) continue;

            for (var i = 0; i < vias.Length - 1; i++)
            {
                var part = (From: int.Parse(vias[i], CultureInfo.InvariantCulture), To: int.Parse(vias[i + 1], CultureInfo.InvariantCulture));
                if (!declared.Contains(part))
                    faults.Add($"Route {pair.From}-{pair.To} is composed of {payload}, but {part.From}-{part.To} is not declared");
            }
        }

        foreach (var fault in faults) Console.WriteLine(fault);
        Assert.IsEmpty(faults, $"{fileName} has routes that cannot be built as written");
    }

    private static IEnumerable<((int From, int To) Pair, string? Payload)> RouteLines(IEnumerable<string> lines)
    {
        var inRoutes = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inRoutes = line.Equals("[Routes]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inRoutes || line.Length == 0 || line.StartsWith('\'')) continue;

            var withoutComment = line.Split('\'')[0].Trim();
            var withoutAddress = withoutComment.Split('@')[0].Trim();
            var parts = withoutAddress.Split(':', 2);
            var signals = parts[0].Split('-');
            if (signals.Length != 2) continue;
            if (!int.TryParse(signals[0], NumberStyles.None, CultureInfo.InvariantCulture, out var from)) continue;
            if (!int.TryParse(signals[1], NumberStyles.None, CultureInfo.InvariantCulture, out var to)) continue;

            yield return ((from, to), parts.Length > 1 ? parts[1].Trim() : null);
        }
    }
}
