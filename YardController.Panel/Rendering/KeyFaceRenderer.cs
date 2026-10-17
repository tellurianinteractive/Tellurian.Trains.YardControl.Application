using SkiaSharp;
using Svg.Skia;

namespace Tellurian.Trains.YardController.Panel.Rendering;

/// <summary>
/// Composes a key face: the ground, an icon drawn for a 96 unit viewBox or a glyph set in type,
/// and an optional label under it. Nothing here knows about the panel or the wire protocol, so
/// the same composition serves the preview files and the device.
/// </summary>
public sealed class KeyFaceRenderer : IDisposable
{
    private readonly KeyFaceOptions _options;
    private readonly SKTypeface _typeface;
    private readonly bool _ownsTypeface;

    public KeyFaceRenderer(KeyFaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        (_typeface, _ownsTypeface) = ResolveTypeface(options);
    }

    /// <summary>
    /// The face labels are actually being set in, which is not always the one asked for — a
    /// missing family falls back silently, and a fallback that is not condensed changes how much
    /// text fits.
    /// </summary>
    public string FontFamily => _typeface.FamilyName;

    /// <summary>The options this renderer was built with.</summary>
    public KeyFaceOptions Options => _options;

    /// <summary>An icon read from a file, with an optional label under it.</summary>
    public KeyFaceResult RenderSvgFile(string svgPath, string? label)
    {
        ArgumentNullException.ThrowIfNull(svgPath);
        using var svg = new SKSvg();
        var picture = svg.Load(svgPath)
            ?? throw new InvalidOperationException($"'{svgPath}' could not be read as SVG.");
        return Compose(Path.GetFileNameWithoutExtension(svgPath), picture, null, label);
    }

