namespace Tellurian.Trains.YardController.Model.Control.Extensions;

public static class ShuntingAreaExtensions
{
    extension(IEnumerable<ShuntingArea> areas)
    {
        /// <summary>
        /// Fills in hardware addresses and message kinds on each area's flank protection commands,
        /// the same way <see cref="TrainRouteCommandExtensions"/> does for route point commands.
        /// Points the shunter operates carry no position and therefore no addresses.
        /// </summary>
        public IEnumerable<ShuntingArea> UpdateFlankProtectionWithPointAddresses(IDictionary<int, Point> points)
        {
            foreach (var area in areas)
            {
                foreach (var pointCommand in area.FlankProtection)
                {
                    pointCommand.AddAddresses(points.AddressesFor(pointCommand.Number, pointCommand.Position));
                    pointCommand.MessageKinds ??= points.MessageKindsFor(pointCommand.Number);
                }
                yield return area;
            }
        }

        /// <summary>The points the shunter may move, taken from every area given.</summary>
        public HashSet<int> ShunterPoints() => [.. areas.SelectMany(area => area.ShunterPointNumbers)];
    }

    extension(TrainRouteCommand route)
    {
        /// <summary>
        /// True when every point the route sets is one a shunter owns, which is what makes the
        /// route the shunter's to set and to clear. Ownership spans every released area at once,
        /// so a route may run from one into the next. A point coupled to an owned point by a slave
        /// cascade moves with it and therefore counts as owned as well, while route flank
        /// protection must not pin a point the shunter is free to move.
        /// <para>
        /// A point a released area holds as flank protection counts as satisfied where the route
        /// asks for the position it is already held in: such a point is both the area's protection
        /// and part of the way through, and the route passes over it without altering it. Asking
        /// for the opposite position would break the protection and is refused.
        /// </para>
        /// </summary>
        /// <param name="releasedAreas">The shunting areas that are released to a shunter.</param>
        /// <param name="points">All points of the station, for resolving slave cascades.</param>
        public bool IsWithinShuntingAreas(IEnumerable<ShuntingArea> releasedAreas, IDictionary<int, Point> points) =>
            route.IsWithinShuntingAreas(releasedAreas, points, out _);

        /// <inheritdoc cref="IsWithinShuntingAreas(IEnumerable{ShuntingArea}, IDictionary{int, Point})"/>
        /// <param name="outsidePoint">
        /// The first point on the route that no released area accounts for, so the operator can be told
        /// which point put the route out of reach rather than only that something did. Null whenever the
        /// route fails for another reason — nothing is released, the route sets no points, its flank
        /// protection pins a point the shunter may move, or a slave cascade contradicts itself.
        /// </param>
        public bool IsWithinShuntingAreas(IEnumerable<ShuntingArea> releasedAreas, IDictionary<int, Point> points, out PointCommand? outsidePoint)
        {
            outsidePoint = null;
            var released = releasedAreas as IReadOnlyList<ShuntingArea> ?? [.. releasedAreas];
            var releasedPoints = released.ShunterPoints();
            if (releasedPoints.Count == 0) return false;
            var onRoutePoints = route.PointCommands.Where(pointCommand => pointCommand.IsOnRoute).ToList();
            if (onRoutePoints.Count == 0) return false;
            if (route.PointCommands.Any(pointCommand => !pointCommand.IsOnRoute && releasedPoints.Contains(pointCommand.Number))) return false;

            var owned = new HashSet<int>(releasedPoints);
            foreach (var pointCommand in onRoutePoints.Where(pointCommand => releasedPoints.Contains(pointCommand.Number)))
            {
                try
                {
                    foreach (var slave in pointCommand.ExpandWithSlaves(points).Skip(1)) owned.Add(slave.Number);
                }
                catch (InvalidPointCascadeException)
                {
                    return false;
                }
            }
            outsidePoint = onRoutePoints.FirstOrDefault(pointCommand =>
                !owned.Contains(pointCommand.Number) && !released.IsHeldAsFlankProtection(pointCommand));
            return outsidePoint is null;
        }
    }

    extension(IEnumerable<ShuntingArea> releasedAreas)
    {
        /// <summary>
        /// True when a released area already holds this very point in the very position asked for,
        /// so honouring the request moves nothing and leaves the protection standing.
        /// </summary>
        private bool IsHeldAsFlankProtection(PointCommand pointCommand) =>
            releasedAreas.SelectMany(area => area.FlankProtection)
                .Any(flank => flank.Number == pointCommand.Number && flank.Position == pointCommand.Position);
    }
}
