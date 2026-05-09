using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using YardController.Web.Hardware;

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
        var command1 = PointCommand.Create(1, PointPosition.Straight, [801]);
        var command2 = PointCommand.Create(1, PointPosition.Straight, [802]);

        Assert.IsFalse(command1.Equals(command2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForDifferentAddressOrder()
    {
        var command1 = PointCommand.Create(1, PointPosition.Straight, [801, 802]);
        var command2 = PointCommand.Create(1, PointPosition.Straight, [802, 801]);

        // SequenceEqual checks order
        Assert.IsFalse(command1.Equals(command2));
    }

    [TestMethod]
    public void Equals_ReturnsFalse_ForNull()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801]);

        Assert.IsFalse(command.Equals(null));
    }

    [TestMethod]
    public void StaticEquals_WorksCorrectly()
    {
        var command1 = PointCommand.Create(1, PointPosition.Straight, [801]);
        var command2 = PointCommand.Create(1, PointPosition.Straight, [801]);

        Assert.IsTrue(PointCommand.Equals(command1, command2));
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

    #region ToAccessoryCommands Tests

    [TestMethod]
    public void ToAccessoryCommands_CreatesLocoNetCommandsForEachAddress()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801, 802, 803]);

        var locoNetCommands = command.ToAccessoryCommands().ToList();

        Assert.HasCount(3, locoNetCommands);
    }

    [TestMethod]
    public void ToAccessoryCommands_ReturnsEmpty_ForNoAddresses()
    {
        var command = new PointCommand(1, PointPosition.Straight);

        var locoNetCommands = command.ToAccessoryCommands().ToList();

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

    #region ToLockAccessoryCommands Tests

    [TestMethod]
    public void ToLockAccessoryCommands_GeneratesCloseCommands()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801], 1000);

        var lockCommands = command.ToLockAccessoryCommands().ToList();

        Assert.HasCount(1, lockCommands);
        // Lock commands use Close (which sets to straight/locked position)
    }

    [TestMethod]
    public void ToLockAccessoryCommands_ReturnsEmpty_WhenNoLockAddresses()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [801]);

        var lockCommands = command.ToLockAccessoryCommands().ToList();

        Assert.IsEmpty(lockCommands);
    }

    [TestMethod]
    public void ToLockAccessoryCommands_SkipsUndefined()
    {
        var command = PointCommand.Undefined;

        var lockCommands = command.ToLockAccessoryCommands().ToList();

        Assert.IsEmpty(lockCommands);
    }

    #endregion

    #region ToUnlockAccessoryCommands Tests

    [TestMethod]
    public void ToUnlockAccessoryCommands_GeneratesThrowCommands()
    {
        var command = PointCommand.Create(1, PointPosition.Diverging, [801], 1000);

        var unlockCommands = command.ToUnlockAccessoryCommands().ToList();

        Assert.HasCount(1, unlockCommands);
        // Unlock commands use Throw (which sets to diverging/unlocked position)
    }

    [TestMethod]
    public void ToUnlockAccessoryCommands_ReturnsEmpty_WhenNoLockAddresses()
    {
        var command = PointCommand.Create(1, PointPosition.Diverging, [801]);

        var unlockCommands = command.ToUnlockAccessoryCommands().ToList();

        Assert.IsEmpty(unlockCommands);
    }

    [TestMethod]
    public void ToUnlockAccessoryCommands_SkipsUndefined()
    {
        var command = PointCommand.Undefined;

        var unlockCommands = command.ToUnlockAccessoryCommands().ToList();

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
    public void ToAccessoryCommands_HandlesNegativeAddresses()
    {
        // Negative addresses flip the position
        var command = PointCommand.Create(1, PointPosition.Straight, [-801]);

        var locoNetCommands = command.ToAccessoryCommands().ToList();

        Assert.HasCount(1, locoNetCommands);
        // Negative address should produce a valid LocoNet command
        Assert.IsNotNull(locoNetCommands[0]);
    }

    [TestMethod]
    public void ToAccessoryCommands_HandlesMultipleNegativeAddresses()
    {
        var command = PointCommand.Create(1, PointPosition.Straight, [-801, -802]);

        var locoNetCommands = command.ToAccessoryCommands().ToList();

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

    #region ExpandWithSlaves Tests

    [TestMethod]
    public void ExpandWithSlaves_NoSlaves_ReturnsMasterOnly()
    {
        var points = new Dictionary<int, Point>
        {
            [10] = new Point(10, [813], [822], 1000)
        };
        var master = PointCommand.Create(10, PointPosition.Diverging, [822]);
        var expanded = master.ExpandWithSlaves(points);
        Assert.HasCount(1, expanded);
        Assert.AreSame(master, expanded[0]);
    }

    [TestMethod]
    public void ExpandWithSlaves_AsymmetricSlave_CascadesOnlyMatchingPosition()
    {
        // Point 10: when set to -, also set 8-. 10+ has no slave.
        var points = new Dictionary<int, Point>
        {
            [10] = new Point(10, [813], [822], 1000, Slaves: [new SlaveCommand(PointPosition.Diverging, 8, PointPosition.Diverging)]),
            [8] = new Point(8, [809], [812], 1000)
        };

        var master = PointCommand.Create(10, PointPosition.Diverging, [822]);
        var expanded = master.ExpandWithSlaves(points);
        Assert.HasCount(2, expanded);
        Assert.AreEqual(10, expanded[0].Number);
        Assert.AreEqual(8, expanded[1].Number);
        Assert.AreEqual(PointPosition.Diverging, expanded[1].Position);

        // 10+ should not cascade
        var masterPlus = PointCommand.Create(10, PointPosition.Straight, [813]);
        var expandedPlus = masterPlus.ExpandWithSlaves(points);
        Assert.HasCount(1, expandedPlus);
    }

    [TestMethod]
    public void ExpandWithSlaves_TransitiveCascadeTerminates()
    {
        // 10- → 8-, 8- → 5-. Setting 10- should cascade to both 8- and 5-.
        var points = new Dictionary<int, Point>
        {
            [10] = new Point(10, [813], [822], 1000, Slaves: [new SlaveCommand(PointPosition.Diverging, 8, PointPosition.Diverging)]),
            [8] = new Point(8, [809], [812], 1000, Slaves: [new SlaveCommand(PointPosition.Diverging, 5, PointPosition.Diverging)]),
            [5] = new Point(5, [800], [801], 1000)
        };

        var master = PointCommand.Create(10, PointPosition.Diverging, [822]);
        var expanded = master.ExpandWithSlaves(points);
        Assert.HasCount(3, expanded);
        Assert.IsTrue(expanded.Any(c => c.Number == 5 && c.Position == PointPosition.Diverging));
    }

    [TestMethod]
    public void ExpandWithSlaves_ContradictoryCascadeRejected()
    {
        // 10- → 8+, 8+ → 10+. Trying to set 10- creates a contradiction on point 10.
        var points = new Dictionary<int, Point>
        {
            [10] = new Point(10, [813], [822], 1000, Slaves: [new SlaveCommand(PointPosition.Diverging, 8, PointPosition.Straight)]),
            [8] = new Point(8, [809], [812], 1000, Slaves: [new SlaveCommand(PointPosition.Straight, 10, PointPosition.Straight)])
        };

        var master = PointCommand.Create(10, PointPosition.Diverging, [822]);
        Assert.Throws<InvalidPointCascadeException>(() => master.ExpandWithSlaves(points));
    }

    [TestMethod]
    public void ExpandWithSlaves_SymmetricRedundantRulesAccepted()
    {
        // 10- → 8-, 8+ → 10+. Both rules express the same constraint (contrapositives).
        // Setting 10- visits {10:-, 8:-}; no conflict.
        var points = new Dictionary<int, Point>
        {
            [10] = new Point(10, [813], [822], 1000, Slaves: [new SlaveCommand(PointPosition.Diverging, 8, PointPosition.Diverging)]),
            [8] = new Point(8, [809], [812], 1000, Slaves: [new SlaveCommand(PointPosition.Straight, 10, PointPosition.Straight)])
        };

        var masterMinus = PointCommand.Create(10, PointPosition.Diverging, [822]);
        var expanded1 = masterMinus.ExpandWithSlaves(points);
        Assert.HasCount(2, expanded1);

        var masterPlus = PointCommand.Create(8, PointPosition.Straight, [809]);
        var expanded2 = masterPlus.ExpandWithSlaves(points);
        Assert.HasCount(2, expanded2);
        Assert.IsTrue(expanded2.Any(c => c.Number == 10 && c.Position == PointPosition.Straight));
    }

    [TestMethod]
    public void ExpandWithSlaves_MissingSlavePoint_Throws()
    {
        // Point 10 references point 99, which isn't in the points dictionary.
        var points = new Dictionary<int, Point>
        {
            [10] = new Point(10, [813], [822], 1000, Slaves: [new SlaveCommand(PointPosition.Diverging, 99, PointPosition.Diverging)])
        };
        var master = PointCommand.Create(10, PointPosition.Diverging, [822]);
        Assert.Throws<InvalidPointCascadeException>(() => master.ExpandWithSlaves(points));
    }

    #endregion
}
