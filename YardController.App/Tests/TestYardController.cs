namespace Tellurian.Trains.YardController.Tests;

public sealed class TestYardController : IYardController
{
    private readonly List<PointCommand> _commands = new(50);
    public Task SendPointCommandAsync(PointCommand command, CancellationToken cancellationToken)
    {
        _commands.Add(command);
        return Task.CompletedTask;
    }
    public IReadOnlyList<PointCommand> Commands => _commands;
    public void Clear() => _commands.Clear();
}
