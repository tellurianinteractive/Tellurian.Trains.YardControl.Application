namespace Tellurian.Trains.YardController;

public interface IKeyReader
{
    ConsoleKeyInfo ReadKey();
    bool KeyNotAvailable { get; }
}

public class ConsoleKeyReader : IKeyReader
{
    public ConsoleKeyInfo ReadKey() => Console.ReadKey(true);
    public bool KeyNotAvailable { get => !Console.KeyAvailable; }
}

public class TestKeyReader() : IKeyReader
{
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    public void AddKey(char key) => _keys.Enqueue(new ConsoleKeyInfo(key, key.ConsoleKey, false, false, false));   
    public ConsoleKeyInfo ReadKey() => _keys.Dequeue();
    public bool KeyNotAvailable { get => _keys.Count == 0; }
    public void Clear() => _keys.Clear();
}
