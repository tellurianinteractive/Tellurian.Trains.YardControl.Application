namespace Tellurian.Trains.YardController;

public interface IYardController
{
    Task SendSwitchCommandAsync(SwitchCommand command, CancellationToken cancellationToken);
}

