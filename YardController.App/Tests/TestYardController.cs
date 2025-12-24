namespace Tellurian.Trains.YardController.Tests;

public sealed class TestYardController : IYardController
{
    private readonly List<SwitchCommand> _commands = new(50);
    public Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken)
    {
        _commands.Add(command);
        return Task.CompletedTask;
    }
    public IReadOnlyList<SwitchCommand> Commands => _commands;
    public void Clear() => _commands.Clear();
}

