using Tellurian.Trains.Communications.Interfaces.Accessories;

namespace Tellurian.Trains.YardController;

public static class PointPositionExtensions
{
    extension(PointPosition position)
    {
        public char Char => position switch
        {
            PointPosition.Straight => '+',
            PointPosition.Diverging => '-',
            _ => '?'
        };

        public PointPosition Opposite =>
            position == PointPosition.Straight ? PointPosition.Diverging :
            position == PointPosition.Diverging ? PointPosition.Straight :
            PointPosition.Undefined;

        public Position LocoNetPosition =>
            position == PointPosition.Straight ? Position.ClosedOrGreen :
            position == PointPosition.Diverging ? Position.ThrownOrRed :
            Position.ThrownOrRed;

        internal PointPosition WithAddressSignConsidered(short address) => address < 0 ? position.Opposite : position;

    }

    extension(char c)
    {
        public PointPosition PointPosition => c switch
        {
            '+' => PointPosition.Straight,
            '-' => PointPosition.Diverging,
            _ => PointPosition.Undefined
        };
    }
}
