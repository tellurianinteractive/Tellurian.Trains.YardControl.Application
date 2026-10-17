using System.Collections.Concurrent;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Publishes a command as it is being entered, keyed by station. Follows the same shape as the
/// other notification services: a singleton registered as itself and as its interface, raising an
/// event the components subscribe to in <c>OnInitialized</c>.
/// <para>
/// The last state per station is kept because a component may be created part-way through an
/// entry — a browser refreshed while the operator is halfway through typing a route should show
/// what is being typed, not an empty line.
/// </para>
/// </summary>
public sealed class EntryStateService : IEntryStateService
{
    private readonly ConcurrentDictionary<string, EntryState> _entries = new(StringComparer.OrdinalIgnoreCase);

    public event Action<EntryState>? EntryChanged;

    public EntryState GetEntry(string stationName) =>
        _entries.TryGetValue(stationName ?? "", out var state) ? state : EntryState.Idle(stationName ?? "");

    public void NotifyEntryChanged(EntryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _entries[state.StationName] = state;
        EntryChanged?.Invoke(state);
    }

    public void NotifyEntryCleared(string stationName)
    {
        // The button set survives a cleared entry: cancelling does not return the pad to the main
        // buttons (§5.3), so it is part of the panel's standing state rather than of the entry.
        // The role survives too, and for a different reason: each view shows only what its own
        // operator is typing, so a clear addressed to nobody would leave the abandoned characters
        // standing on the screen that was showing them.
        var previous = GetEntry(stationName);
        var idle = EntryState.Idle(stationName, previous.ButtonSet) with { Role = previous.Role };
        _entries[stationName] = idle;
        EntryChanged?.Invoke(idle);
    }
}
