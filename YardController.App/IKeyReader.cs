namespace Tellurian.Trains.YardController;

public interface IKeyReader
{
    ConsoleKeyInfo ReadKey();
    bool KeyNotAvailable { get; }
}
