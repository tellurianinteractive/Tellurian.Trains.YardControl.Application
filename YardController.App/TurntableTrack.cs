namespace Tellurian.Trains.YardController;

public sealed record TurntableTrack(int Number, int Address);

internal static class TurntableTrackExtensions
{
    extension(string command)
    {
        public PointPosition TurntableDirection => command.Length > 0 && command[0] == '-' ? PointPosition.Diverging : PointPosition.Straight;
    }
}
