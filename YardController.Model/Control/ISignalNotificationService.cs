namespace Tellurian.Trains.YardController.Model.Control;

public interface ISignalNotificationService
{
    event Action<SignalCommand>? SignalChanged;
    void NotifySignalStateChanged(SignalCommand command);
}
