using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>The numbers a station has, which is what entry matches digits against.</summary>
/// <param name="Signals">Every signal number at the station.</param>
/// <param name="Points">Every point number at the station.</param>
/// <param name="ShuntingAreas">Every shunting area number at the station.</param>
public sealed record PanelNumbers(
    IReadOnlySet<int> Signals,
    IReadOnlySet<int> Points,
    IReadOnlySet<int> ShuntingAreas)
{
    /// <summary>A station with nothing in it, which matches no digit an operator presses.</summary>
    public static PanelNumbers Empty { get; } =
        new(new HashSet<int>(), new HashSet<int>(), new HashSet<int>());

    /// <summary>The numbers a parameter of this kind is matched against.</summary>
    public IReadOnlySet<int> For(PanelParameter parameter) => parameter switch
    {
        PanelParameter.Signal => Signals,
        PanelParameter.Point => Points,
        PanelParameter.ShuntingArea => ShuntingAreas,
        _ => Empty.Signals,
    };
}

/// <summary>What a press did.</summary>
public enum PanelEntryOutcome
{
    /// <summary>Taken into the entry. The display has changed.</summary>
    Accepted,

    /// <summary>
    /// Not this device's press. No command has been chosen, so the key keeps its postfix meaning
    /// and belongs to the keypad grammar (§5.4) — the right pad alone behaves as a numeric keypad.
    /// </summary>
    PassThrough,

    /// <summary>Refused. <see cref="PanelEntryResult.Rejection"/> says why.</summary>
    Rejected,

    /// <summary>A whole command; <see cref="PanelEntryResult.KeypadKeys"/> carries what to send.</summary>
    Completed,

    /// <summary>The entry was abandoned.</summary>
    Cancelled,

    /// <summary>The command pad switched to the other set of buttons.</summary>
    ButtonSetChanged,
}

/// <summary>Why a press was refused. Each maps to something the operator can be told.</summary>
public enum PanelEntryRejection
{
    None = 0,

    /// <summary>A command key pressed while a command is already being entered (§10.2).</summary>
    CommandAlreadyChosen,

    /// <summary>The command is drawn on the pad but reaches nothing yet (§12).</summary>
    NotImplemented,

    /// <summary>The wildcard where the command does not take one — all points diverging is not an operation.</summary>
    WildcardNotAccepted,

    /// <summary>The wildcard after digits: it must be the first press of a parameter (§6.2).</summary>
    WildcardNotFirst,

    /// <summary>The command takes a wildcard, but that form is not built yet.</summary>
    WildcardNotImplemented,

    /// <summary>No number at this station starts with the digits entered (§6.3).</summary>
    NoSuchNumber,

    /// <summary>Sent before the command is whole.</summary>
    Incomplete,

    /// <summary>A divider where no number is waiting to be settled.</summary>
    NothingToSettle,

    /// <summary>The button set cannot change while a command is being entered (§5.3).</summary>
    ButtonSetLocked,

    /// <summary>A key with no role once a command has been chosen.</summary>
    NoRoleInEntry,
}

/// <summary>The outcome of one press, and anything that came of it.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Rejection">Why, where the press was refused.</param>
/// <param name="KeypadKeys">The characters to send on, where a command completed.</param>
/// <param name="Command">The command that completed, for the message that reports it.</param>
public sealed record PanelEntryResult(
    PanelEntryOutcome Outcome,
    PanelEntryRejection Rejection = PanelEntryRejection.None,
    string? KeypadKeys = null,
    PanelCommand Command = PanelCommand.None)
{
    internal static PanelEntryResult Accepted { get; } = new(PanelEntryOutcome.Accepted);
    internal static PanelEntryResult PassThrough { get; } = new(PanelEntryOutcome.PassThrough);
    internal static PanelEntryResult Cancelled { get; } = new(PanelEntryOutcome.Cancelled);
    internal static PanelEntryResult SetChanged { get; } = new(PanelEntryOutcome.ButtonSetChanged);
    internal static PanelEntryResult Refuse(PanelEntryRejection why) => new(PanelEntryOutcome.Rejected, why);
}

