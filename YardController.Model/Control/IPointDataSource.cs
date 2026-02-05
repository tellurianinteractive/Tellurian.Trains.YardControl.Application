namespace Tellurian.Trains.YardController.Model.Control;

public interface IPointDataSource
{
    Task<IEnumerable<Point>> GetPointsAsync(CancellationToken cancellationToken);
    Task<IEnumerable<TurntableTrack>> GetTurntableTracksAsync(CancellationToken cancellationToken);
}
