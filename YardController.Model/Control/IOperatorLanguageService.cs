namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// The language the yard is being worked in, and notice when it changes.
/// <para>
/// A browser circuit gets its language from the request, but a button panel is a piece of the
/// layout room rather than a session: it has no request to take a culture from, and the thread it
/// repaints on is whichever one happened to raise the event. Reading
/// <c>CultureInfo.CurrentUICulture</c> there answers with the machine's own culture, which is why
/// keys could come up lettered in one language while the diagram was in another.
/// </para>
/// <para>
/// So the choice is held explicitly, in one place, and anything outside a request asks here.
/// </para>
/// </summary>
public interface IOperatorLanguageService
{
    /// <summary>Raised when the operator picks another language.</summary>
    event Action<string>? LanguageChanged;

    /// <summary>The current two-letter ISO language code.</summary>
    string Language { get; }

    /// <summary>
    /// Records the operator's choice. Anything but a two-letter code is ignored: a culture may
    /// arrive as <c>sv-SE</c>, and the labels are keyed by language rather than by region.
    /// </summary>
    void SetLanguage(string language);
}