/// <summary>
/// Command-first entry on a button panel (Specifications/InputDevices.md §6): the command is
/// chosen, its parameters are typed, and the entry is sent. Holding it as a state machine of its
/// own — knowing only the station's numbers, not its services — is what makes the dot rule and
/// the key lighting testable without a device or a yard.
/// <para>
/// The machine never talks to hardware and never executes anything. A completed entry comes back
/// as the characters the keypad grammar already accepts, so the whole of the existing locking,
/// validation and queueing runs unchanged (§11.3).
/// </para>
/// </summary>
public sealed class PanelEntry
{
    private readonly List<string> _parameters = [];
    private readonly List<char> _rawKeys = [];
    private PanelNumbers _numbers;

    public PanelEntry(PanelNumbers? numbers = null) => _numbers = numbers ?? PanelNumbers.Empty;

    /// <summary>The command chosen, or none while the pad is at rest.</summary>
    public PanelCommand Command { get; private set; }

    /// <summary>Which set of buttons the command pad is showing.</summary>
    public ButtonSet ButtonSet { get; private set; }

    /// <summary>The parameters settled so far.</summary>
    public IReadOnlyList<string> Parameters => _parameters;

    /// <summary>The digits of the parameter being typed.</summary>
    public string Current { get; private set; } = "";

    /// <summary>True where the wildcard stands in place of every number.</summary>
    public bool IsWildcard { get; private set; }

    /// <summary>The literal characters entered, for an operator reading their own typing back.</summary>
    public string RawKeys => new([.. _rawKeys]);

    /// <summary>True where nothing has been entered.</summary>
    public bool IsIdle => Command == PanelCommand.None && _parameters.Count == 0
        && Current.Length == 0 && !IsWildcard;

    /// <summary>
    /// The parameters as they would be passed on, the one being typed included.
    /// </summary>
    public IReadOnlyList<string> AllParameters =>
        Current.Length == 0 ? _parameters : [.. _parameters, Current];

    /// <summary>
    /// The numbers still matching the digits typed so far, which the diagram blinks (§8.2).
    /// Empty where nothing is being typed, and empty for a train number — that is whatever the
    /// timetable says and matches against nothing the station file knows.
    /// </summary>
    public IReadOnlyList<int> Candidates
    {
        get
        {
            if (Current.Length == 0 || Command == PanelCommand.None) return [];
            var kind = KindAt(_parameters.Count);
            if (kind is PanelParameter.None or PanelParameter.TrainNumber) return [];
            return [.. _numbers.For(kind).Where(n => n.ToPanelDigits().StartsWith(Current, StringComparison.Ordinal)).Order()];
        }
    }

    /// <summary>True where the entry is a whole, valid command and the send key may light (§10.2).</summary>
    public bool IsComplete
    {
        get
        {
            if (Command == PanelCommand.None || !Command.IsImplemented) return false;
            if (IsWildcard) return Command.IsWildcardImplemented;

            var all = AllParameters;
            var required = Command.Parameters.Count;
            if (all.Count < required) return false;
            if (all.Count > required && !Command.AcceptsVia) return false;

            for (var i = 0; i < all.Count; i++)
            {
                var kind = KindAt(i);
                if (kind == PanelParameter.None) return false;
                if (all[i].Length == 0) return false;
                if (kind == PanelParameter.TrainNumber) continue;
                if (!int.TryParse(all[i], out var value) || !_numbers.For(kind).Contains(value)) return false;
            }

            // A route runs from one signal to a different one; the same signal twice is a typo,
            // not a movement.
            return !Command.AcceptsVia || all.Distinct(StringComparer.Ordinal).Count() == all.Count;
        }
    }

    /// <summary>
    /// True where another parameter may still follow the one being typed, which is what decides
    /// whether the divider key has anything to settle onto.
    /// </summary>
    public bool MoreParametersMayFollow =>
        Command != PanelCommand.None
        && (_parameters.Count + 1 < Command.Parameters.Count || Command.AcceptsVia);

    /// <summary>
    /// Whether a key may legally be pressed next. This is what darkens the keys that would
    /// malform the command (§10.2) — and it is asked of the machine rather than worked out again
    /// by whoever is painting the panel, so that what the keys show and what the entry accepts
    /// cannot disagree.
    /// </summary>
    public bool IsPressLegal(char key)
    {
        // Before a command is chosen every key keeps its postfix meaning, so none of them is out.
        if (Command == PanelCommand.None) return true;

        return key switch
        {
            >= '0' and <= '9' => IsDigitLegal(key),
            '*' => !IsWildcard && _parameters.Count == 0 && Current.Length == 0
                   && Command.AcceptsWildcard && Command.IsWildcardImplemented,
            '.' => !IsWildcard && Current.Length > 0 && CurrentIsValidNumber && MoreParametersMayFollow,
            '#' => IsComplete,
            '/' => true,
            '-' => true,
            _ => false,     // '+' has no role once a command has been chosen (§5.4).
        };
    }

