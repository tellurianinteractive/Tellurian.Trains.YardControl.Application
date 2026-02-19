using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Resources;

internal static partial class Messages
{
    internal static string LocalizedPosition(PointPosition position) => position switch
    {
        PointPosition.Straight => PositionStraight,
        PointPosition.Diverging => PositionDiverging,
        _ => position.ToString()
    };
}
