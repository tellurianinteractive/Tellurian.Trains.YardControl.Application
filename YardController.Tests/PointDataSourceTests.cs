using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using YardController.Web.Services;
using YardController.Web.Services.Data;

namespace YardController.Tests;

[TestClass]
public class PointDataSourceTests
{
    private string _tempDir = null!;
    private string _pointsPath = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _pointsPath = Path.Combine(_tempDir, "Points.txt");
        // Create minimal topology file required by YardDataService
        File.WriteAllText(Path.Combine(_tempDir, "Topology.txt"), "TestStation\n[Tracks]\n");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private async Task<YardDataService> CreateAndInitialize(string pointsContent)
    {
        File.WriteAllText(_pointsPath, pointsContent);
        // Create empty TrainRoutes.txt to avoid warnings
        File.WriteAllText(Path.Combine(_tempDir, "TrainRoutes.txt"), "");
        var settings = Options.Create(new StationSettings
        {
            Stations = [new StationConfig { Name = "Test", DataFolder = _tempDir }]
        });
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var service = new YardDataService(settings, loggerFactory.CreateLogger<YardDataService>(), loggerFactory);
        await service.InitializeAsync();
        return service;
    }

    #region File Not Found Tests

    [TestMethod]
    public async Task GetPoints_ReturnsEmpty_WhenFileNotFound()
    {
        // Don't write Points.txt
        File.WriteAllText(Path.Combine(_tempDir, "TrainRoutes.txt"), "");
        var settings = Options.Create(new StationSettings
        {
            Stations = [new StationConfig { Name = "Test", DataFolder = _tempDir }]
        });
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var service = new YardDataService(settings, loggerFactory.CreateLogger<YardDataService>(), loggerFactory);
        await service.InitializeAsync();

        Assert.IsEmpty(service.Points);
    }

    [TestMethod]
    public async Task GetTurntableTracks_ReturnsEmpty_WhenFileNotFound()
    {
        File.WriteAllText(Path.Combine(_tempDir, "TrainRoutes.txt"), "");
        var settings = Options.Create(new StationSettings
        {
            Stations = [new StationConfig { Name = "Test", DataFolder = _tempDir }]
        });
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var service = new YardDataService(settings, loggerFactory.CreateLogger<YardDataService>(), loggerFactory);
        await service.InitializeAsync();

        Assert.IsEmpty(service.TurntableTracks);
    }

    #endregion

    #region Empty File Tests

    [TestMethod]
    public async Task GetPoints_ReturnsEmpty_WhenFileIsEmpty()
    {
        var service = await CreateAndInitialize("");

        Assert.IsEmpty(service.Points);
    }

    #endregion

    #region Basic Point Parsing Tests

    [TestMethod]
    public async Task GetPoints_ParsesBasicFormat()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n1:801");

        Assert.HasCount(1, service.Points);
        Assert.AreEqual(1, service.Points[0].Number);
        Assert.Contains(801, service.Points[0].StraightAddresses);
        Assert.Contains(801, service.Points[0].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPoints_ParsesMultipleAddresses()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n1:801,802,803");

        Assert.HasCount(1, service.Points);
        Assert.HasCount(3, service.Points[0].StraightAddresses);
        Assert.Contains(801, service.Points[0].StraightAddresses);
        Assert.Contains(802, service.Points[0].StraightAddresses);
        Assert.Contains(803, service.Points[0].StraightAddresses);
        Assert.HasCount(3, service.Points[0].DivergingAddresses);
        Assert.Contains(801, service.Points[0].DivergingAddresses);
        Assert.Contains(802, service.Points[0].DivergingAddresses);
        Assert.Contains(803, service.Points[0].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPoints_ParsesMultipleLines()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n1:801\n2:802\n3:803");

        Assert.HasCount(3, service.Points);
        Assert.AreEqual(1, service.Points[0].Number);
        Assert.AreEqual(2, service.Points[1].Number);
        Assert.AreEqual(3, service.Points[2].Number);
    }

    #endregion

    #region LockOffset Parsing Tests

    [TestMethod]
    public async Task GetPoints_ParsesLockOffset()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n1:801");

        Assert.HasCount(1, service.Points);
        Assert.AreEqual(1000, service.Points[0].LockAddressOffset);
    }

    [TestMethod]
    public async Task GetPoints_LockOffsetIsCaseInsensitive()
    {
        var service = await CreateAndInitialize("lockoffset:500\n1:801");

        Assert.HasCount(1, service.Points);
        Assert.AreEqual(500, service.Points[0].LockAddressOffset);
    }

    [TestMethod]
    public async Task GetPoints_LockOffsetAppliedToAllPoints()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n1:801\n2:802");

