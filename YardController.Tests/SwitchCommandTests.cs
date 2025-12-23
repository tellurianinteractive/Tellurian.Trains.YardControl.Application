using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class SwitchCommandTests
{
    #region Create Tests

    [TestMethod]
    public void Create_ReturnsCommandWithAddresses()
    {
        var command = SwitchCommand.Create(1, SwitchDirection.Straight, [801, 802]);

        Assert.AreEqual(1, command.Number);
        Assert.AreEqual(SwitchDirection.Straight, command.Direction);
        Assert.AreEqual(2, command.Addresses.Count());
        Assert.IsTrue(command.Addresses.Contains(801));
        Assert.IsTrue(command.Addresses.Contains(802));
    }

    [TestMethod]
    public void Create_WithEmptyAddresses_ReturnsCommandWithNoAddresses()
    {
        var command = SwitchCommand.Create(1, SwitchDirection.Straight, []);

        Assert.AreEqual(1, command.Number);
        Assert.AreEqual(0, command.Addresses.Count());
    }

    #endregion

    #region Address Handling Tests

    [TestMethod]
    public void Create_AddsAddressesCorrectly()
    {
        var command = SwitchCommand.Create(1, SwitchDirection.Straight, [801, 802]);

        Assert.AreEqual(2, command.Addresses.Count());
        Assert.IsTrue(command.Addresses.Contains(801));
        Assert.IsTrue(command.Addresses.Contains(802));
    }

    [TestMethod]
    public void NewCommand_HasNoAddresses()
    {
        var command = new SwitchCommand(1, SwitchDirection.Straight);

        Assert.AreEqual(0, command.Addresses.Count());
    }

    #endregion

    #region Undefined Tests

    [TestMethod]
    public void Undefined_ReturnsCommandWithZeroNumberAndUndefinedDirection()
    {
        var undefined = SwitchCommand.Undefined;

        Assert.AreEqual(0, undefined.Number);
        Assert.AreEqual(SwitchDirection.Undefined, undefined.Direction);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_ForUndefinedDirection()
    {
        var command = new SwitchCommand(1, SwitchDirection.Undefined);
        Assert.IsTrue(command.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsFalse_ForDefinedDirection()
    {
        var straight = new SwitchCommand(1, SwitchDirection.Straight);
        var diverging = new SwitchCommand(1, SwitchDirection.Diverging);

        Assert.IsFalse(straight.IsUndefined);
        Assert.IsFalse(diverging.IsUndefined);
    }

    #endregion

    #region Equals Tests

    [TestMethod]
    public void Equals_ReturnsTrue_ForIdenticalCommands()
    {
        var cmd1 = SwitchCommand.Create(1, SwitchDirection.Straight, [801, 802]);
        var cmd2 = SwitchCommand.Create(1, SwitchDirection.Straight, [801, 802]);

        Assert.IsTrue(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentNumbers()
    {
        var cmd1 = SwitchCommand.Create(1, SwitchDirection.Straight, [801]);
        var cmd2 = SwitchCommand.Create(2, SwitchDirection.Straight, [801]);

        Assert.IsFalse(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentDirections()
    {
        var cmd1 = SwitchCommand.Create(1, SwitchDirection.Straight, [801]);
        var cmd2 = SwitchCommand.Create(1, SwitchDirection.Diverging, [801]);

        Assert.IsFalse(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentAddresses()
    {
        var cmd1 = SwitchCommand.Create(1, SwitchDirection.Straight, [801]);
        var cmd2 = SwitchCommand.Create(1, SwitchDirection.Straight, [802]);

        Assert.IsFalse(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentAddressOrder()
    {
        var cmd1 = SwitchCommand.Create(1, SwitchDirection.Straight, [801, 802]);
        var cmd2 = SwitchCommand.Create(1, SwitchDirection.Straight, [802, 801]);

        // SequenceEqual checks order
        Assert.IsFalse(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForNull()
    {
        var cmd = SwitchCommand.Create(1, SwitchDirection.Straight, [801]);

        Assert.IsFalse(cmd.Equals(null));
    }

    [TestMethod]
    public void StaticEquals_WorksCorrectly()
    {
        var cmd1 = SwitchCommand.Create(1, SwitchDirection.Straight, [801]);
        var cmd2 = SwitchCommand.Create(1, SwitchDirection.Straight, [801]);

        Assert.IsTrue(SwitchCommand.Equals(cmd1, cmd2));
    }

    #endregion

    #region ToSwitchCommand String Extension Tests

    [TestMethod]
    public void ToSwitchCommand_ParsesValidPlusCommand()
    {
        var command = "1+".ToSwitchCommand();

        Assert.AreEqual(1, command.Number);
        Assert.AreEqual(SwitchDirection.Straight, command.Direction);
    }

    [TestMethod]
    public void ToSwitchCommand_ParsesValidMinusCommand()
    {
        var command = "99-".ToSwitchCommand();

        Assert.AreEqual(99, command.Number);
        Assert.AreEqual(SwitchDirection.Diverging, command.Direction);
    }

    [TestMethod]
    public void ToSwitchCommand_ReturnsUndefined_ForNull()
    {
        var command = ((string?)null).ToSwitchCommand();

        Assert.IsTrue(command.IsUndefined);
    }

    [TestMethod]
    public void ToSwitchCommand_ReturnsUndefined_ForEmptyString()
    {
        var command = "".ToSwitchCommand();

        Assert.IsTrue(command.IsUndefined);
    }

    [TestMethod]
    public void ToSwitchCommand_ReturnsUndefined_ForSingleChar()
    {
        var command = "+".ToSwitchCommand();

        Assert.IsTrue(command.IsUndefined);
    }

    [TestMethod]
    public void ToSwitchCommand_ReturnsUndefined_ForInvalidDirection()
    {
        var command = "1x".ToSwitchCommand();

        Assert.AreEqual(SwitchDirection.Undefined, command.Direction);
    }

    [TestMethod]
    public void ToSwitchCommand_ParsesMultiDigitNumber()
    {
        var command = "123+".ToSwitchCommand();

        Assert.AreEqual(123, command.Number);
    }

    #endregion

    #region ToTurnoutCommands Tests

    [TestMethod]
    public void ToTurnoutCommands_CreatesTurnoutCommandsForEachAddress()
    {
        var command = SwitchCommand.Create(1, SwitchDirection.Straight, [801, 802, 803]);

        var turnoutCommands = command.ToTurnoutCommands().ToList();

        Assert.AreEqual(3, turnoutCommands.Count);
    }

    [TestMethod]
    public void ToTurnoutCommands_ReturnsEmpty_ForNoAddresses()
    {
        var command = new SwitchCommand(1, SwitchDirection.Straight);

        var turnoutCommands = command.ToTurnoutCommands().ToList();

        Assert.AreEqual(0, turnoutCommands.Count);
    }

    #endregion

    #region ToString Tests

    [TestMethod]
    public void ToString_FormatsCorrectly()
    {
        var command = SwitchCommand.Create(1, SwitchDirection.Straight, [801, 802]);

        var result = command.ToString();

        Assert.IsTrue(result.Contains("1"));
        Assert.IsTrue(result.Contains("Straight"));
        Assert.IsTrue(result.Contains("801"));
        Assert.IsTrue(result.Contains("802"));
    }

    #endregion
}
