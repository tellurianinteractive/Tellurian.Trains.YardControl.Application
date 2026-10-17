using Tellurian.Trains.YardController.Model.Control;

namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// The geometry and the interlocking of a plain crossing. A crossing has nothing on it that
/// moves, so everything about it is read off the points that lead into it: which way through it
/// is laid, which of its four arms are closed, and what has to be held clear while a movement
/// runs over it.
/// </summary>
public static class CrossingExtensions
{
    extension(YardTopology topology)
    {
        /// <summary>
        /// The links at the crossing that carry one way through it — normally two, one on either
        /// side. The way is named by a point rather than by coordinates, so the arms are found by
        /// stepping out of the crossing towards that point: the switch point of a crossover lies
        /// at the far end of the diagonal, and the arm is the first step along it.
        /// </summary>
        public IReadOnlyList<TrackLink> ArmsOf(CrossingDefinition crossing, CrossingWay way)
        {
            var arms = new List<TrackLink>();
            foreach (var point in topology.PointsNumbered(way.Governing.Number))
            {
                if (topology.Graph.ArmLink(crossing.Coordinate, point.SwitchPoint) is { } link && !arms.Contains(link))
                    arms.Add(link);
            }
            return arms;
        }

        /// <summary>
        /// The ways through every crossing that the given point commands lay. A command that puts
        /// a governing point where its way wants it lays that way, and with it the way's
        /// conditions; a command that puts it anywhere else lays nothing.
        /// </summary>
        public IEnumerable<(CrossingDefinition Crossing, CrossingWay Way)> WaysLaidBy(IEnumerable<PointCommand> pointCommands)
        {
            foreach (var crossing in topology.Crossings)
                foreach (var way in crossing.Ways)
                    if (pointCommands.Any(pc => pc.Number == way.Governing.Number && pc.Position == way.Governing.Position))
                        yield return (crossing, way);
        }

        /// <summary>
        /// What stands in the way of laying a way through a crossing: every condition of it that
        /// does not hold. A command that lays no way over any crossing is answered with nothing,
        /// and so is one whose crossings are all clear.
        /// </summary>
        /// <param name="pointCommand">The point command about to be given.</param>
        /// <param name="positionOf">Where a point stands now, by its number.</param>
        /// <returns>
        /// The conditions in the way, each with the crossing it belongs to and the position the
        /// offending point is actually in — <see cref="PointPosition.Undefined"/> where nothing
        /// has reported it yet, which is as much a reason to refuse as the wrong position is.
        /// </returns>
        public IEnumerable<(CrossingDefinition Crossing, PointCommand Condition, PointPosition Actual)> CrossingConditionsInTheWay(
            PointCommand pointCommand,
            Func<int, PointPosition> positionOf)
        {
            foreach (var (crossing, way) in topology.WaysLaidBy([pointCommand]))
            {
                foreach (var condition in way.Conditions)
                {
                    var actual = positionOf(condition.Number);
                    if (actual != condition.Position) yield return (crossing, condition, actual);
                }
            }
        }

        /// <summary>All point definitions carrying the given number, whatever sub-point letter they wear.</summary>
        internal IEnumerable<PointDefinition> PointsNumbered(int number) =>
            topology.Points.Where(point => point.Label.PointNumber() == number);
    }

    extension(string label)
    {
        /// <summary>The numeric part of a point label, so that "27a" and "27b" both answer 27.</summary>
        internal int PointNumber()
        {
            var digits = new string(label.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var number) ? number : 0;
        }
    }
}
