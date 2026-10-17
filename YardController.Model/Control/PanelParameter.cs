namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// What kind of number a command asks for. It decides which set of numbers the digits entered so
/// far are matched against, and so which keys may legally be pressed next (§10.2).
/// </summary>
public enum PanelParameter
{
    /// <summary>The command takes no parameter — the two set-switching keys.</summary>
    None = 0,

    /// <summary>A point number, matched against the points of the station.</summary>
    Point,

    /// <summary>
    /// One or more signal numbers: a from-signal, then any via-signals, then the destination.
    /// A route always ends at the last signal entered, exactly as the keypad's <c>21.25.31</c> does.
    /// </summary>
    Signal,

    /// <summary>A shunting area number, matched against the areas declared for the station.</summary>
    ShuntingArea,

    /// <summary>
    /// A train number. Unlike the others this is not matched against anything the station file
    /// knows: a train number is whatever the timetable says, so entry cannot narrow it and the
    /// operator settles it with ⏎.
    /// </summary>
    TrainNumber,
}
