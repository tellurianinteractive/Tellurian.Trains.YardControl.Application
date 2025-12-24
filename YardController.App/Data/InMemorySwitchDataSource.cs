using Tellurian.Trains.YardController;

namespace Tellurian.Trains.YardController.Data;

public class InMemorySwitchDataSource() : ISwitchDataSource
{
    private readonly List<Switch> _switches = new(100);
    public void AddSwitch(Switch sw)
    {
        if (_switches.Any(existing => sw.Number == existing.Number)) throw new InvalidOperationException($"Switch number {sw.Number} already exists.");
        _switches.Add(sw);
    }

    public void AddSwitch(int number, int[] addresses) => AddSwitch(new Switch(number, addresses));

    public Task<IEnumerable<Switch>> GetSwitchesAsync(CancellationToken cancellationToken) => Task.FromResult(_switches.AsEnumerable());
}
