using Tellurian.Trains.YardController.Model.Control;

namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// Deduction of track detection stretches from the topology, and resolution of a declared
/// stretch back to the track links it covers.
/// </summary>
public static class OccupancySectionExtensions
{
    extension(YardTopology topology)
    {
        /// <summary>
        /// The coordinates a detection stretch may begin or end at: every signal coordinate and
        /// every coordinate at the edge of the layout, where the track leaves the station.
        /// Signals sharing a coordinate — an inbound and an outbound signal back to back, such
        /// as 22 and 71 at Munkeröd's 2.2 — count as one boundary, because the set holds
        /// coordinates rather than signals.
        /// </summary>
        public IReadOnlySet<GridCoordinate> OccupancySectionBoundaries
        {
            get
            {
                var graph = topology.Graph;
                var maxColumn = graph.MaxColumn;
                var boundaries = new HashSet<GridCoordinate>(
                    topology.Signals
                        .Select(signal => signal.Coordinate)
                        .Where(coordinate => graph.GetNode(coordinate) is not null));

                foreach (var coordinate in graph.Nodes.Keys)
                {
                    if (coordinate.Column == 0 || coordinate.Column == maxColumn)
                        boundaries.Add(coordinate);
                }

                return boundaries;
            }
        }

        /// <summary>
        /// Deduces a first set of detection stretches: one for every pair of boundary
        /// coordinates that can be reached from each other without passing a third one.
        /// Where the track branches, the same stretch of rail belongs to several sections —
        /// a train standing on the throat is on all of them — so overlapping sections are
        /// expected rather than a fault. The sections carry no address; that is for the person
        /// who knows what the detectors are wired to.
        /// </summary>
        public IReadOnlyList<OccupancySection> DeriveOccupancySections()
        {
            var graph = topology.Graph;
            var boundaries = topology.OccupancySectionBoundaries;
            var pairs = new HashSet<(GridCoordinate From, GridCoordinate To)>();

            foreach (var start in boundaries.Order())
            {
                foreach (var reached in ReachableBoundaries(graph, start, boundaries))
                    pairs.Add(start < reached ? (start, reached) : (reached, start));
            }

            return [.. pairs
                .OrderBy(pair => pair.From)
                .ThenBy(pair => pair.To)
                .Select((pair, index) => new OccupancySection(index + 1, pair.From, pair.To))];
        }

        /// <summary>
        /// Resolves every section to the track it covers, so the diagram can paint it, and
        /// notes which sections have their track to themselves. The station file records only
        /// the two ends, so the stretch between them is recovered as the shortest run that
        /// passes no other boundary coordinate. A hand-written section deliberately spanning
        /// several signals has no such run, and falls back to the plain shortest path.
        /// </summary>
        public IReadOnlyDictionary<int, OccupancySectionTrack> MapOccupancySectionTracks(
            IEnumerable<OccupancySection> sections)
        {
            var graph = topology.Graph;
            var boundaries = topology.OccupancySectionBoundaries;
            var links = new Dictionary<int, IReadOnlyList<TrackLink>>();

            foreach (var section in sections)
            {
                var sectionLinks = FindSectionLinks(graph, section, boundaries);
                if (sectionLinks.Count > 0) links[section.Number] = sectionLinks;
            }

            var coveringSections = new Dictionary<TrackLink, int>();
            foreach (var link in links.Values.SelectMany(sectionLinks => sectionLinks))
                coveringSections[link] = coveringSections.GetValueOrDefault(link) + 1;

            return links.ToDictionary(
                entry => entry.Key,
                entry => new OccupancySectionTrack(
                    entry.Value,
                    entry.Value.All(link => coveringSections[link] == 1)));
        }

        /// <summary>
        /// The arm links the points are currently turned away from: for every point standing in
        /// a known position, the link to the arm it is <i>not</i> set to. A point is the only
        /// thing in the yard that says which way a piece of track continues, so this is what
        /// tells one stretch through a throat from the several others sharing its rails —
        /// whether or not a route is set over them. A point whose position is unknown is turned
        /// away from nothing: not knowing where a point stands must never hide a train.
        /// </summary>
        /// <param name="positionOf">
        /// Where a point stands. Taken as a function of the definition rather than a lookup,
        /// because a paired point such as 1a/1b is one number with an arm each.
        /// </param>
        public IReadOnlySet<TrackLink> ArmsTurnedAway(Func<PointDefinition, PointPosition> positionOf)
        {
            var graph = topology.Graph;
            var turnedAway = new HashSet<TrackLink>();

            foreach (var point in topology.Points)
            {
                var arm = positionOf(point) switch
                {
                    PointPosition.Straight => graph.DeduceDivergingEnd(point),
                    PointPosition.Diverging => graph.DeduceStraightArm(point),
                    _ => (GridCoordinate?)null
                };
                if (arm is null) continue;
                if (graph.ArmLink(point.SwitchPoint, arm.Value) is { } link) turnedAway.Add(link);
            }

            return turnedAway;
        }
    }

