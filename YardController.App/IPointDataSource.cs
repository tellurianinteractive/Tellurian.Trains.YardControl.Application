namespace Tellurian.Trains.YardController;

public interface IPointDataSource
{
    Task<IEnumerable<Point>> GetPointsAsync(CancellationToken cancellationToken);
    Task<IEnumerable<TurntableTrack>> GetTurntableTracksAsync(CancellationToken cancellationToken);
}
