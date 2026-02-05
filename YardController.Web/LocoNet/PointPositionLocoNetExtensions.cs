using Tellurian.Trains.Communications.Interfaces.Accessories;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.LocoNet;

public static class PointPositionLocoNetExtensions
{
    extension(PointPosition position)
    {
        public Position LocoNetPosition =>
            position == PointPosition.Straight ? Position.ClosedOrGreen :
            position == PointPosition.Diverging ? Position.ThrownOrRed :
            Position.ThrownOrRed;
    }
}
