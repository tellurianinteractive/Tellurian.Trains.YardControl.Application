namespace YardController.Web.Services;

/// <summary>
/// Translates topology label text using a semicolon-separated CSV file.
/// The first row contains language codes (e.g., en;da;de;nb;sv).
/// Subsequent rows contain the term in each language.
/// Translation works by finding the longest matching term from any language
/// and replacing it with the equivalent in the target language.
/// </summary>
public sealed class LabelTranslator
{
    private readonly Dictionary<string, int> _languageColumns = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string[]> _rows = [];

    internal LabelTranslator() { }

    public static LabelTranslator Load(string csvContent)
    {
        var translator = new LabelTranslator();
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0) return translator;

        // Parse header
        var headers = lines[0].Trim().Split(';');
        for (var i = 0; i < headers.Length; i++)
        {
            translator._languageColumns[headers[i].Trim()] = i;
        }

        // Parse data rows
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            translator._rows.Add(line.Split(';').Select(s => s.Trim()).ToArray());
        }

        return translator;
    }

    public static async Task<LabelTranslator> LoadFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return new LabelTranslator();

        var content = await File.ReadAllTextAsync(filePath);
        return Load(content);
    }

    /// <summary>
    /// Translates a label text to the specified language.
    /// Finds the longest matching term from any language column and replaces it
    /// with the equivalent term in the target language, preserving any suffix (e.g., " 1a").
    /// Returns the original text if no translation is found.
    /// </summary>
    public string Translate(string text, string targetLanguage)
    {
        if (_rows.Count == 0 || !_languageColumns.TryGetValue(targetLanguage, out var targetCol))
            return text;

        // Find the longest matching term from any language
        string? bestMatch = null;
        string? bestTranslation = null;
        var bestLength = 0;

        foreach (var row in _rows)
        {
            if (targetCol >= row.Length) continue;

            for (var col = 0; col < row.Length; col++)
            {
                var term = row[col];
                if (term.Length > bestLength && text.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                {
                    bestMatch = term;
                    bestTranslation = row[targetCol];
                    bestLength = term.Length;
                }
            }
        }

        if (bestMatch is null || bestTranslation is null)
            return text;

        // Replace matched prefix, keep suffix (e.g., " 1a")
        return bestTranslation + text[bestLength..];
    }
}
