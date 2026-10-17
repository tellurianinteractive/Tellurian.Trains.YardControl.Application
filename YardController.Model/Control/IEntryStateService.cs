namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Publishes a command as it is being entered, alongside the other notification services
/// (Specifications/InputDevices.md §8.1). Both input devices raise it: the panel knows the
/// command from the first press, and the keypad at least knows the characters, which is enough
/// for an operator to read their own typing back off the diagram.
/// </summary>
public interface IEntryStateService
{
    /// <summary>Raised on every keystroke, not only on commit.</summary>
    event Action<EntryState>? EntryChanged;

    /// <summary>The entry standing for a station, or an idle state where nothing is being typed.</summary>
    EntryState GetEntry(string stationName);

    void NotifyEntryChanged(EntryState state);

    void NotifyEntryCleared(string stationName);
}
