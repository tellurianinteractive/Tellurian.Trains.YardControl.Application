namespace Tellurian.Trains.YardController.Model.Control;

public sealed record TurntableTrack(int Number, int Address);

public static class TurntableTrackExtensions
{
    extension(string command)
    {
        public PointPosition TurntableDirection => command.Length > 0 && command[0] == '-' ? PointPosition.Diverging : PointPosition.Straight;
    }
}
