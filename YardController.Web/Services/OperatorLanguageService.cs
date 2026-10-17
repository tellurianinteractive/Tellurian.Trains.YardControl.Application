using System.Globalization;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

/// <summary>
/// Holds the language the yard is being worked in. Single-user application: one operator, one
/// station, one choice of language, so this is a singleton and not per-circuit.
/// </summary>
public sealed class OperatorLanguageService : IOperatorLanguageService
{
    /// <summary>
    /// The languages the yard can be worked in. One list, used both to configure request
    /// localisation and to validate what reaches the panel: the label table has a column per
    /// language, and a language with no column would letter every key alike.
    /// </summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["en", "sv", "da", "nb", "de"];

    /// <summary>What the yard starts in, before anyone has chosen.</summary>
    public const string DefaultLanguage = "en";

    private static readonly HashSet<string> Supported =
        new(SupportedLanguages, StringComparer.OrdinalIgnoreCase);

    private string _language;

    public OperatorLanguageService(string defaultLanguage = DefaultLanguage) =>
        _language = Normalise(defaultLanguage) ?? DefaultLanguage;

    public event Action<string>? LanguageChanged;

    public string Language => _language;

    public void SetLanguage(string language)
    {
        if (Normalise(language) is not { } normalised || normalised == _language) return;
        _language = normalised;
        LanguageChanged?.Invoke(normalised);
    }

    /// <summary>
    /// The two-letter code for whatever was passed, or null where it is not a language this yard
    /// is worked in.
    /// <para>
    /// Checked against the supported list rather than merely parsed, because
    /// <c>CultureInfo.GetCultureInfo</c> does not reject nonsense: on Windows it builds a custom
    /// culture out of it, and "not-a-culture-at-all" comes back as a culture named "not".
    /// </para>
    /// <para>
    /// A culture arrives as <c>sv</c> or <c>sv-SE</c> depending on where it came from, and the
    /// label table is keyed by language: Swedish is Swedish whichever region asked for it.
    /// </para>
    /// </summary>
    private static string? Normalise(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        try
        {
            var code = CultureInfo.GetCultureInfo(language.Trim()).TwoLetterISOLanguageName;
            return Supported.Contains(code) ? code : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
