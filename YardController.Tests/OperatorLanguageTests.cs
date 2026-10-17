using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Panel.Rendering;
using YardController.Web.Services;

namespace YardController.Tests;

/// <summary>
/// The language the yard is worked in. A browser circuit takes it from the request; a button panel
/// has no request, so the choice has to reach it as an event or its keys keep the words they were
/// lettered with.
/// </summary>
[TestClass]
public class OperatorLanguageTests
{
    [TestMethod]
    public void ChangingTheLanguageIsAnnounced()
    {
        // The panel repaints on this event. Without it the keys keep their old words until
        // something else happens to repaint them, which was the reported fault.
        var service = new OperatorLanguageService("en");
        var announced = new List<string>();
        service.LanguageChanged += announced.Add;

        service.SetLanguage("sv");

        CollectionAssert.AreEqual(new[] { "sv" }, announced);
        Assert.AreEqual("sv", service.Language);
    }

    [TestMethod]
    public void PickingTheLanguageAlreadyInUseAnnouncesNothing()
    {
        // Repainting thirty-two keys costs real USB traffic; doing it for a change that is not one
        // is waste an operator would see as a flicker.
        var service = new OperatorLanguageService("sv");
        var announced = 0;
        service.LanguageChanged += _ => announced++;

        service.SetLanguage("sv");

        Assert.AreEqual(0, announced);
    }

    [TestMethod]
    [DataRow("sv-SE", "sv")]
    [DataRow("da-DK", "da")]
    [DataRow("nb-NO", "nb")]
    [DataRow("de", "de")]
    public void ARegionalCultureIsTakenAsItsLanguage(string culture, string expected)
    {
        // A culture arrives as sv or sv-SE depending on where it came from, and the label table is
        // keyed by language: Swedish is Swedish whichever region asked for it.
        var service = new OperatorLanguageService("en");
        service.SetLanguage(culture);

        Assert.AreEqual(expected, service.Language);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not-a-culture-at-all")]
    [DataRow("fr")]     // a real language, but one this yard has no labels for
    public void ALanguageThisYardIsNotWorkedInLeavesTheChoiceAlone(string culture)
    {
        var service = new OperatorLanguageService("en");
        service.SetLanguage(culture);

        Assert.AreEqual("en", service.Language);
    }

    [TestMethod]
    public void TheDefaultIsWhatItWasGivenNotTheMachinesOwnCulture()
    {
        // The fault this guards against is invisible until a key is lettered: the panel read
        // CultureInfo.CurrentUICulture, which on a Swedish machine is Swedish, while the web UI
        // defaulted to English from configuration. Two languages in one room.
        Assert.AreEqual("en", new OperatorLanguageService("en").Language);
        Assert.AreEqual("de", new OperatorLanguageService("de").Language);
    }

    [TestMethod]
    public void EveryLanguageLettersTheKeysDifferently()
    {
        // If the label table quietly lost a column, relettering would repaint thirty-two identical
        // keys and look like the fault that was just fixed.
        using var faces = new PanelKeyFaces(new KeyFaceOptions { Size = 96 });
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var language in new[] { "en", "sv", "da", "nb", "de" })
        {
            var label = faces.Label(PanelCommand.PointDiverging, language);
            Assert.IsFalse(string.IsNullOrEmpty(label), $"{language} has no word for point-diverging");
            seen[language] = label!;
        }

        Assert.AreEqual("Diverging", seen["en"]);
        Assert.AreEqual("Sido", seen["sv"]);
        Assert.AreEqual("Ablenkung", seen["de"]);
    }

    [TestMethod]
    public void AKeyLetteredInTwoLanguagesIsTwoDifferentFaces()
    {
        using var faces = new PanelKeyFaces(new KeyFaceOptions { Size = 96 });

        var english = faces.For(PanelCommand.PointDiverging, "en").Bgr24.ToArray();
        var swedish = faces.For(PanelCommand.PointDiverging, "sv").Bgr24.ToArray();

        CollectionAssert.AreNotEqual(english, swedish);
    }
}
