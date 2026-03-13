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
    private readonly IYardDataService _yardDataService;
    private readonly ConcurrentDictionary<(string StationName, int SignalNumber), SignalState> _states = new();

    public LoggingSignalStateService(ISignalNotificationService signalNotifications, IYardDataService yardDataService)
    {
        _signalNotifications = signalNotifications;
        _yardDataService = yardDataService;
        _signalNotifications.SignalChanged += OnSignalChanged;
    }

    public event Action<SignalStateFeedback>? SignalStateChanged;

    public SignalState GetSignalState(string stationName, int signalNumber) =>
        _states.TryGetValue((stationName, signalNumber), out var state) ? state : SignalState.Stop;

    public IReadOnlyDictionary<int, SignalState> GetAllSignalStates(string stationName) =>
        _states
            .Where(kvp => kvp.Key.StationName.Equals(stationName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key.SignalNumber, kvp => kvp.Value);

    private void OnSignalChanged(SignalCommand command)
    {
        // Signal notifications don't carry station name, so find all stations with this signal
        foreach (var station in _yardDataService.AvailableStations)
        {
            var stationData = _yardDataService.GetStationData(station);
            if (stationData?.Signals.Any(s => int.TryParse(s.Name, out var n) && n == command.SignalNumber) == true)
            {
                _states[(station, command.SignalNumber)] = command.State;
                SignalStateChanged?.Invoke(new SignalStateFeedback(station, command.SignalNumber, command.State));
            }
        }
    }

    public void Dispose()
    {
        _signalNotifications.SignalChanged -= OnSignalChanged;
    }
}
