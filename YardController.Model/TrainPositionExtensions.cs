using System.Globalization;
using Tellurian.Trains.YardController.Model.Control;

namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// Where a train stands, expressed as a detection stretch. Nothing on the layout reports a train
/// number, so its position is deduced from the commands the dispatcher gives: a train follows the
/// route it was given, and each confirmation moves it one stretch further along that route.
/// </summary>
public static class TrainPositionExtensions
{
    extension(YardTopology topology)
    {
        /// <summary>
        /// The stretch leading up to a signal — the one a movement stands on while it waits at
        /// that signal, on the side it approaches from. Several stretches end at the same signal
        /// where the track branches, so an active route is asked first: only it can say which way
        /// through the points the movement came. Without one the shortest approach stretch wins,
        /// which is the track alongside the signal rather than a run across the whole throat.
        /// <para>
        /// Which side the movement approaches from is the movement's direction, not the signal's:
        /// <paramref name="travelDrivesRight"/> is what a route in hand answers, since a route
        /// runs past signals facing the other way and the train still comes up to those from
        /// behind. Only without a route does the signal's own facing stand in for it.
        /// </para>
        /// </summary>
        public int? SectionLeadingUpTo(
            int signalNumber,
            IReadOnlyList<OccupancySection> sections,
            IReadOnlyDictionary<int, OccupancySectionTrack>? sectionTracks = null,
            IReadOnlySet<TrackLink>? routeLinks = null,
            bool? travelDrivesRight = null)
        {
            if (topology.FindSignal(signalNumber) is not { } signal) return null;
            var drivesRight = travelDrivesRight ?? signal.DrivesRight;

            var approaching = sections
                .Where(section => (drivesRight ? section.To : section.From) == signal.Coordinate)
                .ToList();
            if (approaching.Count == 0) return null;

            if (routeLinks is { Count: > 0 } && sectionTracks is not null)
            {
                var onRoute = approaching
                    .Where(section => sectionTracks.TryGetValue(section.Number, out var track)
                        && track.Links.Count > 0
                        && track.Links.All(routeLinks.Contains))
                    .ToList();
                if (onRoute.Count > 0) approaching = onRoute;
            }

            return approaching
                .OrderBy(section => LinkCount(sectionTracks, section.Number))
                .ThenBy(section => section.Number)
                .First().Number;
        }

        /// <summary>
        /// The stretch on the far side of a signal that runs out to the edge of the station — the
        /// track a train stands on once it has passed the outbound signal but before the next
        /// station reports it arrived. Null at a signal nothing leaves the station through.
        /// </summary>
        public int? SectionBeyond(
            int signalNumber,
            IReadOnlyList<OccupancySection> sections,
            IReadOnlyDictionary<int, OccupancySectionTrack>? sectionTracks = null)
        {
            if (topology.FindSignal(signalNumber) is not { } signal) return null;
            var maxColumn = topology.MaxColumn;

            return sections
                .Where(section => (signal.DrivesRight ? section.From : section.To) == signal.Coordinate)
                .Where(section =>
                {
                    var far = signal.DrivesRight ? section.To : section.From;
                    return far.Column == 0 || far.Column == maxColumn;
                })
                .OrderBy(section => LinkCount(sectionTracks, section.Number))
                .ThenBy(section => section.Number)
                .Select(section => (int?)section.Number)
                .FirstOrDefault();
        }

        /// <summary>
        /// The stretch on the near side of a signal that runs in from the edge of the layout — the
        /// track a train stands on once the previous station has offered it but before it has been
        /// let into the station. The mirror of <c>SectionBeyond</c>, and null in the same way: a
        /// signal the track does not come into the station through has no such stretch, so an
        /// outbound signal never answers.
        /// </summary>
        public int? SectionFromEdge(
            int signalNumber,
            IReadOnlyList<OccupancySection> sections,
            IReadOnlyDictionary<int, OccupancySectionTrack>? sectionTracks = null)
        {
            if (topology.FindSignal(signalNumber) is not { } signal) return null;
            var maxColumn = topology.MaxColumn;

            return sections
                .Where(section => (signal.DrivesRight ? section.To : section.From) == signal.Coordinate)
                .Where(section =>
                {
                    var far = signal.DrivesRight ? section.From : section.To;
                    return far.Column == 0 || far.Column == maxColumn;
                })
                .OrderBy(section => LinkCount(sectionTracks, section.Number))
                .ThenBy(section => section.Number)
                .Select(section => (int?)section.Number)
                .FirstOrDefault();
        }

