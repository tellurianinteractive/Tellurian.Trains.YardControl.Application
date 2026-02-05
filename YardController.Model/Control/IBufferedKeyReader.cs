namespace Tellurian.Trains.YardController.Model.Control;

public interface IBufferedKeyReader : IKeyReader
{
    void EnqueueKey(ConsoleKeyInfo key);
    void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys);
}
