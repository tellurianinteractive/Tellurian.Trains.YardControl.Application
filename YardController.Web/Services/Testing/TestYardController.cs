using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services.Testing;

public sealed class TestYardController : IYardController
{
    private readonly List<PointCommand> _commands = new(50);
    private readonly List<SignalCommand> _signalCommands = new(50);

    public IReadOnlyList<PointCommand> Commands => _commands;
    public IReadOnlyList<SignalCommand> SignalCommands => _signalCommands;
    public void Clear() { _commands.Clear(); _signalCommands.Clear(); }

    public Task SendPointSetCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        _commands.Add(command);
        return Task.CompletedTask;
    }

    public Task SendPointLockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        if (command.AlsoLock) _commands.Add(command);
        return Task.CompletedTask;
    }

    public Task SendPointUnlockCommandsAsync(PointCommand command, CancellationToken cancellationToken)
    {
        if (command.AlsoUnlock) _commands.Add(command);
        return Task.CompletedTask;
    }

    public Task SendPointStateRequestAsync(int address, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task SendSignalCommandAsync(SignalCommand command, CancellationToken cancellationToken)
    {
        _signalCommands.Add(command);
        return Task.CompletedTask;
    }
}
