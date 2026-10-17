namespace Tellurian.Trains.YardController.Panel.Rendering;

/// <summary>
/// Key labels in every supported language, in the shape the station files already use: a
/// comment starts with an apostrophe, the first row names the languages, and every row after
/// it is an icon name, a colon, then one label per language separated by semicolons.
/// <para>
/// Button vocabulary is not message vocabulary — a key wants the shortest idiomatic word where
/// a message wants the correct one — so this is a file of its own rather than a reading of
/// <c>Messages.resx</c>.
/// </para>
/// </summary>
public sealed class LabelSet
{
    private const char CommentMarker = '\'';
    private const char NameSeparator = ':';
    private const char LanguageSeparator = ';';

    private readonly Dictionary<string, int> _languageIndex;
    private readonly IReadOnlyDictionary<string, string[]> _labels;

    private LabelSet(IReadOnlyList<string> languages, IReadOnlyDictionary<string, string[]> labels)
    {
        Languages = languages;
        _languageIndex = languages
            .Select((language, index) => (language, index))
            .ToDictionary(x => x.language, x => x.index, StringComparer.OrdinalIgnoreCase);
        _labels = labels;
    }

    /// <summary>An empty set: every icon renders without a label.</summary>
    public static LabelSet Empty { get; } = new([], new Dictionary<string, string[]>());

    /// <summary>The language codes named on the file's first row, in the order they appear.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>Reads a label table from a file.</summary>
    public static LabelSet Load(string path) => Read(File.ReadLines(path));

    /// <summary>
    /// Reads a label table from a stream — how the table embedded in this assembly is read, so
    /// that a published single-file application still letters its keys.
    /// </summary>
    public static LabelSet Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return Read(lines);
    }

    private static LabelSet Read(IEnumerable<string> source)
    {
        var languages = new List<string>();
        var labels = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in source)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == CommentMarker) continue;

            var colon = line.IndexOf(NameSeparator, StringComparison.Ordinal);
            if (colon < 0)
            {
                // The first row without a name is the language row; anything else is a typo.
                if (languages.Count == 0)
                {
                    languages.AddRange(line.Split(LanguageSeparator, StringSplitOptions.TrimEntries));
                }
                continue;
            }

            var name = line[..colon].Trim();
            if (name.Length == 0) continue;
            labels[name] = line[(colon + 1)..].Split(LanguageSeparator, StringSplitOptions.TrimEntries);
        }

        return new LabelSet(languages, labels);
    }

    /// <summary>
    /// The label for an icon in a language, or null where the icon, the language or that one
    /// cell is not there. A missing label is a key that carries its icon alone, not an error.
    /// </summary>
    public string? For(string iconName, string? language)
    {
        if (language is null) return null;
        if (!_languageIndex.TryGetValue(language, out var index)) return null;
        if (!_labels.TryGetValue(iconName, out var values)) return null;
        if (index >= values.Length) return null;
        var label = values[index];
        return label.Length == 0 ? null : label;
    }
}
