using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services.Testing;

public class TestKeyReader() : IKeyReader
{
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    public void AddKey(char key) => _keys.Enqueue(new ConsoleKeyInfo(key, key.ConsoleKey, false, false, false));
    public ConsoleKeyInfo ReadKey() => _keys.Dequeue();
    public bool KeyNotAvailable { get => _keys.Count == 0; }
    public void Clear() => _keys.Clear();
}
