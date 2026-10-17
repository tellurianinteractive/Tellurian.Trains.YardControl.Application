using Tellurian.Trains.YardController.Model.Control;
using System.Globalization;

namespace YardController.Web.Resources;

/// <summary>
/// Picks the wording of a route message. A shunting movement is a shunting route, never a train
/// route, and the status line has to say which one it is — the two are kept apart in colour on
/// the diagram, so they are kept apart in words as well.
/// </summary>
/// <remarks>
/// A teardown command carries <see cref="TrainRouteState.Clear"/> or
/// <see cref="TrainRouteState.Cancel"/>, not the kind of the route it ends, so every method takes
/// the kind as a flag the caller reads off the route that is standing.
/// </remarks>
internal static class RouteMessages
{
    internal static string Set(bool isShunting, int fromSignal, int toSignal) =>
        string.Format(CultureInfo.CurrentCulture, isShunting ? Messages.ShuntingRouteSet : Messages.RouteSet, fromSignal, toSignal);

    internal static string Cleared(bool isShunting, int fromSignal, int toSignal) =>
        string.Format(CultureInfo.CurrentCulture, isShunting ? Messages.ShuntingRouteCleared : Messages.RouteCleared, fromSignal, toSignal);

    internal static string Cancelling(bool isShunting, int fromSignal, int toSignal) =>
        string.Format(CultureInfo.CurrentCulture, isShunting ? Messages.ShuntingRouteCancelling : Messages.RouteCancelling, fromSignal, toSignal);

    internal static string NotFound(bool isShunting, int fromSignal, int toSignal) =>
        string.Format(CultureInfo.CurrentCulture, isShunting ? Messages.ShuntingRouteNotFound : Messages.RouteNotFound, fromSignal, toSignal);

    internal static string Queued(bool isShunting, int fromSignal, int toSignal) =>
        string.Format(CultureInfo.CurrentCulture, isShunting ? Messages.ShuntingRouteQueued : Messages.RouteQueued, fromSignal, toSignal);

    internal static string QueuedCancelled(bool isShunting, int toSignal) =>
        string.Format(CultureInfo.CurrentCulture, isShunting ? Messages.ShuntingRouteQueuedCancelled : Messages.RouteQueuedCancelled, toSignal);

    internal static string ConfirmClear(bool isShunting, int fromSignal, int toSignal) =>
        string.Format(CultureInfo.CurrentCulture, isShunting ? Messages.ConfirmClearShuntingRoute : Messages.ConfirmClearRoute, fromSignal, toSignal);
}
