using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed class BufferedKeyReader : IBufferedKeyReader
{
    private readonly ConcurrentQueue<(ConsoleKeyInfo Key, string StationName, OperatorRole Role)> _keyQueue = new();

    public ConsoleKeyInfo ReadKey() =>
        _keyQueue.TryDequeue(out var entry) ? entry.Key : ConsoleKeyInfo.Empty;

    public (ConsoleKeyInfo Key, string StationName, OperatorRole Role) ReadKeyWithStation() =>
        _keyQueue.TryDequeue(out var entry) ? entry : (ConsoleKeyInfo.Empty, "", OperatorRole.Dispatcher);

    public bool KeyNotAvailable => _keyQueue.IsEmpty;

    public void EnqueueKey(ConsoleKeyInfo key) => _keyQueue.Enqueue((key, "", OperatorRole.Dispatcher));

    public void EnqueueKey(ConsoleKeyInfo key, string stationName, OperatorRole role = OperatorRole.Dispatcher) =>
        _keyQueue.Enqueue((key, stationName, role));

    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys)
    {
        foreach (var key in keys) _keyQueue.Enqueue((key, "", OperatorRole.Dispatcher));
    }

    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys, string stationName, OperatorRole role = OperatorRole.Dispatcher)
    {
        foreach (var key in keys) _keyQueue.Enqueue((key, stationName, role));
    }
}