    /// <summary>True where a command key may legally be pressed next.</summary>
    public bool IsCommandLegal(PanelCommand command) =>
        command.SwitchesButtonSet ? IsIdle : IsIdle && command.IsImplemented;

    private bool IsDigitLegal(char digit)
    {
        if (IsWildcard) return false;
        var kind = KindAt(_parameters.Count);
        if (kind == PanelParameter.None) return false;
        if (kind == PanelParameter.TrainNumber) return true;

        var candidate = Current + digit;
        return _numbers.For(kind).Any(n => n.ToPanelDigits().StartsWith(candidate, StringComparison.Ordinal));
    }

    private bool CurrentIsValidNumber
    {
        get
        {
            var kind = KindAt(_parameters.Count);
            if (kind == PanelParameter.TrainNumber) return Current.Length > 0;
            return int.TryParse(Current, out var value) && _numbers.For(kind).Contains(value);
        }
    }

    /// <summary>
    /// Points the machine at another station's numbers. The entry is abandoned: digits matched
    /// against one station's signals mean nothing against another's.
    /// </summary>
    public void UseNumbers(PanelNumbers numbers)
    {
        _numbers = numbers ?? PanelNumbers.Empty;
        Reset();
    }

    /// <summary>Abandons whatever is being entered. The button set is left as it stands.</summary>
    public void Reset()
    {
        Command = PanelCommand.None;
        _parameters.Clear();
        _rawKeys.Clear();
        Current = "";
        IsWildcard = false;
    }

    /// <summary>
    /// A command key. Switching button set is a command in the same sense and is handled here,
    /// including its refusal mid-entry: switching could only mean abandoning the entry, so the key
    /// reports that rather than doing it silently (§5.3).
    /// </summary>
    public PanelEntryResult ChooseCommand(PanelCommand command)
    {
        if (command.SwitchesButtonSet)
        {
            if (!IsIdle) return PanelEntryResult.Refuse(PanelEntryRejection.ButtonSetLocked);
            ButtonSet = command == PanelCommand.ButtonsAlternate ? ButtonSet.Alternate : ButtonSet.Main;
            return PanelEntryResult.SetChanged;
        }

        if (command == PanelCommand.None) return PanelEntryResult.Refuse(PanelEntryRejection.NotImplemented);
        if (!IsIdle) return PanelEntryResult.Refuse(PanelEntryRejection.CommandAlreadyChosen);
        if (!command.IsImplemented) return PanelEntryResult.Refuse(PanelEntryRejection.NotImplemented);

        Command = command;
        return PanelEntryResult.Accepted;
    }

    /// <summary>
    /// One press of the numeric pad. The characters are the keypad's own, so that a press with no
    /// command chosen can simply be passed on to the keypad grammar unchanged.
    /// </summary>
    public PanelEntryResult Press(char key)
    {
        if (Command == PanelCommand.None) return PanelEntryResult.PassThrough;

        return key switch
        {
            >= '0' and <= '9' => PressDigit(key),
            '*' => PressWildcard(),
            '.' => PressDivider(),
            '#' => PressSend(),
            '/' => PressCancel(),
            '-' => PressUndo(),
            _ => PanelEntryResult.Refuse(PanelEntryRejection.NoRoleInEntry),
        };
    }

    /// <summary>
    /// The parameter kind at a position. Past the ones the command names, a route's further
    /// signals are vias and anything else has no parameter there at all.
    /// </summary>
    private PanelParameter KindAt(int index)
    {
        var declared = Command.Parameters;
        if (index < declared.Count) return declared[index];
        return Command.AcceptsVia ? PanelParameter.Signal : PanelParameter.None;
    }

