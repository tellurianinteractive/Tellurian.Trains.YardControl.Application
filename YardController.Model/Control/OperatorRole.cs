namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// Who issued a command. A station is worked by a dispatcher and, optionally, by a shunting
/// yard master; each has an own view and an own set of permitted commands.
/// </summary>
public enum OperatorRole
{
    /// <summary>
    /// Owns the interlocking — every point and every route, except what is currently handed
    /// over to a shunter. Input without a role (console, UDP) counts as the dispatcher.
    /// </summary>
    Dispatcher,

    /// <summary>
    /// Owns only the points of the shunting areas that are released, and the routes that run
    /// entirely inside them. Everything else in the yard is visible but not operable.
    /// </summary>
    Shunter
}
