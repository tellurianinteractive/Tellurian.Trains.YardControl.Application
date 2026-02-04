using Tellurian.Trains.YardController.Extensions;

namespace Tellurian.Trains.YardController;

public static class PointExtensions
{
    extension(Point point)
    {
        public bool IsUndefined => point.Number == 0 ||
            (point.StraightAddresses.Length == 0 && point.DivergingAddresses.Length == 0);
    }

    extension(IDictionary<int, Point> points)
    {
        public int[] AddressesFor(int pointNumber, PointPosition position) =>
            points.TryGetValue(pointNumber, out var point)
                ? position == PointPosition.Straight ? point.StraightAddresses : point.DivergingAddresses
                : [];
    }

    extension(string? text)
    {
        public Point ToPoint()
        {

            if (text is null or { Length: < 2 }) return new Point(0, [], [], 0);
            var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2) goto invalidPoint;
            var number = parts[0].ToIntOrZero;
            if (number == 0) goto invalidPoint;
            var addresses = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => a.ToIntOrZero)
                .Where(a => a > 0)
                .ToArray();
            if (addresses.Length == 0) goto invalidPoint;
            // Backward compatible: same addresses for both positions
            return new Point(number, addresses, addresses, 0);

        invalidPoint:
            return new Point(0, [], [], 0);

        }
    }
}
