namespace Tellurian.Trains.YardController;

public class ConsoleKeyReader : IKeyReader
{
    public ConsoleKeyInfo ReadKey() => Console.ReadKey(true);
    public bool KeyNotAvailable { get => !Console.KeyAvailable; }
}