    private PanelEntryResult PressDigit(char digit)
    {
        if (IsWildcard) return PanelEntryResult.Refuse(PanelEntryRejection.WildcardNotFirst);

        var kind = KindAt(_parameters.Count);
        if (kind == PanelParameter.None) return PanelEntryResult.Refuse(PanelEntryRejection.NoSuchNumber);

        var candidate = Current + digit;

        // A train number is not matched against anything: it is whatever the timetable says.
        if (kind == PanelParameter.TrainNumber)
        {
            Current = candidate;
            _rawKeys.Add(digit);
            return PanelEntryResult.Accepted;
        }

        var numbers = _numbers.For(kind);
        var matching = numbers.Where(n => n.ToPanelDigits().StartsWith(candidate, StringComparison.Ordinal)).ToList();

        // Entry is rejected as soon as no number matches the digits entered (§6.3). The digit is
        // not taken, so the operator carries on from what was still good.
        if (matching.Count == 0) return PanelEntryResult.Refuse(PanelEntryRejection.NoSuchNumber);

        Current = candidate;
        _rawKeys.Add(digit);

        // When exactly one number remains and the digits are that number in full, entry advances
        // to the next parameter by itself — which is what makes the dot a tie-breaker rather than
        // a mandatory separator. It advances only while another parameter is still wanted.
        if (matching.Count == 1 && matching[0].ToPanelDigits().Equals(Current, StringComparison.Ordinal)
            && (_parameters.Count + 1 < Command.Parameters.Count || Command.AcceptsVia))
        {
            Settle();
        }

        return PanelEntryResult.Accepted;
    }

    private PanelEntryResult PressWildcard()
    {
        if (!Command.AcceptsWildcard) return PanelEntryResult.Refuse(PanelEntryRejection.WildcardNotAccepted);
        if (_parameters.Count > 0 || Current.Length > 0 || IsWildcard)
            return PanelEntryResult.Refuse(PanelEntryRejection.WildcardNotFirst);
        if (!Command.IsWildcardImplemented) return PanelEntryResult.Refuse(PanelEntryRejection.WildcardNotImplemented);

        IsWildcard = true;
        _rawKeys.Add('*');
        return PanelEntryResult.Accepted;
    }

    private PanelEntryResult PressDivider()
    {
        if (IsWildcard) return PanelEntryResult.Refuse(PanelEntryRejection.WildcardNotFirst);
        if (Current.Length == 0) return PanelEntryResult.Refuse(PanelEntryRejection.NothingToSettle);

        // A divider settles the number onto the next parameter, so where there is no next
        // parameter there is nothing for it to do. The key is already dark in that case; refusing
        // it here as well is what keeps the lighting from being merely advisory.
        if (!MoreParametersMayFollow) return PanelEntryResult.Refuse(PanelEntryRejection.NothingToSettle);

        var kind = KindAt(_parameters.Count);
        if (kind is not PanelParameter.TrainNumber
            && (!int.TryParse(Current, out var value) || !_numbers.For(kind).Contains(value)))
            return PanelEntryResult.Refuse(PanelEntryRejection.NoSuchNumber);

        Settle();
        _rawKeys.Add('.');
        return PanelEntryResult.Accepted;
    }

    private PanelEntryResult PressSend()
    {
        if (!IsComplete) return PanelEntryResult.Refuse(PanelEntryRejection.Incomplete);

        var command = Command;
        var keys = command.ToKeypadKeys(AllParameters, IsWildcard);
        if (keys is null) return PanelEntryResult.Refuse(PanelEntryRejection.NotImplemented);

        Reset();

        // The pad returns to the main buttons by itself once an alternate command is sent, so it
        // is never left sitting on the alternate set with the wrong key under the operator's
        // finger (D10). Cancelling does not return, so a mistyped number can simply be retyped.
        ButtonSet = ButtonSet.Main;

        return new PanelEntryResult(PanelEntryOutcome.Completed, PanelEntryRejection.None, keys, command);
    }

    private PanelEntryResult PressCancel()
    {
        Reset();
        return PanelEntryResult.Cancelled;
    }

    private PanelEntryResult PressUndo()
    {
        if (IsWildcard)
        {
            IsWildcard = false;
        }
        else if (Current.Length > 0)
        {
            Current = Current[..^1];
        }
        else if (_parameters.Count > 0)
        {
            // A settled parameter comes back as digits so the operator can retype its end rather
            // than the whole of it.
            Current = _parameters[^1];
            _parameters.RemoveAt(_parameters.Count - 1);
            if (Current.Length > 0) Current = Current[..^1];
        }
        else
        {
            // Nothing left to take back but the command itself.
            Command = PanelCommand.None;
        }

        if (_rawKeys.Count > 0) _rawKeys.RemoveAt(_rawKeys.Count - 1);
        return PanelEntryResult.Accepted;
    }

    private void Settle()
    {
        _parameters.Add(Current);
        Current = "";
    }
}
