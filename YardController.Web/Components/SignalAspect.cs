namespace YardController.Web.Components;

/// <summary>
/// How a signal is presented on the yard view, following EBICOS
/// (Trafikverket TRVINFRA-00305 "Rangerstyrsystem" §6.2.3–6.2.5).
/// The symbol shape carries the signal type; the colour carries the state.
/// Members are ordered by the priority the specification assigns them,
/// highest priority first.
/// </summary>
public enum SignalAspect
{
    /// <summary>Upplåsning pågår — route is being cancelled and locks are releasing. Magenta.</summary>
    Releasing,
    /// <summary>Stopp i huvudsignal, lodrätt i dvärgsignal — green dwarf part, grey main part.</summary>
    ProceedShunting,
    /// <summary>Kör — proceed. Green in both the dwarf and the main part.</summary>
    Proceed,
    /// <summary>Slutpunkt/stopp i rörelseväg — destination signal of an active route. Red.</summary>
    RouteEnd,
    /// <summary>Stopp (normaltillstånd) — at stop with no route. Grey.</summary>
    Stop
}
