using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Hardware;

public static class PointPositionAccessoryExtensions
{
    extension(PointPosition position)
    {
        public Position AccessoryPosition =>
            position == PointPosition.Straight ? Position.ClosedOrGreen :
            position == PointPosition.Diverging ? Position.ThrownOrRed :
            Position.ThrownOrRed;
    }
}
