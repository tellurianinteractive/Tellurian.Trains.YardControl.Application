namespace Tellurian.Trains.YardController;

public interface ISwitchDataSource
{
    Task<IEnumerable<Switch>> GetSwitchesAsync(CancellationToken cancellationToken);
}
