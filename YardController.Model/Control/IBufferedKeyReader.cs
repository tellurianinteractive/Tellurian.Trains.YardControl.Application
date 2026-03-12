namespace Tellurian.Trains.YardController.Model.Control;

public interface IBufferedKeyReader : IKeyReader
{
    void EnqueueKey(ConsoleKeyInfo key);
    void EnqueueKey(ConsoleKeyInfo key, string stationName);
    void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys);
    void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys, string stationName);
    (ConsoleKeyInfo Key, string StationName) ReadKeyWithStation();
}
