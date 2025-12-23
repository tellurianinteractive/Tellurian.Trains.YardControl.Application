using System.Text;
using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class CharExtensionsTests
{
    #region Char SwitchState Tests

    [TestMethod]
    public void PlusCharReturnsStraightDirection()
    {
        Assert.AreEqual(SwitchDirection.Straight, '+'.SwitchState);
    }

    [TestMethod]
    public void MinusCharReturnsDivergingDirection()
    {
        Assert.AreEqual(SwitchDirection.Diverging, '-'.SwitchState);
    }

    [TestMethod]
    public void InvalidCharReturnsUndefinedDirection()
    {
        Assert.AreEqual(SwitchDirection.Undefined, 'x'.SwitchState);
        Assert.AreEqual(SwitchDirection.Undefined, '0'.SwitchState);
        Assert.AreEqual(SwitchDirection.Undefined, '='.SwitchState);
    }

    #endregion

    #region Char TrainRouteState Tests

    [TestMethod]
    public void EqualsCharReturnsSetMainState()
    {
        Assert.AreEqual(TrainRouteState.SetMain, '='.TrainRouteState);
    }

    [TestMethod]
    public void AsteriskCharReturnsSetShuntingState()
    {
        Assert.AreEqual(TrainRouteState.SetShunting, '*'.TrainRouteState);
    }

    [TestMethod]
    public void SlashCharReturnsClearState()
    {
        Assert.AreEqual(TrainRouteState.Clear, '/'.TrainRouteState);
    }

    [TestMethod]
    public void InvalidCharReturnsUndefinedTrainRouteState()
    {
        Assert.AreEqual(TrainRouteState.Undefined, '+'.TrainRouteState);
        Assert.AreEqual(TrainRouteState.Undefined, '-'.TrainRouteState);
        Assert.AreEqual(TrainRouteState.Undefined, 'x'.TrainRouteState);
    }

    #endregion

    #region Char IsSwitchCommand Tests

    [TestMethod]
    public void PlusAndMinusAreSwitchCommands()
    {
        Assert.IsTrue('+'.IsSwitchCommand);
        Assert.IsTrue('-'.IsSwitchCommand);
    }

    [TestMethod]
    public void TrainRouteCharsAreNotSwitchCommands()
    {
        Assert.IsFalse('='.IsSwitchCommand);
        Assert.IsFalse('*'.IsSwitchCommand);
        Assert.IsFalse('/'.IsSwitchCommand);
    }

    #endregion

    #region Char IsTrainPathCommand Tests

    [TestMethod]
    public void TrainRouteCharsAreTrainPathCommands()
    {
        Assert.IsTrue('='.IsTrainPathCommand);
        Assert.IsTrue('*'.IsTrainPathCommand);
        Assert.IsTrue('/'.IsTrainPathCommand);
    }

    [TestMethod]
    public void SwitchCharsAreNotTrainPathCommands()
    {
        Assert.IsFalse('+'.IsTrainPathCommand);
        Assert.IsFalse('-'.IsTrainPathCommand);
    }

    #endregion

    #region Char IsTrainRouteClearCommand Tests

    [TestMethod]
    public void SlashIsTrainRouteClearCommand()
    {
        Assert.IsTrue('/'.IsTrainRouteClearCommand);
    }

    [TestMethod]
    public void OtherCharsAreNotTrainRouteClearCommand()
    {
        Assert.IsFalse('='.IsTrainRouteClearCommand);
        Assert.IsFalse('*'.IsTrainRouteClearCommand);
        Assert.IsFalse('+'.IsTrainRouteClearCommand);
    }

    #endregion

    #region String ToIntOrZero Tests

    [TestMethod]
    public void ValidIntegerStringReturnsInteger()
    {
        Assert.AreEqual(123, "123".ToIntOrZero);
        Assert.AreEqual(1, "1".ToIntOrZero);
        Assert.AreEqual(99, "99".ToIntOrZero);
    }

    [TestMethod]
    public void EmptyOrNullStringReturnsZero()
    {
        Assert.AreEqual(0, "".ToIntOrZero);
        Assert.AreEqual(0, ((string?)null).ToIntOrZero);
    }

    [TestMethod]
    public void NonNumericStringReturnsZero()
    {
        Assert.AreEqual(0, "abc".ToIntOrZero);
        Assert.AreEqual(0, "12a".ToIntOrZero);
    }

    [TestMethod]
    public void LeadingZerosAreParsedCorrectly()
    {
        Assert.AreEqual(7, "007".ToIntOrZero);
        Assert.AreEqual(21, "021".ToIntOrZero);
    }

    #endregion

    #region String IsSwitchCommand Tests

    [TestMethod]
    public void ValidSwitchCommandStringsAreRecognized()
    {
        Assert.IsTrue("1+".IsSwitchCommand);
        Assert.IsTrue("99-".IsSwitchCommand);
        Assert.IsTrue("123+".IsSwitchCommand);
    }

    [TestMethod]
    public void InvalidSwitchCommandStringsAreRejected()
    {
        Assert.IsFalse("+".IsSwitchCommand); // Too short
        Assert.IsFalse("".IsSwitchCommand);
        Assert.IsFalse(((string?)null).IsSwitchCommand);
        Assert.IsFalse("1=".IsSwitchCommand); // Wrong ending
    }

    #endregion

    #region String IsTrainPathCommand Tests

    [TestMethod]
    public void ValidTrainPathCommandStringsAreRecognized()
    {
        Assert.IsTrue("2131=".IsTrainPathCommand);
        Assert.IsTrue("21*".IsTrainPathCommand);
        Assert.IsTrue("31/".IsTrainPathCommand);
    }

    [TestMethod]
    public void InvalidTrainPathCommandStringsAreRejected()
    {
        Assert.IsFalse("=".IsTrainPathCommand); // Too short
        Assert.IsFalse("".IsTrainPathCommand);
        Assert.IsFalse(((string?)null).IsTrainPathCommand);
        Assert.IsFalse("21+".IsTrainPathCommand); // Wrong ending
    }

    #endregion

    #region StringBuilder IsClearAllTrainPaths Tests

    [TestMethod]
    public void DoubleSlashIsClearAllTrainPaths()
    {
        var sb = new StringBuilder("//");
        Assert.IsTrue(sb.IsClearAllTrainPaths);
    }

    [TestMethod]
    public void OtherStringsAreNotClearAllTrainPaths()
    {
        Assert.IsFalse(new StringBuilder("/").IsClearAllTrainPaths);
        Assert.IsFalse(new StringBuilder("///").IsClearAllTrainPaths);
        Assert.IsFalse(new StringBuilder("==").IsClearAllTrainPaths);
        Assert.IsFalse(new StringBuilder("").IsClearAllTrainPaths);
    }

    #endregion

    #region StringBuilder IsSwitchCommand Tests

    [TestMethod]
    public void StringBuilderSwitchCommandsAreRecognized()
    {
        Assert.IsTrue(new StringBuilder("1+").IsSwitchCommand);
        Assert.IsTrue(new StringBuilder("99-").IsSwitchCommand);
    }

    [TestMethod]
    public void StringBuilderNonSwitchCommandsAreRejected()
    {
        Assert.IsFalse(new StringBuilder("+").IsSwitchCommand);
        Assert.IsFalse(new StringBuilder("1=").IsSwitchCommand);
    }

    #endregion

    #region StringBuilder IsTrainPathCommand Tests

    [TestMethod]
    public void StringBuilderTrainPathCommandsAreRecognized()
    {
        Assert.IsTrue(new StringBuilder("2131=").IsTrainPathCommand);
        Assert.IsTrue(new StringBuilder("31/").IsTrainPathCommand);
    }

    [TestMethod]
    public void StringBuilderNonTrainPathCommandsAreRejected()
    {
        Assert.IsFalse(new StringBuilder("=").IsTrainPathCommand);
        Assert.IsFalse(new StringBuilder("21+").IsTrainPathCommand);
    }

    #endregion

    #region StringBuilder CommandString Tests

    [TestMethod]
    public void CommandStringReturnsAndClearsBuffer()
    {
        var sb = new StringBuilder("2131=");
        var result = sb.CommandString;

        Assert.AreEqual("2131=", result);
        Assert.AreEqual(0, sb.Length);
    }

    #endregion

    #region StringBuilder All Tests

    [TestMethod]
    public void AllReturnsTrueWhenAllCharsMatch()
    {
        Assert.IsTrue(new StringBuilder("//").All('/'));
        Assert.IsTrue(new StringBuilder("===").All('=', 3));
    }

    [TestMethod]
    public void AllReturnsFalseWhenCharsDontMatch()
    {
        Assert.IsFalse(new StringBuilder("/=").All('/'));
        Assert.IsFalse(new StringBuilder("//").All('/', 3));
    }

    #endregion

    #region SignalDivider Tests

    [TestMethod]
    public void SignalDividerIsPeriod()
    {
        Assert.AreEqual('.', char.SignalDivider);
    }

    #endregion
}
