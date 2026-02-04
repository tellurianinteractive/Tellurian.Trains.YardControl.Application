using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class PointCommandTests
{
    #region Create Tests

    [TestMethod]
    public void Create_ReturnsCommandWithAddresses()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801, 802]);

        Assert.AreEqual(1, command.Number);
        Assert.AreEqual(PointPosition.Straight, command.Position);
        Assert.HasCount(2, command.Addresses);
        Assert.Contains(801, command.Addresses);
        Assert.Contains(802, command.Addresses);
    }

    [TestMethod]
    public void Create_WithEmptyAddresses_ReturnsCommandWithNoAddresses()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, []);

        Assert.AreEqual(1, command.Number);
        Assert.IsEmpty(command.Addresses);
    }

    #endregion

    #region Undefined Tests

    [TestMethod]
    public void Undefined_ReturnsCommandWithZeroNumberAndUndefinedPosition()
    {
        var undefined = PointCommand.Undefined;

        Assert.AreEqual(0, undefined.Number);
        Assert.AreEqual(PointPosition.Undefined, undefined.Position);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_ForUndefinedPosition()
    {
        var command = new PointCommand(1, PointPosition.Undefined);
        Assert.IsTrue(command.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsFalse_ForDefinedPosition()
    {
        var straight = new PointCommand(1, PointPosition.Straight);
        var diverging = new PointCommand(1, PointPosition.Diverging);

        Assert.IsFalse(straight.IsUndefined);
        Assert.IsFalse(diverging.IsUndefined);
    }

    #endregion

    #region Equals Tests

    [TestMethod]
    public void Equals_ReturnsTrue_ForIdenticalCommands()
    {
        var cmd1 = PointCommand.Create(1, PointPosition.Straight, [801, 802]);
        var cmd2 = PointCommand.Create(1, PointPosition.Straight, [801, 802]);

        Assert.IsTrue(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentNumbers()
    {
        var cmd1 = PointCommand.Create(1, PointPosition.Straight, [801]);
        var cmd2 = PointCommand.Create(2, PointPosition.Straight, [801]);

        Assert.IsFalse(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentPositions()
    {
        var cmd1 = PointCommand.Create(1, PointPosition.Straight, [801]);
        var cmd2 = PointCommand.Create(1, PointPosition.Diverging, [801]);

        Assert.IsFalse(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentAddresses()
    {
        var cmd1 = PointCommand.Create(1, PointPosition.Straight, [801]);
        var cmd2 = PointCommand.Create(1, PointPosition.Straight, [802]);

        Assert.IsFalse(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentAddressOrder()
    {
        var cmd1 = PointCommand.Create(1, PointPosition.Straight, [801, 802]);
        var cmd2 = PointCommand.Create(1, PointPosition.Straight, [802, 801]);

        // SequenceEqual checks order
        Assert.IsFalse(cmd1.Equals(cmd2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForNull()
    {
        var cmd = PointCommand.Create(1, PointPosition.Straight, [801]);

        Assert.IsFalse(cmd.Equals(null));
    }

    [TestMethod]
    public void StaticEquals_WorksCorrectly()
    {
        var cmd1 = PointCommand.Create(1, PointPosition.Straight, [801]);
        var cmd2 = PointCommand.Create(1, PointPosition.Straight, [801]);

        Assert.IsTrue(PointCommand.Equals(cmd1, cmd2));
    }

    #endregion

    #region ToPointCommand String Extension Tests

    [TestMethod]
    public void ToPointCommand_ParsesValidPlusCommand()
    {
        var command = "1+".ToPointCommand();

        Assert.AreEqual(1, command.Number);
        Assert.AreEqual(PointPosition.Straight, command.Position);
    }

    [TestMethod]
    public void ToPointCommand_ParsesValidMinusCommand()
    {
        var command = "99-".ToPointCommand();

        Assert.AreEqual(99, command.Number);
        Assert.AreEqual(PointPosition.Diverging, command.Position);
    }

    [TestMethod]
    public void ToPointCommand_ReturnsUndefined_ForNull()
    {
        var command = ((string?)null).ToPointCommand();

        Assert.IsTrue(command.IsUndefined);
    }

    [TestMethod]
    public void ToPointCommand_ReturnsUndefined_ForEmptyString()
    {
        var command = "".ToPointCommand();

        Assert.IsTrue(command.IsUndefined);
    }

    [TestMethod]
    public void ToPointCommand_ReturnsUndefined_ForSingleChar()
    {
        var command = "+".ToPointCommand();

        Assert.IsTrue(command.IsUndefined);
    }

    [TestMethod]
    public void ToPointCommand_ReturnsUndefined_ForInvalidPosition()
    {
        var command = "1x".ToPointCommand();

        Assert.AreEqual(PointPosition.Undefined, command.Position);
    }

    [TestMethod]
    public void ToPointCommand_ParsesMultiDigitNumber()
    {
        var command = "123+".ToPointCommand();

        Assert.AreEqual(123, command.Number);
    }

    #endregion

    #region ToLocoNetCommands Tests

    [TestMethod]
    public void ToLocoNetCommands_CreatesLocoNetCommandsForEachAddress()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801, 802, 803]);

        var locoNetCommands = command.ToLocoNetCommands().ToList();

        Assert.HasCount(3, locoNetCommands);
    }

    [TestMethod]
    public void ToLocoNetCommands_ReturnsEmpty_ForNoAddresses()
    {
        var command = new PointCommand(1, PointPosition.Straight);

        var locoNetCommands = command.ToLocoNetCommands().ToList();

        Assert.IsEmpty(locoNetCommands);
    }

    #endregion

    #region Lock/Unlock Tests

    [TestMethod]
    public void AlsoLockAndUnlock_ReturnsTrue_WhenStraightAndHasLockOffset()
    {
        var command = new PointCommand(1, PointPosition.Straight, 1000);

        Assert.IsTrue(command.AlsoLock);
        Assert.IsTrue(command.AlsoUnlock);
    }

    [TestMethod]
    public void AlsoLockAndUnlock_ReturnsTrue_WhenDivergingAndHasLockOffset()
    {
        var command = new PointCommand(1, PointPosition.Diverging, 1000);

        Assert.IsTrue(command.AlsoLock);
        Assert.IsTrue(command.AlsoUnlock);
    }

    [TestMethod]
    public void AlsoLock_ReturnsFalse_WhenNoLockOffset()
    {
        var command = new PointCommand(1, PointPosition.Straight);

        Assert.IsFalse(command.AlsoLock);
        Assert.IsFalse(command.AlsoUnlock);
    }

    [TestMethod]
    public void LockAddresses_ReturnsAddressesWithOffset_WhenAlsoLock()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801, 802], 1000);

        var lockAddresses = command.LockAddresses.ToList();

        Assert.HasCount(2, lockAddresses);
        Assert.Contains(1801, lockAddresses); // 801 + 1000
        Assert.Contains(1802, lockAddresses); // 802 + 1000
    }

    [TestMethod]
    public void LockAddresses_ReturnsEmpty_WhenNoLockOffset()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801, 802]);

        var lockAddresses = command.LockAddresses.ToList();

        Assert.IsEmpty(lockAddresses);
    }

    #endregion

    #region ToLocoNetLockCommands Tests

    [TestMethod]
    public void ToLocoNetLockCommands_GeneratesCloseCommands()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801], 1000);

        var lockCommands = command.ToLocoNetLockCommands().ToList();

        Assert.HasCount(1, lockCommands);
        // Lock commands use Close (which sets to straight/locked position)
    }

    [TestMethod]
    public void ToLocoNetLockCommands_ReturnsEmpty_WhenNoLockAddresses()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801]);

        var lockCommands = command.ToLocoNetLockCommands().ToList();

        Assert.IsEmpty(lockCommands);
    }

    [TestMethod]
    public void ToLocoNetLockCommands_SkipsUndefined()
    {
        var command = PointCommand.Undefined;

        var lockCommands = command.ToLocoNetLockCommands().ToList();

        Assert.IsEmpty(lockCommands);
    }

    #endregion

    #region ToLocoNetUnlockCommands Tests

    [TestMethod]
    public void ToLocoNetUnlockCommands_GeneratesThrowCommands()
    {
        var command = PointCommand.Create(1, PointPosition.Diverging, [801], 1000);

        var unlockCommands = command.ToLocoNetUnlockCommands().ToList();

        Assert.HasCount(1, unlockCommands);
        // Unlock commands use Throw (which sets to diverging/unlocked position)
    }

    [TestMethod]
    public void ToLocoNetUnlockCommands_ReturnsEmpty_WhenNoLockAddresses()
    {
        var command = PointCommand.Create(1, PointPosition.Diverging, [801]);

        var unlockCommands = command.ToLocoNetUnlockCommands().ToList();

        Assert.IsEmpty(unlockCommands);
    }

    [TestMethod]
    public void ToLocoNetUnlockCommands_SkipsUndefined()
    {
        var command = PointCommand.Undefined;

        var unlockCommands = command.ToLocoNetUnlockCommands().ToList();

        Assert.IsEmpty(unlockCommands);
    }

    #endregion

    #region AsLockOrUnlockCommand Tests

    [TestMethod]
    public void AsLockOrUnlockCommand_CreatesCommandWithLockAddresses()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801, 802], 1000);

        var lockCommand = command.AsLockOrUnlockCommand;

        Assert.AreEqual(1, lockCommand.Number);
        Assert.AreEqual(PointPosition.Straight, lockCommand.Position);
        // The AsLockOrUnlockCommand uses the LockAddresses as the main addresses
        Assert.HasCount(2, lockCommand.Addresses);
    }

    #endregion

    #region Negative Address Tests

    [TestMethod]
    public void ToLocoNetCommands_HandlesNegativeAddresses()
    {
        // Negative addresses flip the position
        var command = PointCommand.Create(1, PointPosition.Straight, [-801]);

        var locoNetCommands = command.ToLocoNetCommands().ToList();

        Assert.HasCount(1, locoNetCommands);
        // Negative address should produce a valid LocoNet command
        Assert.IsNotNull(locoNetCommands[0]);
    }

    [TestMethod]
    public void ToLocoNetCommands_HandlesMultipleNegativeAddresses()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [-801, -802]);

        var locoNetCommands = command.ToLocoNetCommands().ToList();

        Assert.HasCount(2, locoNetCommands);
    }

    #endregion

    #region IsOnRoute Tests

    [TestMethod]
    public void PointCommand_DefaultsToOnRoute()
    {
        var command = new PointCommand(1, PointPosition.Straight);

        Assert.IsTrue(command.IsOnRoute);
    }

    [TestMethod]
    public void PointCommand_CanBeMarkedOffRoute()
    {
        var command = new PointCommand(1, PointPosition.Straight, null, false);

        Assert.IsFalse(command.IsOnRoute);
    }

    [TestMethod]
    public void Create_DefaultsToOnRoute()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801]);

        Assert.IsTrue(command.IsOnRoute);
    }

    [TestMethod]
    public void Create_CanBeMarkedOffRoute()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801], null, false);

        Assert.IsFalse(command.IsOnRoute);
    }

    [TestMethod]
    public void ToPointCommand_ParsesXPrefix_AsOffRoute()
    {
        var command = "x1+".ToPointCommand();

        Assert.AreEqual(1, command.Number);
        Assert.AreEqual(PointPosition.Straight, command.Position);
        Assert.IsFalse(command.IsOnRoute);
    }

    [TestMethod]
    public void ToPointCommand_ParsesUppercaseXPrefix_AsOffRoute()
    {
        var command = "X33-".ToPointCommand();

        Assert.AreEqual(33, command.Number);
        Assert.AreEqual(PointPosition.Diverging, command.Position);
        Assert.IsFalse(command.IsOnRoute);
    }

    [TestMethod]
    public void ToPointCommand_WithoutXPrefix_IsOnRoute()
    {
        var command = "25+".ToPointCommand();

        Assert.AreEqual(25, command.Number);
        Assert.AreEqual(PointPosition.Straight, command.Position);
        Assert.IsTrue(command.IsOnRoute);
    }

    [TestMethod]
    public void ToPointCommand_XPrefixOnly_ReturnsUndefined()
    {
        var command = "x".ToPointCommand();

        Assert.IsTrue(command.IsUndefined);
    }

    [TestMethod]
    public void ToPointCommand_XPrefixWithSingleChar_ReturnsUndefined()
    {
        var command = "x+".ToPointCommand();

        Assert.IsTrue(command.IsUndefined);
    }

    #endregion
}