    /// <summary>An icon read from markup — how the embedded icons are drawn.</summary>
    public KeyFaceResult RenderSvg(string name, string svgMarkup, string? label, string? accent = null)
    {
        ArgumentNullException.ThrowIfNull(svgMarkup);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgMarkup));
        using var svg = new SKSvg();
        var picture = svg.Load(stream)
            ?? throw new InvalidOperationException($"'{name}' could not be read as SVG.");
        return Compose(name, picture, null, label, accent);
    }

    /// <summary>
    /// A numeric-pad key: one character set large, with an optional word under it. The right pad
    /// carries numbers and the four entry controls, and none of those is a picture.
    /// </summary>
    public KeyFaceResult RenderGlyph(string glyph, string? label, string? accent = null) =>
        Compose(glyph, null, glyph, label, accent);

    /// <summary>A key with nothing on it. Eleven of the alternate set are blank (§5.2).</summary>
    public KeyFaceResult RenderBlank() => Compose("", null, null, null);

    /// <summary>
    /// Encodes a face for a file. The device takes raw pixels, so this exists for the preview
    /// images and the contact sheet rather than for the panel.
    /// </summary>
    public ReadOnlyMemory<byte> Encode(KeyFaceResult face)
    {
        ArgumentNullException.ThrowIfNull(face);
        var info = new SKImageInfo(face.Size, face.Size, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var source = face.Bgr24.Span;
        var bgra = new byte[face.Size * face.Size * 4];
        for (var i = 0; i < face.Size * face.Size; i++)
        {
            bgra[(i * 4) + 0] = source[(i * 3) + 0];
            bgra[(i * 4) + 1] = source[(i * 3) + 1];
            bgra[(i * 4) + 2] = source[(i * 3) + 2];
            bgra[(i * 4) + 3] = 0xFF;
        }
        using var image = SKImage.FromPixelCopy(info, bgra);
        var format = _options.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        using var data = image.Encode(format, _options.JpegQuality);
        return data.ToArray();
    }

    public void Dispose()
    {
        if (_ownsTypeface) _typeface.Dispose();
    }

    private KeyFaceResult Compose(string name, SKPicture? picture, string? glyph, string? label, string? accent = null)
    {
        var size = _options.Size;
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(_options.Background);

        // A half turn applies to the whole face, label included, so it is a transform on the
        // canvas rather than a pass over the finished pixels.
        if (_options.Rotate180) canvas.RotateDegrees(180, size / 2f, size / 2f);

        var hasLabel = !string.IsNullOrWhiteSpace(label);
        if (picture is not null) DrawIcon(canvas, picture, hasLabel);
        else if (glyph is not null) DrawGlyph(canvas, glyph, hasLabel);

        var height = 0f;
        var shrunk = false;
        var fits = true;
        if (hasLabel) (height, shrunk, fits) = DrawLabel(canvas, label!);

        if (accent is { Length: > 0 }) DrawAccent(canvas, accent);
        if (_options.Dimming < 1f) DrawDimming(canvas);

        // After the dimming, never before it. The hairline says which set of buttons is showing,
        // which is true of the key whether or not it can be pressed just now — dimmed along with
        // everything else it would be faintest exactly when a whole pad of unfamiliar keys is
        // under the operator's fingers, which is when it is most needed (§5.3).
        if (_options.AlternateHairline) DrawHairline(canvas);

        return new KeyFaceResult(name, size, ToBgr24(surface, size), label, height, shrunk, fits);
    }

    /// <summary>
    /// Reads the surface out as three bytes a pixel in blue-green-red order, which is the one
    /// arrangement every panel in this family takes.
    /// </summary>
    private static ReadOnlyMemory<byte> ToBgr24(SKSurface surface, int size)
    {
        using var image = surface.Snapshot();
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var bgra = new byte[size * size * 4];
        image.ReadPixels(info, System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(bgra, 0), info.RowBytes, 0, 0);

        var bgr = new byte[size * size * 3];
        for (var i = 0; i < size * size; i++)
        {
            bgr[(i * 3) + 0] = bgra[(i * 4) + 0];
            bgr[(i * 3) + 1] = bgra[(i * 4) + 1];
            bgr[(i * 3) + 2] = bgra[(i * 4) + 2];
        }
        return bgr;
    }

    private void DrawIcon(SKCanvas canvas, SKPicture picture, bool hasLabel)
    {
        var source = picture.CullRect;
        if (source.Width <= 0 || source.Height <= 0) return;

        var size = _options.Size;
        var side = hasLabel ? size * _options.IconZone : size;
        var scale = side / Math.Max(source.Width, source.Height);

        canvas.Save();
        // Centred across the key either way; pinned to the top when a label takes the bottom of
        // it, centred vertically when the icon has the whole key to itself.
        var left = (size - (source.Width * scale)) / 2f;
        var top = hasLabel ? 0f : (size - (source.Height * scale)) / 2f;
        canvas.Translate(left, top);
        canvas.Scale(scale);
        canvas.Translate(-source.Left, -source.Top);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    /// <summary>
    /// True where the resolved face can really draw every character of a string. Skia draws a
    /// missing character as an empty box rather than failing, and three different symbols coming
    /// out as the same box is not something anyone notices by reading code — so a caller that
    /// cares is given a way to ask first.
    /// </summary>
    public bool CanDraw(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var font = new SKFont(_typeface, 16);
        foreach (var rune in text.EnumerateRunes())
        {
            if (font.GetGlyph(rune.Value) == 0) return false;
        }
        return true;
    }

    private void DrawGlyph(SKCanvas canvas, string glyph, bool hasLabel)
    {
        var size = _options.Size;
        using var font = new SKFont(_typeface, size * _options.GlyphHeight) { Subpixel = true };
        using var paint = new SKPaint { Color = _options.LabelColour, IsAntialias = true };

        var metrics = font.Metrics;
        // Centre the glyph in the space above the label, or in the whole key without one.
        var zone = hasLabel ? size * _options.IconZone : size;
        var baseline = ((zone - (metrics.Descent - metrics.Ascent)) / 2f) - metrics.Ascent;
        canvas.DrawText(glyph, size / 2f, baseline, SKTextAlign.Center, font, paint);
    }

    private (float Height, bool WasShrunk, bool Fits) DrawLabel(SKCanvas canvas, string label)
    {
        var size = _options.Size;
        var usable = size * (1f - (2f * _options.SideMargin));
        var nominal = size * _options.LabelHeight;

        using var font = new SKFont(_typeface, nominal) { Subpixel = true };
        var width = font.MeasureText(label);
        var height = nominal;
        if (width > usable && width > 0f)
        {
            height = nominal * usable / width;
            font.Size = height;
        }

        // Centre the label in the strip under the icon using the face's own metrics, so a face
        // with unusual ascent or descent still sits where the eye expects it.
        var metrics = font.Metrics;
        var zoneTop = size * _options.IconZone;
        var zoneHeight = size - zoneTop;
        var baseline = zoneTop + ((zoneHeight - (metrics.Descent - metrics.Ascent)) / 2f) - metrics.Ascent;

        using var paint = new SKPaint { Color = _options.LabelColour, IsAntialias = true };
        canvas.DrawText(label, size / 2f, baseline, SKTextAlign.Center, font, paint);

        var shrunk = height < nominal - 0.01f;
        return (height, shrunk, height >= size * _options.MinimumLabelHeight);
    }

    /// <summary>
    /// A bar along the foot of the key carrying the state of the object the entry names (§10.1).
    /// A bar rather than a tint over the whole face: the icon's own colours are the EBICOS symbol
    /// colours, and washing them with a second one would say something the symbol set does not.
    /// </summary>
    private void DrawAccent(SKCanvas canvas, string accent)
    {
        if (!SKColor.TryParse(accent, out var colour)) return;
        var size = _options.Size;
        var height = Math.Max(3f, size / 12f);
        using var paint = new SKPaint { Color = colour, IsAntialias = false };
        canvas.DrawRect(0, size - height, size, height, paint);
    }

    private void DrawHairline(SKCanvas canvas)
    {
        var size = _options.Size;
        var stroke = Math.Max(1f, size / 48f);
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            IsAntialias = true,
        };
        var inset = stroke / 2f;
        canvas.DrawRect(inset, inset, size - stroke, size - stroke, paint);
    }

    /// <summary>
    /// Lays black over the whole face at the complement of the dimming. A key that cannot legally
    /// be pressed is darkened rather than blanked (§10.2): the operator still needs to read what
    /// the key is in order to understand why it is out.
    /// </summary>
    private void DrawDimming(SKCanvas canvas)
    {
        var alpha = (byte)Math.Clamp((1f - _options.Dimming) * 255f, 0, 255);
        if (alpha == 0) return;
        using var paint = new SKPaint { Color = SKColors.Black.WithAlpha(alpha) };
        canvas.DrawRect(0, 0, _options.Size, _options.Size, paint);
    }

    /// <summary>
    /// Takes the first family that is really installed, at the asked-for width and weight.
    /// <para>
    /// Two things make this less obvious than it looks. Asking Skia for a family it does not have
    /// gets a silent fallback rather than nothing, so the installed list is consulted first. And
    /// Skia enumerates typographic families, in which a narrow cut is a width variant rather than
    /// a family of its own — "Arial Narrow" is not in the list and never resolves, while "Arial"
    /// asked for condensed is exactly it.
    /// </para>
    /// Only a face we created is ours to dispose.
    /// </summary>
    private static (SKTypeface Typeface, bool Owned) ResolveTypeface(KeyFaceOptions options)
    {
        var installed = SKFontManager.Default.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var style = new SKFontStyle(options.FontWeight, options.FontWidth, SKFontStyleSlant.Upright);
        foreach (var family in options.FontFamilies)
        {
            if (!installed.Contains(family)) continue;
            var typeface = SKTypeface.FromFamilyName(family, style);
            if (typeface is not null) return (typeface, true);
        }
        return (SKTypeface.Default, false);
    }
}
