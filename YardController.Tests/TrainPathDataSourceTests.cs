using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class TrainPathDataSourceTests
{
    private string _tempFilePath = null!;
    private ILogger<ITrainPathDataSource> _logger = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempFilePath = Path.GetTempFileName();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ITrainPathDataSource>();
    }

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
    public async Task GetTrainPathCommands_ReturnsEmpty_WhenFileNotFound()
    {
        var dataSource = new TextFileTrainPathDataSource(_logger, "nonexistent.txt");

        var commands = await dataSource.GetTrainPathCommandsAsync(default);

        Assert.AreEqual(0, commands.Count());
    }

    #endregion

    #region Empty File Tests

    [TestMethod]
    public async Task GetTrainPathCommands_ReturnsEmpty_WhenFileIsEmpty()
    {
        File.WriteAllText(_tempFilePath, "");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = await dataSource.GetTrainPathCommandsAsync(default);

        Assert.AreEqual(0, commands.Count());
    }

    [TestMethod]
    public async Task GetTrainPathCommands_ReturnsEmpty_WhenFileHasOnlyWhitespace()
    {
        File.WriteAllText(_tempFilePath, "   \n\n   \n");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = await dataSource.GetTrainPathCommandsAsync(default);

        Assert.AreEqual(0, commands.Count());
    }

    #endregion

    #region Basic Format Parsing Tests

    [TestMethod]
    public async Task GetTrainPathCommands_ParsesBasicFormat()
    {
        File.WriteAllText(_tempFilePath, "21-31:1+,3-");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        Assert.AreEqual(1, commands.Count);
        Assert.AreEqual(21, commands[0].FromSignal);
        Assert.AreEqual(31, commands[0].ToSignal);
        Assert.AreEqual(2, commands[0].SwitchCommands.Count());
    }

    [TestMethod]
    public async Task GetTrainPathCommands_ParsesMultipleLines()
    {
        File.WriteAllText(_tempFilePath, "21-31:1+,3-\n31-41:5+,7-");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        Assert.AreEqual(2, commands.Count);
        Assert.AreEqual(21, commands[0].FromSignal);
        Assert.AreEqual(31, commands[1].FromSignal);
    }

    [TestMethod]
    public async Task GetTrainPathCommands_ParsesSingleSwitchCommand()
    {
        File.WriteAllText(_tempFilePath, "21-31:1+");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        Assert.AreEqual(1, commands.Count);
        Assert.AreEqual(1, commands[0].SwitchCommands.Count());
    }

    #endregion

    #region Composite Format Parsing Tests

    [TestMethod]
    public async Task GetTrainPathCommands_ParsesCompositeFormat()
    {
        // First define base routes, then composite
        File.WriteAllText(_tempFilePath, "21-31:1+,3-\n31-41:5+,7-\n21-41:21.31.41");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        Assert.AreEqual(3, commands.Count);

        var compositeRoute = commands[2];
        Assert.AreEqual(21, compositeRoute.FromSignal);
        Assert.AreEqual(41, compositeRoute.ToSignal);
        // Should have combined switches from 21-31 and 31-41
        Assert.AreEqual(4, compositeRoute.SwitchCommands.Count());
    }

    [TestMethod]
    public async Task GetTrainPathCommands_CompositeRoute_SkipsIfBaseRouteNotFound()
    {
        // Composite route references non-existent route
        File.WriteAllText(_tempFilePath, "21-31:1+\n21-51:21.31.51");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        // Only the first route should be parsed, composite fails
        Assert.AreEqual(1, commands.Count);
    }

    [TestMethod]
    public async Task GetTrainPathCommands_CompositeRoute_WithThreeSegments()
    {
        File.WriteAllText(_tempFilePath, "21-31:1+\n31-41:2+\n41-51:3+\n21-51:21.31.41.51");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        Assert.AreEqual(4, commands.Count);

        var compositeRoute = commands[3];
        Assert.AreEqual(21, compositeRoute.FromSignal);
        Assert.AreEqual(51, compositeRoute.ToSignal);
        Assert.AreEqual(3, compositeRoute.SwitchCommands.Count());
    }

    #endregion

    #region Invalid Format Tests

    [TestMethod]
    public async Task GetTrainPathCommands_SkipsInvalidFormat_MissingColon()
    {
        File.WriteAllText(_tempFilePath, "21-31,1+,3-");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        Assert.AreEqual(0, commands.Count);
    }

    [TestMethod]
    public async Task GetTrainPathCommands_SkipsInvalidFormat_MissingHyphen()
    {
        File.WriteAllText(_tempFilePath, "2131:1+,3-");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        Assert.AreEqual(0, commands.Count);
    }

    [TestMethod]
    public async Task GetTrainPathCommands_ContinuesAfterInvalidLine()
    {
        File.WriteAllText(_tempFilePath, "invalid-line\n21-31:1+");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        Assert.AreEqual(1, commands.Count);
        Assert.AreEqual(21, commands[0].FromSignal);
    }

    #endregion

    #region Switch Command Handling in Composite Routes

    [TestMethod]
    public async Task GetTrainPathCommands_CompositeRoute_CombinesSwitchCommands()
    {
        // Routes share switch 2 - Distinct() is called but compares by reference
        // so duplicates may still exist. This test documents actual behavior.
        File.WriteAllText(_tempFilePath, "21-31:1+,2+\n31-41:2+,3+\n21-41:21.31.41");
        var dataSource = new TextFileTrainPathDataSource(_logger, _tempFilePath);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        var compositeRoute = commands[2];
        // Distinct() uses SwitchCommand's Equals which checks Number, Direction, AND Addresses
        // Since addresses are empty and different instances, they are not deduplicated
        Assert.AreEqual(4, compositeRoute.SwitchCommands.Count());
    }

    #endregion

    #region InMemoryTrainPathDataSource Tests

    [TestMethod]
    public async Task InMemory_ReturnsAddedCommands()
    {
        var dataSource = new InMemoryTrainPathDataSource();
        var command = new TrainRouteCommand(21, 31, TrainRouteState.SetMain,
            [new SwitchCommand(1, SwitchDirection.Straight)]);

        dataSource.AddTrainPathCommand(command);

        var commands = (await dataSource.GetTrainPathCommandsAsync(default)).ToList();

        Assert.AreEqual(1, commands.Count);
        Assert.AreEqual(21, commands[0].FromSignal);
    }

    [TestMethod]
    public async Task InMemory_ReturnsEmpty_WhenNoCommandsAdded()
    {
        var dataSource = new InMemoryTrainPathDataSource();

        var commands = await dataSource.GetTrainPathCommandsAsync(default);

        Assert.AreEqual(0, commands.Count());
    }

    #endregion
}
