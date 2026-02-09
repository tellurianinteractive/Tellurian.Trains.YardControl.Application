using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public sealed class SignalNotificationService : ISignalNotificationService
{
    public event Action<SignalCommand>? SignalChanged;

    public void NotifySignalStateChanged(SignalCommand command)
    {
        SignalChanged?.Invoke(command);
    }
}
