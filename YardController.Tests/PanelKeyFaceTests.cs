using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Panel.Rendering;
using YardController.Web.Components;

namespace YardController.Tests;

/// <summary>
/// The composed key faces. These mostly guard against things that fail silently: a face that draws
/// a missing character as an empty box, and an accent that is computed but never reaches the pixels.
/// </summary>
[TestClass]
public class PanelKeyFaceTests
{
    private static PanelKeyFaces Faces() => new(new KeyFaceOptions { Size = 96 });

    [TestMethod]
    public void EveryCommandComposesAFace()
    {
        using var faces = Faces();
        foreach (var command in Enum.GetValues<PanelCommand>())
        {
            var face = faces.For(command, "sv");
            Assert.AreEqual(96 * 96 * 3, face.Bgr24.Length, $"{command} should be 96x96 in BGR24");
        }
    }

    [TestMethod]
    public void EveryNumericKeyComposesAFace()
    {
        using var faces = Faces();
        foreach (var key in NumericPad.Keys)
        {
            var face = faces.For(key, "sv");
            Assert.AreEqual(96 * 96 * 3, face.Bgr24.Length, $"'{key}' should be 96x96 in BGR24");
        }
    }

    [TestMethod]
    public void TheThreeDrawnSymbolsAreNotAllTheSameBox()
    {
        // Skia draws a character the face has not got as an empty box rather than failing, and the
        // minus sign, the wildcard and the return arrow are all missing from Bahnschrift. They came
        // out as three identical images once; they are drawn as icons now.
        using var faces = Faces();
        var undo = faces.For('-', null).Bgr24.ToArray();
        var all = faces.For('*', null).Bgr24.ToArray();
        var send = faces.For('#', null).Bgr24.ToArray();

        CollectionAssert.AreNotEqual(undo, all, "minus and the wildcard must not draw the same");
        CollectionAssert.AreNotEqual(all, send, "the wildcard and enter must not draw the same");
        CollectionAssert.AreNotEqual(undo, send, "minus and enter must not draw the same");
    }

    [TestMethod]
    public void ThePlusIsDrawnToTheSameMeasureAsTheMinus()
    {
        // Reported from the panel: the plus looked small beside the keys either side of it. A
        // typeset plus is set within a digit's height and its arms are shorter still, so it read
        // as a lighter key than the minus and the wildcard it sits between. It is drawn now, on
        // the minus bar's own measure.
        using var faces = Faces();
        var (plusWidth, plusHeight) = Extent(faces.For('+', null).Bgr24.Span);
        var (minusWidth, _) = Extent(faces.For('-', null).Bgr24.Span);
        var (wildcardWidth, _) = Extent(faces.For('*', null).Bgr24.Span);

        Assert.AreEqual(minusWidth, plusWidth, 2.0,
            $"the plus should span the minus bar (plus {plusWidth}, minus {minusWidth})");
        Assert.AreEqual(plusWidth, plusHeight, 2.0,
            $"the plus should stand as tall as it is wide (wide {plusWidth}, tall {plusHeight})");
        Assert.IsTrue(plusWidth >= wildcardWidth * 0.8,
            $"the plus should hold its own beside the wildcard (plus {plusWidth}, wildcard {wildcardWidth})");
    }

    [TestMethod]
    public void ADrawnSymbolIsNotBlank()
    {
        // An empty key and a key whose glyph silently failed to draw look the same from here, so
        // the test that matters is that something was actually put on it.
        using var faces = Faces();
        var blank = faces.Blank().Bgr24.ToArray();

        foreach (var key in NumericPad.Keys)
        {
            CollectionAssert.AreNotEqual(blank, faces.For(key, null).Bgr24.ToArray(), $"'{key}' drew nothing");
        }
    }

    [TestMethod]
    public void AnAccentReachesThePixels()
    {
        using var faces = Faces();
        var plain = faces.For(PanelCommand.SignalGo, "sv").Bgr24.ToArray();
        var green = faces.For(PanelCommand.SignalGo, "sv", ButtonSet.Main, KeyLighting.Lit, EbicosColours.Green).Bgr24.ToArray();
        var grey = faces.For(PanelCommand.SignalGo, "sv", ButtonSet.Main, KeyLighting.Lit, EbicosColours.Grey).Bgr24.ToArray();

        CollectionAssert.AreNotEqual(plain, green, "an accent must change the face");
        CollectionAssert.AreNotEqual(green, grey, "different accents must differ from each other");
    }

    [TestMethod]
    public void TheAccentSitsAlongTheFootAndLeavesTheIconAlone()
    {
        using var faces = Faces();
        var plain = faces.For(PanelCommand.SignalGo, null).Bgr24.Span;
        var accented = faces.For(PanelCommand.SignalGo, null, ButtonSet.Main, KeyLighting.Lit, EbicosColours.Blue).Bgr24.Span;

        // The top of the key carries the symbol and must be untouched; the bottom row must not be.
        var topRowsEqual = true;
        for (var i = 0; i < 96 * 80 * 3; i++)
        {
            if (plain[i] != accented[i]) { topRowsEqual = false; break; }
        }
        Assert.IsTrue(topRowsEqual, "the accent must not paint over the symbol");

        var last = (96 * 95) * 3;
        Assert.AreEqual(0xFF, accented[last], "the foot should carry blue's blue channel");
        Assert.AreEqual(0x33, accented[last + 2], "the foot should carry blue's red channel");
    }

