using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Development-mode implementation of <see cref="ISignalStateService"/>.
/// Tracks signal states by listening to <see cref="ISignalNotificationService"/> events.
/// </summary>
public sealed class LoggingSignalStateService : ISignalStateService, IDisposable
{
    private readonly ISignalNotificationService _signalNotifications;
    private readonly ConcurrentDictionary<int, SignalState> _states = new();

    public LoggingSignalStateService(ISignalNotificationService signalNotifications)
    {
        _signalNotifications = signalNotifications;
        _signalNotifications.SignalChanged += OnSignalChanged;
    }

    public event Action<SignalStateFeedback>? SignalStateChanged;

    public SignalState GetSignalState(int signalNumber) =>
        _states.TryGetValue(signalNumber, out var state) ? state : SignalState.Stop;

    public IReadOnlyDictionary<int, SignalState> GetAllSignalStates() =>
        _states.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    private void OnSignalChanged(SignalCommand command)
    {
        _states[command.SignalNumber] = command.State;
        SignalStateChanged?.Invoke(new SignalStateFeedback(command.SignalNumber, command.State));
    }

    public void Dispose()
    {
        _signalNotifications.SignalChanged -= OnSignalChanged;
    }
}
