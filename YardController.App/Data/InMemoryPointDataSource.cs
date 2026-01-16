using Tellurian.Trains.YardController;

namespace Tellurian.Trains.YardController.Data;

public class InMemoryPointDataSource(ILogger<InMemoryPointDataSource> logger) : IPointDataSource
{
    private readonly ILogger<InMemoryPointDataSource> _logger = logger;
    private readonly List<Point> _points = new(100);
    private readonly List<TurntableTrack> _turntableTracks = new(50);

    public void AddPoint(Point point)
    {
        if (_points.Any(existing => point.Number == existing.Number)) throw new InvalidOperationException($"Point number {point.Number} already exists.");
        _points.Add(point);
    }

    public void AddPoint(int number, int[] addresses, int lockingAddressOffset = 0) => AddPoint(new Point(number, addresses, lockingAddressOffset));

    public void AddTurntableTrack(TurntableTrack track)
    {
        if (_turntableTracks.Any(existing => track.Number == existing.Number)) throw new InvalidOperationException($"Turntable track number {track.Number} already exists.");
        _turntableTracks.Add(track);
    }

    public Task<IEnumerable<Point>> GetPointsAsync(CancellationToken cancellationToken)
    {
        var lockAddressOffset = _points.Count != 0 ? _points.First().LockAddressOffset : 0;
        if (_points.SelectMany(p => p.Addresses).ToArray().IsAdressesAndLockAdressesOverlaping(lockAddressOffset))
        {
            if (_logger.IsEnabled(LogLevel.Error)) _logger.LogError("Point adresses and lock adresses overlap");
            return Task.FromResult(Enumerable.Empty<Point>());
        }
        return Task.FromResult(_points.AsEnumerable());
    }

    public Task<IEnumerable<TurntableTrack>> GetTurntableTracksAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_turntableTracks.AsEnumerable());
}