    [TestMethod]
    public void ADimmedKeyIsDarkerButStillThere()
    {
        // §10.2 darkens a key that cannot legally be pressed rather than blanking it: the operator
        // still needs to read what it is in order to understand why it is out.
        using var faces = Faces();
        var lit = Brightness(faces.For(PanelCommand.PointStraight, "sv", ButtonSet.Main, KeyLighting.Lit).Bgr24.Span);
        var dim = Brightness(faces.For(PanelCommand.PointStraight, "sv", ButtonSet.Main, KeyLighting.Dimmed).Bgr24.Span);
        var blank = Brightness(faces.Blank().Bgr24.Span);

        Assert.IsTrue(dim < lit, "a dimmed key must be darker than a lit one");
        Assert.IsTrue(dim > blank, "a dimmed key must still show something");
    }

    [TestMethod]
    public void TheAlternateSetIsAnnouncedOnTheKeyItself()
    {
        // §5.3: a wrong-set press is the one new error the two sets can produce, so it is signalled
        // on all sixteen keys as well as by the pictures changing.
        using var faces = Faces();
        var main = faces.For(PanelCommand.PointLock, "sv", ButtonSet.Main).Bgr24.ToArray();
        var alternate = faces.For(PanelCommand.PointLock, "sv", ButtonSet.Alternate).Bgr24.ToArray();

        CollectionAssert.AreNotEqual(main, alternate);
    }

    [TestMethod]
    public void TheAlternateSetIsNoDimmerThanTheMainOne()
    {
        // Reported from the panel: the alternate keys came up noticeably darker. They carry the
        // four commands that have no pipeline yet, and the painter was asking IsCommandLegal —
        // which folds that in — so every one of them rendered dimmed.
        using var faces = Faces();

        var main = Brightness(faces.For(PanelCommand.PointStraight, "sv", ButtonSet.Main).Bgr24.Span);
        var alternate = Brightness(faces.For(PanelCommand.PointLock, "sv", ButtonSet.Alternate).Bgr24.Span);

        Assert.IsTrue(alternate > main * 0.8,
            $"the alternate set should be lit like the main one (main {main:F1}, alternate {alternate:F1})");
    }

    [TestMethod]
    public void TheHairlineKeepsItsFullStrengthOnADimmedKey()
    {
        // The hairline says which set of buttons is showing. Dimmed along with the rest it is
        // faintest exactly when a pad of unfamiliar keys is under the operator's fingers.
        using var faces = Faces();

        var lit = EdgeBrightness(faces.For(PanelCommand.PointLock, "sv", ButtonSet.Alternate, KeyLighting.Lit).Bgr24.Span);
        var dimmed = EdgeBrightness(faces.For(PanelCommand.PointLock, "sv", ButtonSet.Alternate, KeyLighting.Dimmed).Bgr24.Span);

        Assert.AreEqual(lit, dimmed, 2.0,
            $"the hairline must not dim with the key (lit {lit:F1}, dimmed {dimmed:F1})");
    }

    [TestMethod]
    public void ABlankAlternateKeyStillCarriesTheHairline()
    {
        // Eleven of the sixteen are blank, and §5.3 wants the set announced on all of them.
        using var faces = Faces();

        var plain = EdgeBrightness(faces.Blank(hairline: false).Bgr24.Span);
        var marked = EdgeBrightness(faces.Blank(hairline: true).Bgr24.Span);

        Assert.IsTrue(marked > plain + 50, $"a blank alternate key needs its border (plain {plain:F1}, marked {marked:F1})");
    }

    /// <summary>The mean brightness of the outermost ring of pixels, which is where the hairline is.</summary>
    private static double EdgeBrightness(ReadOnlySpan<byte> bgr24)
    {
        const int Size = 96;
        long total = 0;
        var counted = 0;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (x > 1 && x < Size - 2 && y > 1 && y < Size - 2) continue;
                var i = ((y * Size) + x) * 3;
                total += bgr24[i] + bgr24[i + 1] + bgr24[i + 2];
                counted += 3;
            }
        }
        return total / (double)counted;
    }

    /// <summary>
    /// The bounding box of what was actually drawn on a face. A symbol's measure is not something
    /// the icon file can be read for - the composition scales and places it - so it is measured
    /// off the pixels the panel would be sent.
    /// </summary>
    private static (int Width, int Height) Extent(ReadOnlySpan<byte> bgr24)
    {
        const int Size = 96;
        int left = Size, right = -1, top = Size, bottom = -1;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i = ((y * Size) + x) * 3;
                if (bgr24[i] + bgr24[i + 1] + bgr24[i + 2] < 3 * 128) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        return right < left ? (0, 0) : (right - left + 1, bottom - top + 1);
    }

    private static double Brightness(ReadOnlySpan<byte> bgr24)
    {
        long total = 0;
        for (var i = 0; i < bgr24.Length; i++) total += bgr24[i];
        return total / (double)bgr24.Length;
    }
}