        /// <summary>
        /// The stretch a train on <paramref name="sectionNumber"/> departs onto: it stands at a
        /// signal, and beyond that signal the track runs out of the station.
        /// </summary>
        public int? DepartureSectionFrom(
            int sectionNumber,
            IReadOnlyList<OccupancySection> sections,
            IReadOnlyDictionary<int, OccupancySectionTrack>? sectionTracks = null)
        {
            if (sections.FirstOrDefault(section => section.Number == sectionNumber) is not { } section) return null;

            foreach (var signal in topology.Signals)
            {
                if ((signal.DrivesRight ? section.To : section.From) != signal.Coordinate) continue;
                if (!int.TryParse(signal.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) continue;
                if (topology.SectionBeyond(number, sections, sectionTracks) is { } beyond) return beyond;
            }
            return null;
        }

        /// <summary>
        /// The track a route covers, in the order a movement runs over it. The view answers the
        /// same question as a set of links; the order is what tells one stretch along the route
        /// from another.
        /// </summary>
        public IReadOnlyList<TrackLink> RouteLinksInOrder(TrainRouteCommand route)
        {
            if (topology.FindSignal(route.FromSignal) is not { } fromSignal) return [];
            if (topology.FindSignal(route.ToSignal) is not { } toSignal) return [];

            var waypoints = new List<SignalDefinition> { fromSignal };
            foreach (var intermediate in route.IntermediateSignals)
                if (topology.FindSignal(intermediate) is { } signal) waypoints.Add(signal);
            waypoints.Add(toSignal);

            var points = route.PointCommands.ToList();
            var path = new List<TrackLink>();
            for (var i = 0; i < waypoints.Count - 1; i++)
                path.AddRange(topology.Graph.FindRoutePath(
                    waypoints[i].Coordinate, waypoints[i + 1].Coordinate, waypoints[i].DrivesRight, topology.Points, points));
            return path;
        }

        /// <summary>
        /// Which way a movement runs over a route. A route is set for one movement, so its from
        /// signal answers for the whole of it — including the stretch at a signal facing the
        /// other way. Null where the from signal is not in the topology.
        /// </summary>
        public bool? RouteDrivesRight(TrainRouteCommand route) =>
            topology.FindSignal(route.FromSignal)?.DrivesRight;

        /// <summary>
        /// Whether a movement standing on <paramref name="currentSection"/> goes forward by moving
        /// to <paramref name="targetSection"/>. The target has to lie on the route and further
        /// along it than where the train stands. A train that has not yet passed its from signal
        /// stands on the approach stretch, which is behind the whole route, so every stretch on
        /// the route is ahead of it.
        /// </summary>
        public bool IsSectionAheadOnRoute(
            int currentSection,
            int targetSection,
            IReadOnlyList<TrackLink> orderedRouteLinks,
            IReadOnlyDictionary<int, OccupancySectionTrack> sectionTracks)
        {
            var target = topology.SectionPositionOnRoute(targetSection, orderedRouteLinks, sectionTracks);
            return target >= 0 && target > topology.SectionPositionOnRoute(currentSection, orderedRouteLinks, sectionTracks);
        }

        /// <summary>How far along a route a stretch reaches, or -1 when it is not on the route.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
            Justification = "Declared on the topology so it reads beside its siblings, SectionLeadingUpTo and SectionBeyond, and is called as topology.SectionPositionOnRoute(...).")]
        public int SectionPositionOnRoute(
            int sectionNumber,
            IReadOnlyList<TrackLink> orderedRouteLinks,
            IReadOnlyDictionary<int, OccupancySectionTrack> sectionTracks)
        {
            if (!sectionTracks.TryGetValue(sectionNumber, out var track)) return -1;
            var last = -1;
            for (var i = 0; i < orderedRouteLinks.Count; i++)
                if (track.Links.Contains(orderedRouteLinks[i])) last = i;
            return last;
        }

        private SignalDefinition? FindSignal(int signalNumber) =>
            topology.Signals.FirstOrDefault(signal =>
                int.TryParse(signal.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number == signalNumber);
    }

    extension(IReadOnlyDictionary<int, OccupancySectionTrack> sectionTracks)
    {
        /// <summary>
        /// The stretches a route runs over, one per run between consecutive boundaries. A stretch
        /// that spans several of those is dropped: the derived stretches overlap wherever the
        /// track branches, so keeping the long ones would write the train number over the same
        /// rails several times.
        /// </summary>
        public IReadOnlyList<int> SectionsAlongRoute(IReadOnlySet<TrackLink> routeLinks)
        {
            var covered = sectionTracks
                .Where(entry => entry.Value.Links.Count > 0 && entry.Value.Links.All(routeLinks.Contains))
                .ToList();

            return
            [
                .. covered
                    .Where(entry => !covered.Any(other =>
                        other.Value.Links.Count < entry.Value.Links.Count
                        && other.Value.Links.All(entry.Value.Links.Contains)))
                    .Select(entry => entry.Key)
                    .Order()
            ];
        }
    }

    private static int LinkCount(IReadOnlyDictionary<int, OccupancySectionTrack>? sectionTracks, int sectionNumber) =>
        sectionTracks is not null && sectionTracks.TryGetValue(sectionNumber, out var track) ? track.Links.Count : int.MaxValue;
}