    extension(OccupancySectionTrack track)
    {
        /// <summary>
        /// The part of the stretch the points still connect to one of its ends. A stretch the
        /// points set end to end is the whole of itself; one they are turned against loses the
        /// arm they turned away, and with it whatever that arm cuts off from both ends. What is
        /// left is the track a movement can still have reached, which is what the diagram has
        /// any business colouring.
        /// </summary>
        public IReadOnlyList<TrackLink> ConnectedPart(IReadOnlySet<TrackLink> armsTurnedAway)
        {
            var links = track.Links;
            if (armsTurnedAway.Count == 0 || !links.Any(armsTurnedAway.Contains)) return links;

            // The links run from one end of the stretch to the other, so the part still reachable
            // from an end is the run before the first arm turned away and the run after the last.
            // At least one link separates the two, so they never overlap.
            var fromOneEnd = links.TakeWhile(link => !armsTurnedAway.Contains(link));
            var fromTheOther = links.Reverse().TakeWhile(link => !armsTurnedAway.Contains(link)).Reverse();
            return [.. fromOneEnd, .. fromTheOther];
        }

        /// <summary>
        /// The straight runs the stretch is made of: the path along it, cut wherever it changes
        /// direction, so a track with a throat at either end comes back as three runs rather than
        /// as half a dozen links. The links are in path order but each is spelled left to right,
        /// so the path is walked coordinate by coordinate rather than read off their ends — a
        /// stretch doubling back through a throat, as Munkeröd's 7.10–1.12 does, is walked
        /// leftwards to begin with.
        /// </summary>
        public IReadOnlyList<(GridCoordinate From, GridCoordinate To)> StraightRuns
        {
            get
            {
                var links = track.Links;
                if (links.Count == 0) return [];

                var runs = new List<(GridCoordinate From, GridCoordinate To)>();
                var coordinate = PathStart(links);

                foreach (var link in links)
                {
                    var next = link.FromNode.Coordinate == coordinate ? link.ToNode.Coordinate : link.FromNode.Coordinate;
                    if (runs.Count > 0 && Continues(runs[^1], coordinate, next))
                        runs[^1] = (runs[^1].From, next);
                    else
                        runs.Add((coordinate, next));
                    coordinate = next;
                }

                return runs;
            }
        }

        /// <summary>
        /// The run a label belonging to the stretch is set along. Track runs level or at 45°, and
        /// most stretches run both in turn — a track with a throat at either end — so a label
        /// placed at the plain middle of a stretch can land on a slope, where it reads as
        /// belonging to neither of the tracks it slants between. So the label takes one run of the
        /// stretch: the longest level one where the stretch has any, and otherwise the longest
        /// slanting one, to be set along the rails at 45°. Munkeröd is what settles that order —
        /// 170 of its 171 stretches have a level run, the shortest of them a whole cell — so the
        /// slanting case is the exception it looks like. Runs of equal length are settled by
        /// whichever lies nearest the middle of the stretch, because the middle is where a label
        /// is looked for. Null for a stretch with no track under it.
        /// </summary>
        public (GridCoordinate From, GridCoordinate To)? LabelRun
        {
            get
            {
                var runs = track.StraightRuns;
                if (runs.Count == 0) return null;

                var middle = (runs.Min(run => Math.Min(run.From.Column, run.To.Column))
                    + runs.Max(run => Math.Max(run.From.Column, run.To.Column))) / 2.0;

                return runs
                    .OrderByDescending(run => run.From.Row == run.To.Row)
                    .ThenByDescending(run => Math.Abs(run.To.Column - run.From.Column))
                    .ThenBy(run => Math.Abs((run.From.Column + run.To.Column) / 2.0 - middle))
                    .First();
            }
        }
    }

    /// <summary>
    /// Where the path along a stretch begins: the end of its first link that its second one does
    /// not touch. A stretch of one link begins at that link's own left-hand end.
    /// </summary>
    private static GridCoordinate PathStart(IReadOnlyList<TrackLink> links) =>
        links.Count == 1 || Touches(links[1], links[0].ToNode.Coordinate)
            ? links[0].FromNode.Coordinate
            : links[0].ToNode.Coordinate;

    private static bool Touches(TrackLink link, GridCoordinate coordinate) =>
        link.FromNode.Coordinate == coordinate || link.ToNode.Coordinate == coordinate;

