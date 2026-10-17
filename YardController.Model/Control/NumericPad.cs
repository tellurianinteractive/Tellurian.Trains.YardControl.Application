namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// The right half of the panel, which replicates the numeric keypad (D3). Every parameter a
/// command takes is a number, so this half is not negotiable and is not configurable either.
/// <para>
/// The characters are the keypad grammar's own, which is what lets a press with no command chosen
/// simply be passed on to the existing parser: before a command is chosen the right pad alone
/// behaves exactly like a numeric keypad (§5.4).
/// </para>
/// </summary>
public static class NumericPad
{
    /// <summary>Keys across the numeric pad.</summary>
    public const int Columns = 4;

    /// <summary>Keys down the numeric pad.</summary>
    public const int Rows = 4;

    /// <summary>Keys on the numeric pad.</summary>
    public const int Count = Columns * Rows;

    /// <summary>
    /// The characters, left to right and top to bottom, in the arrangement of §5.1:
    /// <code>
    ///   7  8  9  −      − undoes one press while a command is being entered
    ///   4  5  6  +      + has no role once a command has been chosen
    ///   1  2  3  ✳      ✳ where a number is expected means all of them
    ///   /  0  .  ⏎      / cancels the entry, . settles a number, ⏎ sends
    /// </code>
    /// </summary>
    public static IReadOnlyList<char> Keys { get; } =
    [
        '7', '8', '9', '-',
        '4', '5', '6', '+',
        '1', '2', '3', '*',
        '/', '0', '.', '#',
    ];

    /// <summary>The character on a key, or nul outside the pad.</summary>
    public static char At(int index) => index >= 0 && index < Count ? Keys[index] : '\0';

    /// <summary>True where the character is one of the ten digits.</summary>
    public static bool IsDigit(char key) => key is >= '0' and <= '9';

    /// <summary>
    /// The name a key's picture is drawn under. Digits are set as themselves; the four entry
    /// controls and the two arithmetic keys get names because their symbols are not characters
    /// every face carries.
    /// </summary>
    public static string GlyphName(char key) => key switch
    {
        '-' => "key-undo",
        '+' => "key-plus",
        '*' => "key-all",
        '/' => "key-cancel",
        '.' => "key-next",
        '#' => "key-send",
        _ => key.ToString(),
    };
}
