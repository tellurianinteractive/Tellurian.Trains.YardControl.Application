using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services.Testing;

public class TestKeyReader() : IBufferedKeyReader
{
    private readonly Queue<(ConsoleKeyInfo Key, string StationName, OperatorRole Role)> _keys = new();
    public void AddKey(char key) => AddKey(key, "", OperatorRole.Dispatcher);
    public void AddKey(char key, string stationName) => AddKey(key, stationName, OperatorRole.Dispatcher);
    public void AddKey(char key, string stationName, OperatorRole role) =>
        _keys.Enqueue((new ConsoleKeyInfo(key, key.ConsoleKey, false, false, false), stationName, role));
    public void AddKeys(string keys, string stationName = "", OperatorRole role = OperatorRole.Dispatcher)
    {
        foreach (var key in keys) AddKey(key, stationName, role);
    }
    public ConsoleKeyInfo ReadKey() => _keys.Count > 0 ? _keys.Dequeue().Key : ConsoleKeyInfo.Empty;
    public (ConsoleKeyInfo Key, string StationName, OperatorRole Role) ReadKeyWithStation() =>
        _keys.Count > 0 ? _keys.Dequeue() : (ConsoleKeyInfo.Empty, "", OperatorRole.Dispatcher);
    public bool KeyNotAvailable { get => _keys.Count == 0; }
    public void Clear() => _keys.Clear();

    public void EnqueueKey(ConsoleKeyInfo key) => _keys.Enqueue((key, "", OperatorRole.Dispatcher));
    public void EnqueueKey(ConsoleKeyInfo key, string stationName, OperatorRole role = OperatorRole.Dispatcher) =>
        _keys.Enqueue((key, stationName, role));
    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys)
    {
        foreach (var key in keys) _keys.Enqueue((key, "", OperatorRole.Dispatcher));
    }
    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys, string stationName, OperatorRole role = OperatorRole.Dispatcher)
    {
        foreach (var key in keys) _keys.Enqueue((key, stationName, role));
    }
}