    /// <summary>
    /// Whether a step from <paramref name="from"/> to <paramref name="to"/> carries a run on: it
    /// does when it points the same way, which is parallel and not doubling back.
    /// </summary>
    private static bool Continues((GridCoordinate From, GridCoordinate To) run, GridCoordinate from, GridCoordinate to)
    {
        var (runRows, runColumns) = (run.To.Row - run.From.Row, run.To.Column - run.From.Column);
        var (stepRows, stepColumns) = (to.Row - from.Row, to.Column - from.Column);
        return runRows * stepColumns == runColumns * stepRows
            && runRows * stepRows + runColumns * stepColumns > 0;
    }

    /// <summary>
    /// The link by which an arm leaves its point. A slip's arm runs the length of a diagonal with
    /// coordinates of its own along it — Munkeröd's 8a reaches from 4.7 to 7.10 — so where the
    /// two ends are not neighbours the link taken is the first step towards the arm.
    /// </summary>

    /// <summary>
    /// Every boundary coordinate reachable from <paramref name="start"/> without passing
    /// another one, breadth-first so the first arrival is the nearest.
    /// </summary>
    private static IEnumerable<GridCoordinate> ReachableBoundaries(
        TrackGraph graph,
        GridCoordinate start,
        IReadOnlySet<GridCoordinate> boundaries)
    {
        var reached = new List<GridCoordinate>();
        var visited = new HashSet<GridCoordinate> { start };
        var queue = new Queue<GridCoordinate>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in graph.GetAdjacentCoordinates(current).Order())
            {
                if (!visited.Add(next)) continue;
                if (boundaries.Contains(next)) reached.Add(next);
                else queue.Enqueue(next);
            }
        }

        return reached;
    }

    private static IReadOnlyList<TrackLink> FindSectionLinks(
        TrackGraph graph,
        OccupancySection section,
        IReadOnlySet<GridCoordinate> boundaries)
    {
        var withinSection = FindShortestPath(graph, section.From, section.To, boundaries);
        return withinSection.Count > 0
            ? withinSection
            : FindShortestPath(graph, section.From, section.To, blocked: null);
    }

    /// <summary>
    /// Physically shortest path, Dijkstra weighted by Euclidean distance the way
    /// <see cref="TrackGraphExtensions.FindRoutePath"/> is, so a straight run beats a detour
    /// over a point that costs the same number of links. Coordinates in
    /// <paramref name="blocked"/> end the search unless they are the path's own ends.
    /// Neighbours are taken in coordinate order, so the same path comes back every time.
    /// </summary>
    private static IReadOnlyList<TrackLink> FindShortestPath(
        TrackGraph graph,
        GridCoordinate from,
        GridCoordinate to,
        IReadOnlySet<GridCoordinate>? blocked)
    {
        if (from == to || graph.GetNode(from) is null || graph.GetNode(to) is null) return [];

        var distances = new Dictionary<GridCoordinate, double> { [from] = 0 };
        var cameFrom = new Dictionary<GridCoordinate, GridCoordinate>();
        var settled = new HashSet<GridCoordinate>();
        var queue = new PriorityQueue<GridCoordinate, (double Distance, GridCoordinate Coordinate)>();
        queue.Enqueue(from, (0, from));

        while (queue.TryDequeue(out var current, out _))
        {
            if (!settled.Add(current)) continue;
            if (current == to) return BuildLinks(graph, cameFrom, from, to);
            if (blocked is not null && current != from && blocked.Contains(current)) continue;

            foreach (var next in graph.GetAdjacentCoordinates(current).Order())
            {
                if (settled.Contains(next)) continue;
                var candidate = distances[current] + DistanceBetween(current, next);
                if (distances.TryGetValue(next, out var best) && best <= candidate) continue;
                distances[next] = candidate;
                cameFrom[next] = current;
                queue.Enqueue(next, (candidate, next));
            }
        }

        return [];
    }

    private static double DistanceBetween(GridCoordinate from, GridCoordinate to)
    {
        double rows = from.Row - to.Row;
        double columns = from.Column - to.Column;
        return Math.Sqrt(rows * rows + columns * columns);
    }

    private static IReadOnlyList<TrackLink> BuildLinks(
        TrackGraph graph,
        Dictionary<GridCoordinate, GridCoordinate> cameFrom,
        GridCoordinate from,
        GridCoordinate to)
    {
        var links = new List<TrackLink>();
        var current = to;
        while (current != from)
        {
            var previous = cameFrom[current];
            var link = graph.GetLink(previous, current);
            if (link is null) return [];
            links.Add(link);
            current = previous;
        }
        links.Reverse();
        return links;
    }
}
