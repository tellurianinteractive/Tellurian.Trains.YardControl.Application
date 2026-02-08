using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using YardController.Web.Services.Data;

namespace YardController.Tests;

[TestClass]
public class PointDataSourceTests
{
    private string _tempFilePath = null!;
    private ILogger<IPointDataSource> _logger = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempFilePath = Path.GetTempFileName();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<IPointDataSource>();
    }

    private TextFilePointDataSource CreateDataSource(string path) =>
        new(_logger, Options.Create(new PointDataSourceSettings { Path = path }));

    [TestCleanup]
    public void TestCleanup()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    #region File Not Found Tests

    [TestMethod]
    public async Task GetPointsAsync_ReturnsEmpty_WhenFileNotFound()
    {
        var dataSource = CreateDataSource("nonexistent.txt");

        var points = await dataSource.GetPointsAsync(default);

        Assert.IsEmpty(points);
    }

    [TestMethod]
    public async Task GetTurntableTracksAsync_ReturnsEmpty_WhenFileNotFound()
    {
        var dataSource = CreateDataSource("nonexistent.txt");

        var tracks = await dataSource.GetTurntableTracksAsync(default);

        Assert.IsEmpty(tracks);
    }

    #endregion

    #region Empty File Tests

    [TestMethod]
    public async Task GetPointsAsync_ReturnsEmpty_WhenFileIsEmpty()
    {
        File.WriteAllText(_tempFilePath, "");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = await dataSource.GetPointsAsync(default);

        Assert.IsEmpty(points);
    }

    #endregion

    #region Basic Point Parsing Tests

    [TestMethod]
    public async Task GetPointsAsync_ParsesBasicFormat()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:801");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(1, points[0].Number);
        Assert.Contains(801, points[0].StraightAddresses);
        Assert.Contains(801, points[0].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPointsAsync_ParsesMultipleAddresses()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:801,802,803");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.HasCount(3, points[0].StraightAddresses);
        Assert.Contains(801, points[0].StraightAddresses);
        Assert.Contains(802, points[0].StraightAddresses);
        Assert.Contains(803, points[0].StraightAddresses);
        Assert.HasCount(3, points[0].DivergingAddresses);
        Assert.Contains(801, points[0].DivergingAddresses);
        Assert.Contains(802, points[0].DivergingAddresses);
        Assert.Contains(803, points[0].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPointsAsync_ParsesMultipleLines()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:801\n2:802\n3:803");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(3, points);
        Assert.AreEqual(1, points[0].Number);
        Assert.AreEqual(2, points[1].Number);
        Assert.AreEqual(3, points[2].Number);
    }

    #endregion

    #region LockOffset Parsing Tests

    [TestMethod]
    public async Task GetPointsAsync_ParsesLockOffset()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:801");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(1000, points[0].LockAddressOffset);
    }

    [TestMethod]
    public async Task GetPointsAsync_LockOffsetIsCaseInsensitive()
    {
        File.WriteAllText(_tempFilePath, "lockoffset:500\n1:801");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(500, points[0].LockAddressOffset);
    }

    [TestMethod]
    public async Task GetPointsAsync_LockOffsetAppliedToAllPoints()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:801\n2:802");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(2, points);
        Assert.IsTrue(points.All(t => t.LockAddressOffset == 1000));
    }

    [TestMethod]
    public async Task GetPointsAsync_WithoutLockOffset_ReturnsEmpty_DueToAddressOverlap()
    {
        // Without a LockOffset, the default is 0, which causes point addresses
        // to overlap with lock addresses (point + 0 = point), so no points are returned
        File.WriteAllText(_tempFilePath, "1:801");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.IsEmpty(points);
    }

    #endregion

    #region Address Range Parsing Tests

    [TestMethod]
    public async Task GetPointsAsync_ParsesAddressRange()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\nAdresses:1-5");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(5, points);
        Assert.AreEqual(1, points[0].Number);
        Assert.AreEqual(5, points[4].Number);
    }

    [TestMethod]
    public async Task GetPointsAsync_AddressRangeUsesNumberAsAddress()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\nAdresses:10-12");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(3, points);
        Assert.AreEqual(10, points[0].Number);
        Assert.Contains(10, points[0].StraightAddresses);
        Assert.Contains(10, points[0].DivergingAddresses);
        Assert.AreEqual(11, points[1].Number);
        Assert.Contains(11, points[1].StraightAddresses);
        Assert.Contains(11, points[1].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPointsAsync_AddressRangeWithLockOffset()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\nAdresses:1-3");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(3, points);
        Assert.IsTrue(points.All(t => t.LockAddressOffset == 1000));
    }

    [TestMethod]
    public async Task GetPointsAsync_AddressRangeIsCaseInsensitive()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\nadresses:1-3");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(3, points);
    }

    #endregion

    #region Turntable Parsing Tests

    [TestMethod]
    public async Task GetTurntableTracksAsync_ParsesTurntableConfig()
    {
        File.WriteAllText(_tempFilePath, "Turntable:1-5;1000");
        var dataSource = CreateDataSource(_tempFilePath);

        var tracks = (await dataSource.GetTurntableTracksAsync(default)).ToList();

        Assert.HasCount(5, tracks);
        Assert.AreEqual(1, tracks[0].Number);
        Assert.AreEqual(5, tracks[4].Number);
    }

    [TestMethod]
    public async Task GetTurntableTracksAsync_AppliesAddressOffset()
    {
        File.WriteAllText(_tempFilePath, "Turntable:1-3;1000");
        var dataSource = CreateDataSource(_tempFilePath);

        var tracks = (await dataSource.GetTurntableTracksAsync(default)).ToList();

        Assert.HasCount(3, tracks);
        Assert.AreEqual(1001, tracks[0].Address); // 1 + 1000
        Assert.AreEqual(1002, tracks[1].Address); // 2 + 1000
        Assert.AreEqual(1003, tracks[2].Address); // 3 + 1000
    }

    [TestMethod]
    public async Task GetTurntableTracksAsync_IsCaseInsensitive()
    {
        File.WriteAllText(_tempFilePath, "turntable:1-2;500");
        var dataSource = CreateDataSource(_tempFilePath);

        var tracks = (await dataSource.GetTurntableTracksAsync(default)).ToList();

        Assert.HasCount(2, tracks);
    }

    [TestMethod]
    public async Task GetTurntableTracksAsync_SkipsInvalidFormat()
    {
        File.WriteAllText(_tempFilePath, "Turntable:invalid");
        var dataSource = CreateDataSource(_tempFilePath);

        var tracks = (await dataSource.GetTurntableTracksAsync(default)).ToList();

        Assert.IsEmpty(tracks);
    }

    [TestMethod]
    public async Task GetPointsAsync_IgnoresTurntableLines()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:801\nTurntable:1-5;1000\n2:802");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(2, points);
        Assert.AreEqual(1, points[0].Number);
        Assert.AreEqual(2, points[1].Number);
    }

    #endregion

    #region Invalid Format Tests

    [TestMethod]
    public async Task GetPointsAsync_SkipsInvalidLines()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\ninvalid\n1:801\nalso-invalid\n2:802");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(2, points);
    }

    [TestMethod]
    public async Task GetPointsAsync_SkipsInvalidAddressRange_WithZeroValues()
    {
        // When range has invalid (zero) values, it should be skipped
        File.WriteAllText(_tempFilePath, "LockOffset:1000\nAdresses:0-5\n1:801");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        // Only the explicit point should be parsed, not the invalid range
        Assert.HasCount(1, points);
        Assert.AreEqual(1, points[0].Number);
    }

    [TestMethod]
    public async Task GetPointsAsync_SkipsInvalidAddressRange_WithInvalidEndValue()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\nAdresses:1-abc\n1:801");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        // Only the explicit point should be parsed
        Assert.HasCount(1, points);
        Assert.AreEqual(1, points[0].Number);
    }

    [TestMethod]
    public async Task GetPointsAsync_SkipsZeroAddresses()
    {
        File.WriteAllText(_tempFilePath, "1:0");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.IsEmpty(points);
    }

    #endregion

    #region Grouped Format Parsing Tests

    [TestMethod]
    public async Task GetPointsAsync_ParsesGroupedFormat_WithDifferentAddresses()
    {
        // Format: number:(addresses)-(addresses)+
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n23:(816,823)-(823)+");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(23, points[0].Number);
        // Diverging addresses (from the - group)
        Assert.HasCount(2, points[0].DivergingAddresses);
        Assert.Contains(816, points[0].DivergingAddresses);
        Assert.Contains(823, points[0].DivergingAddresses);
        // Straight addresses (from the + group)
        Assert.HasCount(1, points[0].StraightAddresses);
        Assert.Contains(823, points[0].StraightAddresses);
    }

    [TestMethod]
    public async Task GetPointsAsync_ParsesGroupedFormat_StraightOnly()
    {
        // Only straight addresses specified
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n23:(816)+");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(23, points[0].Number);
        // Straight addresses only
        Assert.HasCount(1, points[0].StraightAddresses);
        Assert.Contains(816, points[0].StraightAddresses);
        // No diverging addresses
        Assert.IsEmpty(points[0].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPointsAsync_ParsesGroupedFormat_DivergingOnly()
    {
        // Only diverging addresses specified
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n23:(816,823)-");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(23, points[0].Number);
        // No straight addresses
        Assert.IsEmpty(points[0].StraightAddresses);
        // Diverging addresses only
        Assert.HasCount(2, points[0].DivergingAddresses);
        Assert.Contains(816, points[0].DivergingAddresses);
        Assert.Contains(823, points[0].DivergingAddresses);
    }

    [TestMethod]
    public async Task GetPointsAsync_ParsesGroupedFormat_WithNegativeAddresses()
    {
        // Negative addresses flip position interpretation
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n23:(-816)-(823)+");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        // Negative addresses are preserved
        Assert.Contains(-816, points[0].DivergingAddresses);
        Assert.Contains(823, points[0].StraightAddresses);
    }

    [TestMethod]
    public async Task GetPointsAsync_ParsesBasicFormat_UsesSameAddressesForBothPositions()
    {
        // Backward compatible format uses same addresses for both positions
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:801,802");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.HasCount(2, points[0].StraightAddresses);
        Assert.HasCount(2, points[0].DivergingAddresses);
        // Both arrays should contain the same addresses
        Assert.AreEqual(points[0].StraightAddresses[0], points[0].DivergingAddresses[0]);
        Assert.AreEqual(points[0].StraightAddresses[1], points[0].DivergingAddresses[1]);
    }

    #endregion

    #region Sub-Point Parsing Tests

    [TestMethod]
    public async Task GetPointsAsync_ParsesSubPointSuffixes_BasicFormat()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:840a,843b");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(1, points[0].Number);
        Assert.IsNotNull(points[0].SubPointMap);
        Assert.AreEqual('a', points[0].SubPointMap![840]);
        Assert.AreEqual('b', points[0].SubPointMap![843]);
    }

    [TestMethod]
    public async Task GetPointsAsync_ParsesSubPointSuffixes_GroupedFormat()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n8:(809a)+(812b)-");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(8, points[0].Number);
        Assert.IsNotNull(points[0].SubPointMap);
        Assert.AreEqual('a', points[0].SubPointMap![809]);
        Assert.AreEqual('b', points[0].SubPointMap![812]);
    }

    [TestMethod]
    public async Task GetPointsAsync_NoSuffix_SubPointMapIsNull()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:840,843");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.IsNull(points[0].SubPointMap);
    }

    [TestMethod]
    public async Task GetPointsAsync_MixedSuffixAndPlain_OnlySuffixedInMap()
    {
        File.WriteAllText(_tempFilePath, "LockOffset:1000\n1:840a,843");
        var dataSource = CreateDataSource(_tempFilePath);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.IsNotNull(points[0].SubPointMap);
        Assert.AreEqual(1, points[0].SubPointMap!.Count);
        Assert.AreEqual('a', points[0].SubPointMap![840]);
    }

    #endregion

    #region InMemoryPointDataSource Tests

    [TestMethod]
    public async Task InMemory_ReturnsAddedPoints()
    {
        var dataSource = new InMemoryPointDataSource(NullLogger<InMemoryPointDataSource>.Instance);
        var point = new Point(1, [801, 802], [801, 802], 1000);

        dataSource.AddPoint(point);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(1, points[0].Number);
    }

    [TestMethod]
    public async Task InMemory_ReturnsEmpty_WhenNoPointsAdded()
    {
        var dataSource = new InMemoryPointDataSource(NullLogger<InMemoryPointDataSource>.Instance);

        var points = await dataSource.GetPointsAsync(default);

        Assert.IsEmpty(points);
    }

    [TestMethod]
    public async Task InMemory_AddPoint_WithParameters()
    {
        var dataSource = new InMemoryPointDataSource(NullLogger<InMemoryPointDataSource>.Instance);

        dataSource.AddPoint(1, [801, 802], 1000);

        var points = (await dataSource.GetPointsAsync(default)).ToList();

        Assert.HasCount(1, points);
        Assert.AreEqual(1, points[0].Number);
        Assert.AreEqual(1000, points[0].LockAddressOffset);
    }

    [TestMethod]
    public void InMemory_AddPoint_ThrowsOnDuplicateNumber()
    {
        var dataSource = new InMemoryPointDataSource(NullLogger<InMemoryPointDataSource>.Instance);
        dataSource.AddPoint(1, [801], 1000);

        Assert.ThrowsExactly<InvalidOperationException>(() => dataSource.AddPoint(1, [802], 1000));
    }

    #endregion
}