        Assert.HasCount(2, service.Points);
        Assert.IsTrue(service.Points.All(t => t.LockAddressOffset == 1000));
    }

    [TestMethod]
    public async Task GetPoints_WithoutLockOffset_DefaultsToZero()
    {
        // Without a LockOffset line, the default offset is 0
        var service = await CreateAndInitialize("1:801");

        Assert.HasCount(1, service.Points);
        Assert.AreEqual(0, service.Points[0].LockAddressOffset);
    }

    #endregion

    #region Address Range Parsing Tests

    [TestMethod]
    public async Task GetPoints_ParsesAddressRange()
    {
        var service = await CreateAndInitialize("LockOffset:1000\nAdresses:1-5");

        Assert.HasCount(5, service.Points);
        Assert.AreEqual(1, service.Points[0].Number);
        Assert.AreEqual(5, service.Points[4].Number);
    }

    [TestMethod]
    public async Task GetPoints_AddressRangeUsesNumberAsAddress()
    {
        var service = await CreateAndInitialize("LockOffset:1000\nAdresses:10-12");

        Assert.HasCount(3, service.Points);
        Assert.AreEqual(10, service.Points[0].Number);
        Assert.Contains(10, service.Points[0].StraightAddresses);
        Assert.Contains(10, service.Points[0].DivergingAddresses);
        Assert.AreEqual(11, service.Points[1].Number);
        Assert.Contains(11, service.Points[1].StraightAddresses);
        Assert.Contains(11, service.Points[1].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPoints_AddressRangeWithLockOffset()
    {
        var service = await CreateAndInitialize("LockOffset:1000\nAdresses:1-3");

        Assert.HasCount(3, service.Points);
        Assert.IsTrue(service.Points.All(t => t.LockAddressOffset == 1000));
    }

    [TestMethod]
    public async Task GetPoints_AddressRangeIsCaseInsensitive()
    {
        var service = await CreateAndInitialize("LockOffset:1000\nadresses:1-3");

        Assert.HasCount(3, service.Points);
    }

    #endregion

    #region Turntable Parsing Tests

    [TestMethod]
    public async Task GetTurntableTracks_ParsesTurntableConfig()
    {
        var service = await CreateAndInitialize("Turntable:1-5;1000");

        Assert.HasCount(5, service.TurntableTracks);
        Assert.AreEqual(1, service.TurntableTracks[0].Number);
        Assert.AreEqual(5, service.TurntableTracks[4].Number);
    }

    [TestMethod]
    public async Task GetTurntableTracks_AppliesAddressOffset()
    {
        var service = await CreateAndInitialize("Turntable:1-3;1000");

        Assert.HasCount(3, service.TurntableTracks);
        Assert.AreEqual(1001, service.TurntableTracks[0].Address); // 1 + 1000
        Assert.AreEqual(1002, service.TurntableTracks[1].Address); // 2 + 1000
        Assert.AreEqual(1003, service.TurntableTracks[2].Address); // 3 + 1000
    }

    [TestMethod]
    public async Task GetTurntableTracks_IsCaseInsensitive()
    {
        var service = await CreateAndInitialize("turntable:1-2;500");

        Assert.HasCount(2, service.TurntableTracks);
    }

    [TestMethod]
    public async Task GetTurntableTracks_SkipsInvalidFormat()
    {
        var service = await CreateAndInitialize("Turntable:invalid");

        Assert.IsEmpty(service.TurntableTracks);
    }

    [TestMethod]
    public async Task GetPoints_IgnoresTurntableLines()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n1:801\nTurntable:1-5;1000\n2:802");

        Assert.HasCount(2, service.Points);
        Assert.AreEqual(1, service.Points[0].Number);
        Assert.AreEqual(2, service.Points[1].Number);
    }

    #endregion

    #region Invalid Format Tests

    [TestMethod]
    public async Task GetPoints_SkipsInvalidLines()
    {
        var service = await CreateAndInitialize("LockOffset:1000\ninvalid\n1:801\nalso-invalid\n2:802");

        Assert.HasCount(2, service.Points);
    }

    [TestMethod]
    public async Task GetPoints_AddressRangeStartingFromZero_IncludesAllAddresses()
    {
        // Address range starting from 0 includes all addresses in the range
        var service = await CreateAndInitialize("LockOffset:1000\nAdresses:0-5\n1:801");

        // 6 addresses from range (0-5) + 1 explicit point = 7
        Assert.HasCount(7, service.Points);
    }

    [TestMethod]
    public async Task GetPoints_SkipsInvalidAddressRange_WithInvalidEndValue()
    {
        var service = await CreateAndInitialize("LockOffset:1000\nAdresses:1-abc\n1:801");

        // Only the explicit point should be parsed
        Assert.HasCount(1, service.Points);
        Assert.AreEqual(1, service.Points[0].Number);
    }

    [TestMethod]
    public async Task GetPoints_SkipsZeroAddresses()
    {
        var service = await CreateAndInitialize("1:0");

        Assert.IsEmpty(service.Points);
    }

    #endregion

    #region Grouped Format Parsing Tests

    [TestMethod]
    public async Task GetPoints_ParsesGroupedFormat_WithDifferentAddresses()
    {
        // Format: number:(addresses)-(addresses)+
        var service = await CreateAndInitialize("LockOffset:1000\n23:(816,823)-(823)+");

        Assert.HasCount(1, service.Points);
        Assert.AreEqual(23, service.Points[0].Number);
        // Diverging addresses (from the - group)
        Assert.HasCount(2, service.Points[0].DivergingAddresses);
        Assert.Contains(816, service.Points[0].DivergingAddresses);
        Assert.Contains(823, service.Points[0].DivergingAddresses);
        // Straight addresses (from the + group)
        Assert.HasCount(1, service.Points[0].StraightAddresses);
        Assert.Contains(823, service.Points[0].StraightAddresses);
    }

    [TestMethod]
    public async Task GetPoints_ParsesGroupedFormat_StraightOnly()
    {
        // Only straight addresses specified
        var service = await CreateAndInitialize("LockOffset:1000\n23:(816)+");

        Assert.HasCount(1, service.Points);
        Assert.AreEqual(23, service.Points[0].Number);
        // Straight addresses only
        Assert.HasCount(1, service.Points[0].StraightAddresses);
        Assert.Contains(816, service.Points[0].StraightAddresses);
        // No diverging addresses
        Assert.IsEmpty(service.Points[0].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPoints_ParsesGroupedFormat_DivergingOnly()
    {
        // Only diverging addresses specified
        var service = await CreateAndInitialize("LockOffset:1000\n23:(816,823)-");

        Assert.HasCount(1, service.Points);
        Assert.AreEqual(23, service.Points[0].Number);
        // No straight addresses
        Assert.IsEmpty(service.Points[0].StraightAddresses);
        // Diverging addresses only
        Assert.HasCount(2, service.Points[0].DivergingAddresses);
        Assert.Contains(816, service.Points[0].DivergingAddresses);
        Assert.Contains(823, service.Points[0].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPoints_ParsesGroupedFormat_WithNegativeAddresses()
    {
        // Negative addresses flip position interpretation
        var service = await CreateAndInitialize("LockOffset:1000\n23:(-816)-(823)+");

        Assert.HasCount(1, service.Points);
        // Negative addresses are preserved
        Assert.Contains(-816, service.Points[0].DivergingAddresses);
        Assert.Contains(823, service.Points[0].StraightAddresses);
    }

    [TestMethod]
    public async Task GetPoints_ParsesBasicFormat_UsesSameAddressesForBothPositions()
    {
        // Backward compatible format uses same addresses for both positions
        var service = await CreateAndInitialize("LockOffset:1000\n1:801,802");

        Assert.HasCount(1, service.Points);
        Assert.HasCount(2, service.Points[0].StraightAddresses);
        Assert.HasCount(2, service.Points[0].DivergingAddresses);
        // Both arrays should contain the same addresses
        Assert.AreEqual(service.Points[0].StraightAddresses[0], service.Points[0].DivergingAddresses[0]);
        Assert.AreEqual(service.Points[0].StraightAddresses[1], service.Points[0].DivergingAddresses[1]);
    }

    #endregion

    #region Sub-Point Parsing Tests

    [TestMethod]
    public async Task GetPoints_ParsesSubPointSuffixes_BasicFormat()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n1:840a,843b");

        Assert.HasCount(1, service.Points);
        Assert.AreEqual(1, service.Points[0].Number);
        Assert.IsNotNull(service.Points[0].SubPointMap);
        Assert.AreEqual('a', service.Points[0].SubPointMap![840]);
        Assert.AreEqual('b', service.Points[0].SubPointMap![843]);
    }

    [TestMethod]
    public async Task GetPoints_ParsesSubPointSuffixes_GroupedFormat()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n8:(809a)+(812b)-");

        Assert.HasCount(1, service.Points);
        Assert.AreEqual(8, service.Points[0].Number);
        Assert.IsNotNull(service.Points[0].SubPointMap);
        Assert.AreEqual('a', service.Points[0].SubPointMap![809]);
        Assert.AreEqual('b', service.Points[0].SubPointMap![812]);
    }

    [TestMethod]
    public async Task GetPoints_NoSuffix_SubPointMapIsNull()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n1:840,843");

        Assert.HasCount(1, service.Points);
        Assert.IsNull(service.Points[0].SubPointMap);
    }

    [TestMethod]
    public async Task GetPoints_MixedSuffixAndPlain_OnlySuffixedInMap()
    {
        var service = await CreateAndInitialize("LockOffset:1000\n1:840a,843");

        Assert.HasCount(1, service.Points);
        Assert.IsNotNull(service.Points[0].SubPointMap);
        Assert.HasCount(1, service.Points[0].SubPointMap!);
        Assert.AreEqual('a', service.Points[0].SubPointMap![840]);
    }

    #endregion
}
