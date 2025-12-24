using Tellurian.Trains.YardController;
using Tellurian.Trains.YardController.Extensions;

namespace Tellurian.Trains.YardController.Tests;

public class TestKeyReader() : IKeyReader
{
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    public void AddKey(char key) => _keys.Enqueue(new ConsoleKeyInfo(key, key.ConsoleKey, false, false, false));
    public ConsoleKeyInfo ReadKey() => _keys.Dequeue();
    public bool KeyNotAvailable { get => _keys.Count == 0; }
    public void Clear() => _keys.Clear();
}
