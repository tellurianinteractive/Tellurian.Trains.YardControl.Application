using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed class BufferedKeyReader : IBufferedKeyReader
{
    private readonly ConcurrentQueue<ConsoleKeyInfo> _keyQueue = new();

    public ConsoleKeyInfo ReadKey() =>
        _keyQueue.TryDequeue(out var key) ? key : ConsoleKeyInfo.Empty;

    public bool KeyNotAvailable => _keyQueue.IsEmpty;

    public void EnqueueKey(ConsoleKeyInfo key) => _keyQueue.Enqueue(key);

    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys)
    {
        foreach (var key in keys) _keyQueue.Enqueue(key);
    }
}
