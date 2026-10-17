namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// A command as far as it has been entered. Published on every keystroke rather than on commit
/// (Specifications/InputDevices.md §8), which is what lets the yard view show a command being
/// typed — the pending action in words, the raw characters for a keypad operator, and the signals
/// still matching the digits so far blinking on the diagram.
/// <para>
/// The record carries structure, not prose: <see cref="PendingText"/> is composed by whoever
/// raises the state, because naming a route in the operator's language is the web layer's work
/// and the model has no resources.
/// </para>
/// </summary>
/// <param name="StationName">The station being worked.</param>
/// <param name="Role">Who is entering, since that decides what is permitted.</param>
/// <param name="Command">
/// The command chosen, or <see cref="PanelCommand.None"/> for postfix keypad entry — where the
/// command is not known until the terminator arrives, which is the whole reason the panel's
/// grammar is command-first.
/// </param>
/// <param name="ButtonSet">Which set of buttons is showing.</param>
/// <param name="Parameters">The parameters settled so far.</param>
/// <param name="Current">The digits of the parameter being typed, which may still grow.</param>
/// <param name="IsWildcard">True where the wildcard was pressed in place of a number.</param>
/// <param name="Candidates">
/// The numbers still matching <see cref="Current"/> — signals, points or areas depending on what
/// the command asks for. Empty once the number has settled.
/// </param>
/// <param name="IsComplete">True where the entry is a whole, valid command and may be sent.</param>
/// <param name="RawKeys">The literal characters entered, for an operator reading their own typing.</param>
/// <param name="PendingText">What is about to happen, in the operator's language.</param>
public sealed record EntryState(
    string StationName,
    OperatorRole Role,
    PanelCommand Command,
    ButtonSet ButtonSet,
    IReadOnlyList<string> Parameters,
    string Current,
    bool IsWildcard,
    IReadOnlyList<int> Candidates,
    bool IsComplete,
    string RawKeys,
    string? PendingText)
{
    /// <summary>
    /// Why the last press was refused, in the operator's language, or null where nothing was.
    /// It rides along with the entry rather than travelling by itself: a refusal is something that
    /// happened to an entry, and the two are read on the same line.
    /// </summary>
    public string? Rejection { get; init; }

    /// <summary>Nothing being entered: the resting state, and what a cleared entry becomes.</summary>
    public static EntryState Idle(string stationName, ButtonSet buttonSet = ButtonSet.Main) =>
        new(stationName, OperatorRole.Dispatcher, PanelCommand.None, buttonSet,
            [], "", false, [], false, "", null);

    /// <summary>True where nothing has been entered at all.</summary>
    public bool IsIdle => Command == PanelCommand.None && Parameters.Count == 0
        && Current.Length == 0 && !IsWildcard && RawKeys.Length == 0;

    /// <summary>
    /// The parameters as they would be passed on, the one being typed included. A parameter still
    /// being typed counts: an operator reading the diagram wants to see the number they are
    /// halfway through, and the display asks nothing of its completeness.
    /// </summary>
    public IReadOnlyList<string> AllParameters =>
        Current.Length == 0 ? Parameters : [.. Parameters, Current];

    /// <summary>
    /// The one line shown under the diagram (§8.2).
    /// <para>
    /// It is one or the other, never both. A panel entry knows its command from the first press
    /// and says what is about to happen in words, parameters included — appending the raw
    /// characters to that would print the same digits a second time. A keypad entry has no command
    /// to name until its terminator arrives, so the characters it has collected are the whole of
    /// what can be shown, which is what §8.2 asks for on a keypad's behalf.
    /// </para>
    /// </summary>
    public string DisplayText =>
        IsIdle ? ""
        : PendingText is { Length: > 0 } pending ? pending
        : Readable(RawKeys);

    /// <summary>
    /// The characters as an operator can read them back. Two of the keypad's terminators are
    /// control characters, which would otherwise leave the line looking as though nothing had been
    /// typed at all.
    /// </summary>
    private static string Readable(string keys) => keys
        .Replace("\u001b", "ESC", StringComparison.Ordinal)
        .Replace("#", "⏎", StringComparison.Ordinal)
        .Replace("*", "✱", StringComparison.Ordinal);
}
