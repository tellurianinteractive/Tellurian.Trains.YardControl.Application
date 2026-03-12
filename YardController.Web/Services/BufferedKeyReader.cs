using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed class BufferedKeyReader : IBufferedKeyReader
{
    private readonly ConcurrentQueue<(ConsoleKeyInfo Key, string StationName)> _keyQueue = new();

    public ConsoleKeyInfo ReadKey() =>
        _keyQueue.TryDequeue(out var entry) ? entry.Key : ConsoleKeyInfo.Empty;

    public (ConsoleKeyInfo Key, string StationName) ReadKeyWithStation() =>
        _keyQueue.TryDequeue(out var entry) ? entry : (ConsoleKeyInfo.Empty, "");

    public bool KeyNotAvailable => _keyQueue.IsEmpty;

    public void EnqueueKey(ConsoleKeyInfo key) => _keyQueue.Enqueue((key, ""));

    public void EnqueueKey(ConsoleKeyInfo key, string stationName) => _keyQueue.Enqueue((key, stationName));

    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys)
    {
        foreach (var key in keys) _keyQueue.Enqueue((key, ""));
    }

    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys, string stationName)
    {
        foreach (var key in keys) _keyQueue.Enqueue((key, stationName));
    }
}
