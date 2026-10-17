using Microsoft.Extensions.Logging.Abstractions;
using Tellurian.Trains.YardController.Model;
using Tellurian.Trains.YardController.Model.Control;
using YardController.Web.Services;
using YardController.Web.Services.Testing;

namespace YardController.Tests;

/// <summary>
/// A stretch declared with <c>!</c> holds its occupied indication after the detector lets go. The
/// case it exists for: a train arrives, the locomotive is uncoupled and driven away, and the rail
/// falls silent with the wagons still standing on it.
/// </summary>
[TestClass]
public class OccupancyHoldTests
{
    private const string Station = "TestStation";
    private const int Holding = 1;
    private const int Ordinary = 2;
    private const int Undetected = 3;

    private static (LatchingOccupancySectionService Latching, LoggingOccupancySectionService Inner) CreateService()
    {
        var data = new TestYardDataService { CurrentStationName = Station, AvailableStations = [Station] };
        data.AddOccupancySection(new OccupancySection(Holding, new GridCoordinate(2, 0), new GridCoordinate(2, 2), 901, RequiresManualRelease: true));
        data.AddOccupancySection(new OccupancySection(Ordinary, new GridCoordinate(2, 2), new GridCoordinate(2, 4), 902));
        data.AddOccupancySection(new OccupancySection(Undetected, new GridCoordinate(3, 0), new GridCoordinate(3, 2), null, RequiresManualRelease: true));

        var inner = new LoggingOccupancySectionService(NullLogger<LoggingOccupancySectionService>.Instance);
        return (new LatchingOccupancySectionService(inner, data, NullLogger<LatchingOccupancySectionService>.Instance), inner);
    }

    [TestMethod]
    public void DetectorReleasingAHoldingSection_KeepsItOccupied()
    {
        var (latching, inner) = CreateService();

        inner.SetOccupied(Station, Holding, true);
        inner.SetOccupied(Station, Holding, false);

        Assert.IsTrue(latching.IsOccupied(Station, Holding), "the wagons are still standing there");
        Assert.IsTrue(latching.IsHeld(Station, Holding));
        Assert.IsFalse(inner.IsOccupied(Station, Holding), "the detector itself has let go");
    }

    [TestMethod]
    public void DetectorReleasingAnOrdinarySection_FreesIt()
    {
        var (latching, inner) = CreateService();

        inner.SetOccupied(Station, Ordinary, true);
        inner.SetOccupied(Station, Ordinary, false);

        Assert.IsFalse(latching.IsOccupied(Station, Ordinary));
        Assert.IsFalse(latching.IsHeld(Station, Ordinary));
    }

    [TestMethod]
    public void SectionWithoutAddress_IsNeverHeld()
    {
        var (latching, inner) = CreateService();

        inner.SetOccupied(Station, Undetected, true);
        inner.SetOccupied(Station, Undetected, false);

        Assert.IsFalse(latching.IsHeld(Station, Undetected), "a hold answers a detector, and there is none");
        Assert.IsFalse(latching.IsOccupied(Station, Undetected));
    }

    [TestMethod]
    public void ReleasingAHold_FreesTheSection()
    {
        var (latching, inner) = CreateService();
        inner.SetOccupied(Station, Holding, true);
        inner.SetOccupied(Station, Holding, false);

        Assert.IsTrue(latching.Release(Station, Holding));

        Assert.IsFalse(latching.IsOccupied(Station, Holding));
        Assert.IsFalse(latching.IsHeld(Station, Holding));
    }

    [TestMethod]
    public void ReleasingAHold_WhileTheDetectorStillReports_LeavesItOccupied()
    {
        var (latching, inner) = CreateService();
        latching.Hold(Station, Holding);
        inner.SetOccupied(Station, Ordinary, true);
        latching.Hold(Station, Ordinary);

        latching.Release(Station, Ordinary);

        Assert.IsFalse(latching.IsHeld(Station, Ordinary));
        Assert.IsTrue(latching.IsOccupied(Station, Ordinary), "releasing the hold says a person looked, not that the rail is quiet");
    }

    [TestMethod]
    public void DetectorReportingOccupiedAgain_DropsTheHold()
    {
        var (latching, inner) = CreateService();
        inner.SetOccupied(Station, Holding, true);
        inner.SetOccupied(Station, Holding, false);

        inner.SetOccupied(Station, Holding, true);

        Assert.IsFalse(latching.IsHeld(Station, Holding), "the detector has taken the stretch back");
        Assert.IsTrue(latching.IsOccupied(Station, Holding));
    }

    [TestMethod]
    public void HoldingByHand_RequiresADetector()
    {
        var (latching, _) = CreateService();

        Assert.IsTrue(latching.Hold(Station, Holding));
        Assert.IsTrue(latching.Hold(Station, Ordinary), "any addressed stretch may be held by hand");
        Assert.IsFalse(latching.Hold(Station, Undetected));
    }

    [TestMethod]
    public void ReleaseAll_FreesEveryHeldSectionAndCountsThem()
    {
        var (latching, _) = CreateService();
        latching.Hold(Station, Holding);
        latching.Hold(Station, Ordinary);

        Assert.AreEqual(2, latching.ReleaseAll(Station));
        Assert.AreEqual(0, latching.HeldSections(Station).Count);
        Assert.AreEqual(0, latching.ReleaseAll(Station));
    }

    [TestMethod]
    public void HeldSection_IsReportedOccupiedInTheWholeStationsOccupancies()
    {
        var (latching, _) = CreateService();
        latching.Hold(Station, Holding);

        Assert.IsTrue(latching.GetAllOccupancies(Station)[Holding]);
    }

    [TestMethod]
    public void LatchingASection_RaisesNoFreeEvent()
    {
        var (latching, inner) = CreateService();
        var reported = new List<bool>();
        latching.OccupancyChanged += feedback =>
        {
            if (feedback.SectionNumber == Holding) reported.Add(feedback.IsOccupied);
        };

        inner.SetOccupied(Station, Holding, true);
        inner.SetOccupied(Station, Holding, false);

        CollectionAssert.AreEqual(new[] { true }, reported, "the diagram is never told the stretch went free");
    }

    [TestMethod]
    public void ReleasingAHold_AnnouncesBothTheMarkAndTheColour()
    {
        var (latching, inner) = CreateService();
        inner.SetOccupied(Station, Holding, true);
        inner.SetOccupied(Station, Holding, false);
        var holds = new List<bool>();
        var occupancies = new List<bool>();
        latching.HoldChanged += feedback => holds.Add(feedback.IsHeld);
        latching.OccupancyChanged += feedback => occupancies.Add(feedback.IsOccupied);

        latching.Release(Station, Holding);

        CollectionAssert.AreEqual(new[] { false }, holds);
        CollectionAssert.AreEqual(new[] { false }, occupancies);
    }

    [TestMethod]
    public void HoldsAreKeptPerStation()
    {
        var (latching, _) = CreateService();
        latching.Hold(Station, Holding);

        Assert.IsFalse(latching.IsHeld("OtherStation", Holding));
        Assert.AreEqual(0, latching.HeldSections("OtherStation").Count);
    }
}
