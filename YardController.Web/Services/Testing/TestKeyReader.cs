using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services.Testing;

public class TestKeyReader() : IBufferedKeyReader
{
    private readonly Queue<(ConsoleKeyInfo Key, string StationName)> _keys = new();
    public void AddKey(char key) => _keys.Enqueue((new ConsoleKeyInfo(key, key.ConsoleKey, false, false, false), ""));
    public void AddKey(char key, string stationName) => _keys.Enqueue((new ConsoleKeyInfo(key, key.ConsoleKey, false, false, false), stationName));
    public ConsoleKeyInfo ReadKey() => _keys.Count > 0 ? _keys.Dequeue().Key : ConsoleKeyInfo.Empty;
    public (ConsoleKeyInfo Key, string StationName) ReadKeyWithStation() =>
        _keys.Count > 0 ? _keys.Dequeue() : (ConsoleKeyInfo.Empty, "");
    public bool KeyNotAvailable { get => _keys.Count == 0; }
    public void Clear() => _keys.Clear();

    public void EnqueueKey(ConsoleKeyInfo key) => _keys.Enqueue((key, ""));
    public void EnqueueKey(ConsoleKeyInfo key, string stationName) => _keys.Enqueue((key, stationName));
    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys)
    {
        foreach (var key in keys) _keys.Enqueue((key, ""));
    }
    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys, string stationName)
    {
        foreach (var key in keys) _keys.Enqueue((key, stationName));
    }
}
