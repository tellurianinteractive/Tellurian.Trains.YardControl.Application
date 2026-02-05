namespace Tellurian.Trains.YardController.Model.Control;

public interface IKeyReader
{
    ConsoleKeyInfo ReadKey();
    bool KeyNotAvailable { get; }
}
