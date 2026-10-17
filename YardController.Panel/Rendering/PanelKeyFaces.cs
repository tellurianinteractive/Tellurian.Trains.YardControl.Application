using System.Collections.Concurrent;
using System.Reflection;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace Tellurian.Trains.YardController.Panel.Rendering;

/// <summary>
/// How brightly a key is lit. During entry the keys that cannot legally be pressed next are
/// darkened rather than blanked (§10.2), which is the strongest operational argument for this
/// hardware: a malformed command cannot be entered, because the keys that would malform it are
/// dark.
/// </summary>
public enum KeyLighting
{
    /// <summary>Fully lit. The key may be pressed.</summary>
    Lit = 0,

    /// <summary>Darkened. The key is there but cannot legally be pressed now.</summary>
    Dimmed = 1,
}

/// <summary>
/// Every face a panel can show, composed once and kept. Composing a face costs a few milliseconds
/// of Skia; there are a few hundred of them across five languages, two button sets and two
/// lighting states, and a repaint must not wait on any of that.
/// <para>
/// The icons and the label table are embedded in this assembly rather than read from disk: a key
/// face is part of the program, and the published application is a single file.
/// </para>
/// </summary>
public sealed class PanelKeyFaces : IDisposable
{
    private const string IconResourceFolder = "Icons.";
    private const string LabelResource = "Labels.txt";

    private readonly ConcurrentDictionary<string, KeyFaceResult> _faces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(bool Hairline, KeyLighting Lighting), KeyFaceRenderer> _renderers = [];
    private readonly KeyFaceOptions _options;
    private readonly LabelSet _labels;

    public PanelKeyFaces(KeyFaceOptions? options = null)
    {
        _options = options ?? new KeyFaceOptions();
        var assembly = typeof(PanelKeyFaces).Assembly;
        _labels = LoadLabels(assembly);
        LoadIcons(assembly);

        foreach (var hairline in new[] { false, true })
        {
            foreach (var lighting in new[] { KeyLighting.Lit, KeyLighting.Dimmed })
            {
                _renderers[(hairline, lighting)] = new KeyFaceRenderer(_options with
                {
                    AlternateHairline = hairline,
                    Dimming = lighting == KeyLighting.Dimmed ? DimmedShare : 1f,
                });
            }
        }
    }

    /// <summary>How much brightness a dimmed key keeps. Enough to read, not enough to invite.</summary>
    public const float DimmedShare = 0.3f;

    /// <summary>The languages the label table carries.</summary>
    public IReadOnlyList<string> Languages => _labels.Languages;

    /// <summary>The icons found in this assembly, by name.</summary>
    public IReadOnlyCollection<string> IconNames => _icons.Keys;

    /// <summary>The face labels are being set in — not always the one asked for.</summary>
    public string FontFamily => _renderers[(false, KeyLighting.Lit)].FontFamily;

    /// <summary>The face for a command key, in a language, at a lighting.</summary>
    public KeyFaceResult For(
        PanelCommand command,
        string? language,
        ButtonSet buttonSet = ButtonSet.Main,
        KeyLighting lighting = KeyLighting.Lit,
        string? accent = null)
    {
        var hairline = buttonSet == ButtonSet.Alternate;
        if (command == PanelCommand.None) return Blank(hairline, lighting);

        var name = command.IconName;
        var key = $"c:{name}:{language}:{hairline}:{lighting}:{accent}";
        return _faces.GetOrAdd(key, _ =>
        {
            var renderer = _renderers[(hairline, lighting)];
            var label = _labels.For(name, language);
            return _icons.TryGetValue(name, out var markup)
                ? renderer.RenderSvg(name, markup, label, accent)
                // An icon that is declared but not drawn shows its label alone; a missing picture
                // should not take the key out of service.
                : renderer.RenderGlyph("?", label, accent);
        });
    }

    /// <summary>The face for a numeric-pad key.</summary>
    public KeyFaceResult For(char numericKey, string? language, KeyLighting lighting = KeyLighting.Lit)
    {
        var name = NumericPad.GlyphName(numericKey);
        var key = $"n:{name}:{language}:{lighting}";
        return _faces.GetOrAdd(key, _ =>
        {
            var renderer = _renderers[(false, lighting)];
            var label = _labels.For(name, language);
            if (_icons.TryGetValue(name, out var markup)) return renderer.RenderSvg(name, markup, label);

            // Asking first, because Skia draws a character the face has not got as an empty box
            // rather than failing: the minus sign, the wildcard and the return arrow all came out
            // as the same box in Bahnschrift, which is why those three are drawn instead.
            var glyph = numericKey.ToString();
            return renderer.RenderGlyph(renderer.CanDraw(glyph) ? glyph : "?", label);
        });
    }

    /// <summary>
    /// The word a command's key carries in a language, or null where it carries none. The
    /// same vocabulary lets the diagram name a command the way the key the operator just
    /// pressed does.
    /// </summary>
    public string? Label(PanelCommand command, string? language) => _labels.For(command.IconName, language);

    /// <summary>A key with nothing on it.</summary>
    public KeyFaceResult Blank(bool hairline = false, KeyLighting lighting = KeyLighting.Lit) =>
        _faces.GetOrAdd($"blank:{hairline}:{lighting}", _ => _renderers[(hairline, lighting)].RenderBlank());

    /// <summary>Encodes a face for a file, which is what the preview images want.</summary>
    public ReadOnlyMemory<byte> Encode(KeyFaceResult face) => _renderers[(false, KeyLighting.Lit)].Encode(face);

    public void Dispose()
    {
        foreach (var renderer in _renderers.Values) renderer.Dispose();
        _renderers.Clear();
    }

    private static LabelSet LoadLabels(Assembly assembly)
    {
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(LabelResource, StringComparison.OrdinalIgnoreCase));
        if (name is null) return LabelSet.Empty;
        using var stream = assembly.GetManifestResourceStream(name);
        return stream is null ? LabelSet.Empty : LabelSet.Load(stream);
    }

    private void LoadIcons(Assembly assembly)
    {
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var marker = resource.IndexOf(IconResourceFolder, StringComparison.Ordinal);
            if (marker < 0 || !resource.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;

            var name = resource[(marker + IconResourceFolder.Length)..^4];
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            _icons[name] = reader.ReadToEnd();
        }
    }
}
