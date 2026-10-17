namespace Tellurian.Trains.YardController.Model.Control;

public interface IBufferedKeyReader : IKeyReader
{
    void EnqueueKey(ConsoleKeyInfo key);
    void EnqueueKey(ConsoleKeyInfo key, string stationName, OperatorRole role = OperatorRole.Dispatcher);
    void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys);
    void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys, string stationName, OperatorRole role = OperatorRole.Dispatcher);

    /// <summary>
    /// The next key together with the station it was typed for and the role that typed it.
    /// The role decides which commands are permitted, so it travels with every key.
    /// </summary>
    (ConsoleKeyInfo Key, string StationName, OperatorRole Role) ReadKeyWithStation();
}
