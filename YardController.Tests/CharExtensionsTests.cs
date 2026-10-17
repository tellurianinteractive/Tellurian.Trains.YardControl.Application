using System.Text;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Tests;

[TestClass]
public class CharExtensionsTests
{
    #region Char IsPointCommand Tests

    [TestMethod]
    public void PlusAndMinusArePointCommands()
    {
        Assert.IsTrue('+'.IsPointCommand);
        Assert.IsTrue('-'.IsPointCommand);
    }

    [TestMethod]
    public void TrainRouteCharsAreNotPointCommands()
    {
        Assert.IsFalse('#'.IsPointCommand);
        Assert.IsFalse('*'.IsPointCommand);
        Assert.IsFalse('/'.IsPointCommand);
        Assert.IsFalse('\x1b'.IsPointCommand);
    }

    #endregion

    #region Char IsTrainRouteCommand Tests

    [TestMethod]
    public void TrainRouteCharsAreTrainRouteCommands()
    {
        Assert.IsTrue('#'.IsTrainRouteCommand);
        Assert.IsTrue('*'.IsTrainRouteCommand);
        Assert.IsTrue('\x1b'.IsTrainRouteCommand);
        Assert.IsTrue('/'.IsTrainRouteCommand);
    }

    [TestMethod]
    public void EqualsIsNotTrainRouteCommand()
    {
        Assert.IsFalse('='.IsTrainRouteCommand);
    }

    [TestMethod]
    public void EqualsIsTrainNumberSeparator()
    {
        Assert.IsTrue('='.IsTrainNumberSeparator);
    }

    [TestMethod]
    public void PointCharsAreNotTrainRouteCommands()
    {
        Assert.IsFalse('+'.IsTrainRouteCommand);
        Assert.IsFalse('-'.IsTrainRouteCommand);
    }

    #endregion

    #region Char IsTrainRouteTeardownCommand Tests

    [TestMethod]
    public void SlashIsTrainRouteTeardownCommand()
    {
        Assert.IsTrue('/'.IsTrainRouteTeardownCommand);
    }

    [TestMethod]
    public void EscapeIsTrainRouteTeardownCommand()
    {
        Assert.IsTrue('\x1b'.IsTrainRouteTeardownCommand);
    }

    [TestMethod]
    public void EqualsIsNotTrainRouteTeardownCommand()
    {
        Assert.IsFalse('='.IsTrainRouteTeardownCommand);
    }

    [TestMethod]
    public void OtherCharsAreNotTrainRouteTeardownCommand()
    {
        Assert.IsFalse('#'.IsTrainRouteTeardownCommand);
        Assert.IsFalse('*'.IsTrainRouteTeardownCommand);
        Assert.IsFalse('+'.IsTrainRouteTeardownCommand);
    }

    [TestMethod]
    public void EscapeMapsToCancel()
    {
        Assert.AreEqual(TrainRouteState.Cancel, '\x1b'.TrainRouteState);
    }

    [TestMethod]
    public void SlashMapsToClear()
    {
        Assert.AreEqual(TrainRouteState.Clear, '/'.TrainRouteState);
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

    #region String IsPointCommand Tests

    [TestMethod]
    public void ValidPointCommandStringsAreRecognized()
    {
        Assert.IsTrue("1+".IsPointCommand);
        Assert.IsTrue("99-".IsPointCommand);
        Assert.IsTrue("123+".IsPointCommand);
    }

    [TestMethod]
    public void InvalidPointCommandStringsAreRejected()
    {
        Assert.IsFalse("+".IsPointCommand); // Too short
        Assert.IsFalse("".IsPointCommand);
        Assert.IsFalse(((string?)null).IsPointCommand);
        Assert.IsFalse("1#".IsPointCommand); // Wrong ending
    }

    #endregion

    #region String IsTrainRouteCommand Tests

    [TestMethod]
    public void ValidTrainRouteCommandStringsAreRecognized()
    {
        Assert.IsTrue("2131#".IsTrainRouteCommand);
        Assert.IsTrue("21*".IsTrainRouteCommand);
        Assert.IsTrue("31/".IsTrainRouteCommand);
        Assert.IsTrue("31\x1b".IsTrainRouteCommand);
    }

    [TestMethod]
    public void InvalidTrainRouteCommandStringsAreRejected()
    {
        Assert.IsFalse("#".IsTrainRouteCommand); // Too short
        Assert.IsFalse("".IsTrainRouteCommand);
        Assert.IsFalse(((string?)null).IsTrainRouteCommand);
        Assert.IsFalse("21+".IsTrainRouteCommand); // Wrong ending
    }

    #endregion

    #region StringBuilder IsClearAllTrainRoutes Tests

    [TestMethod]
    public void DoubleSlashIsClearAllTrainRoutes()
    {
        var sb = new StringBuilder("//");
        Assert.IsTrue(sb.IsClearAllTrainRoutes);
    }

    [TestMethod]
    public void OtherStringsAreNotClearAllTrainRoutes()
    {
        Assert.IsFalse(new StringBuilder("/").IsClearAllTrainRoutes);
        Assert.IsFalse(new StringBuilder("///").IsClearAllTrainRoutes);
        Assert.IsFalse(new StringBuilder("##").IsClearAllTrainRoutes);
        Assert.IsFalse(new StringBuilder("").IsClearAllTrainRoutes);
    }

    #endregion

    #region StringBuilder IsAllPointsStraight Tests

    [TestMethod]
    public void DoubleEqualsIsAllPointsStraight()
    {
        var sb = new StringBuilder("==");
        Assert.IsTrue(sb.IsAllPointsStraight);
    }

    [TestMethod]
    public void OtherStringsAreNotAllPointsStraight()
    {
        Assert.IsFalse(new StringBuilder("=").IsAllPointsStraight);
        Assert.IsFalse(new StringBuilder("===").IsAllPointsStraight);
        Assert.IsFalse(new StringBuilder("//").IsAllPointsStraight);
        Assert.IsFalse(new StringBuilder("").IsAllPointsStraight);
    }

    #endregion

    #region StringBuilder IsCancelAllTrainRoutes Tests

    [TestMethod]
    public void DoubleEscapeIsCancelAllTrainRoutes()
    {
        var sb = new StringBuilder("\x1b\x1b");
        Assert.IsTrue(sb.IsCancelAllTrainRoutes);
    }

    [TestMethod]
    public void OtherStringsAreNotCancelAllTrainRoutes()
    {
        Assert.IsFalse(new StringBuilder("\x1b").IsCancelAllTrainRoutes);
        Assert.IsFalse(new StringBuilder("\x1b\x1b\x1b").IsCancelAllTrainRoutes);
        Assert.IsFalse(new StringBuilder("//").IsCancelAllTrainRoutes);
        Assert.IsFalse(new StringBuilder("").IsCancelAllTrainRoutes);
    }

    #endregion

    #region StringBuilder IsPointCommand Tests

    [TestMethod]
    public void StringBuilderPointCommandsAreRecognized()
    {
        Assert.IsTrue(new StringBuilder("1+").IsPointCommand);
        Assert.IsTrue(new StringBuilder("99-").IsPointCommand);
    }

    [TestMethod]
    public void StringBuilderNonPointCommandsAreRejected()
    {
        Assert.IsFalse(new StringBuilder("+").IsPointCommand);
        Assert.IsFalse(new StringBuilder("1#").IsPointCommand);
    }

    [TestMethod]
    public void StringBuilderPrefixedCommandsAreNotPointCommands()
    {
        Assert.IsFalse(new StringBuilder("A5+").IsPointCommand, "A prefixes a shunting area.");
        Assert.IsFalse(new StringBuilder("S22+").IsPointCommand, "S prefixes a signal.");
    }

    #endregion

    #region StringBuilder IsShuntingAreaCommand Tests

    [TestMethod]
    public void StringBuilderShuntingAreaCommandsAreRecognized()
    {
        Assert.IsTrue(new StringBuilder("A5+").IsShuntingAreaCommand);
        Assert.IsTrue(new StringBuilder("A12-").IsShuntingAreaCommand);
    }

    [TestMethod]
    public void StringBuilderNonShuntingAreaCommandsAreRejected()
    {
        Assert.IsFalse(new StringBuilder("A+").IsShuntingAreaCommand, "An area number is required.");
        Assert.IsFalse(new StringBuilder("A5#").IsShuntingAreaCommand);
        Assert.IsFalse(new StringBuilder("S5+").IsShuntingAreaCommand, "S now prefixes a signal.");
        Assert.IsFalse(new StringBuilder("5+").IsShuntingAreaCommand);
    }

    #endregion

    #region StringBuilder IsSignalCommand Tests

    [TestMethod]
    public void StringBuilderSignalCommandsAreRecognized()
    {
        Assert.IsTrue(new StringBuilder("S22+").IsSignalCommand);
        Assert.IsTrue(new StringBuilder("S7-").IsSignalCommand);
    }

    [TestMethod]
    public void StringBuilderNonSignalCommandsAreRejected()
    {
        Assert.IsFalse(new StringBuilder("S+").IsSignalCommand, "A signal number is required.");
        Assert.IsFalse(new StringBuilder("S22#").IsSignalCommand);
        Assert.IsFalse(new StringBuilder("A22+").IsSignalCommand, "A prefixes a shunting area.");
        Assert.IsFalse(new StringBuilder("22+").IsSignalCommand);
    }

    #endregion

    #region StringBuilder IsTrainRouteCommand Tests

    [TestMethod]
    public void StringBuilderTrainRouteCommandsAreRecognized()
    {
        Assert.IsTrue(new StringBuilder("2131#").IsTrainRouteCommand);
        Assert.IsTrue(new StringBuilder("31/").IsTrainRouteCommand);
    }

    [TestMethod]
    public void StringBuilderNonTrainRouteCommandsAreRejected()
    {
        Assert.IsFalse(new StringBuilder("#").IsTrainRouteCommand);
        Assert.IsFalse(new StringBuilder("21+").IsTrainRouteCommand);
    }

    #endregion

    #region StringBuilder CommandString Tests

    [TestMethod]
    public void CommandStringReturnsAndClearsBuffer()
    {
        var sb = new StringBuilder("2131#");
        var result = sb.CommandString;

        Assert.AreEqual("2131#", result);
        Assert.AreEqual(0, sb.Length);
    }

    #endregion

    #region StringBuilder All Tests

    [TestMethod]
    public void AllReturnsTrueWhenAllCharsMatch()
    {
        Assert.IsTrue(new StringBuilder("//").All('/'));
        Assert.IsTrue(new StringBuilder("###").All('#', 3));
    }

    [TestMethod]
    public void AllReturnsFalseWhenCharsDontMatch()
    {
        Assert.IsFalse(new StringBuilder("/#").All('/'));
        Assert.IsFalse(new StringBuilder("//").All('/', 3));
    }

    #endregion

    #region String ToAddressWithSubPoint Tests

    [TestMethod]
    public void PlainAddressReturnsNoSubPoint()
    {
        var (address, subPoint) = "840".ToAddressWithSubPoint();
        Assert.AreEqual(840, address);
        Assert.IsNull(subPoint);
    }

    [TestMethod]
    public void SuffixedAddressReturnsSubPoint()
    {
        var (address, subPoint) = "840a".ToAddressWithSubPoint();
        Assert.AreEqual(840, address);
        Assert.AreEqual('a', subPoint);
    }

    [TestMethod]
    public void UpperCaseSuffixIsNormalisedToLower()
    {
        var (address, subPoint) = "843B".ToAddressWithSubPoint();
        Assert.AreEqual(843, address);
        Assert.AreEqual('b', subPoint);
    }

    [TestMethod]
    public void NegativeAddressWithSuffixReturnsSuffix()
    {
        var (address, subPoint) = "-843b".ToAddressWithSubPoint();
        Assert.AreEqual(-843, address);
        Assert.AreEqual('b', subPoint);
    }

    [TestMethod]
    public void EmptyStringReturnsZeroAndNull()
    {
        var (address, subPoint) = "".ToAddressWithSubPoint();
        Assert.AreEqual(0, address);
        Assert.IsNull(subPoint);
    }

    [TestMethod]
    public void NullStringReturnsZeroAndNull()
    {
        var (address, subPoint) = ((string?)null).ToAddressWithSubPoint();
        Assert.AreEqual(0, address);
        Assert.IsNull(subPoint);
    }

    #endregion

    #region String ToAddressWithSubPointAndKind Tests

    [TestMethod]
    public void PlainAddress_DefaultsToCommand()
    {
        var (address, subPoint, kind) = "840".ToAddressWithSubPointAndKind();
        Assert.AreEqual(840, address);
        Assert.IsNull(subPoint);
        Assert.AreEqual(AccessoryMessageKind.Command, kind);
    }

    [TestMethod]
    public void AddressWithCsuffix_IsCommand()
    {
        var (address, subPoint, kind) = "840c".ToAddressWithSubPointAndKind();
        Assert.AreEqual(840, address);
        Assert.IsNull(subPoint);
        Assert.AreEqual(AccessoryMessageKind.Command, kind);
    }

    [TestMethod]
    public void AddressWithNsuffix_IsNotification()
    {
        var (address, subPoint, kind) = "567n".ToAddressWithSubPointAndKind();
        Assert.AreEqual(567, address);
        Assert.IsNull(subPoint);
        Assert.AreEqual(AccessoryMessageKind.Notification, kind);
    }

    [TestMethod]
    public void AddressWithCNsuffix_IsBoth()
    {
        var (address, subPoint, kind) = "888cn".ToAddressWithSubPointAndKind();
        Assert.AreEqual(888, address);
        Assert.IsNull(subPoint);
        Assert.AreEqual(AccessoryMessageKind.Both, kind);
    }

    [TestMethod]
    public void AddressWithNCsuffix_IsBoth()
    {
        var (address, subPoint, kind) = "888nc".ToAddressWithSubPointAndKind();
        Assert.AreEqual(888, address);
        Assert.IsNull(subPoint);
        Assert.AreEqual(AccessoryMessageKind.Both, kind);
    }

    [TestMethod]
    public void NegativeAddressWithNsuffix_Works()
    {
        var (address, subPoint, kind) = "-567n".ToAddressWithSubPointAndKind();
        Assert.AreEqual(-567, address);
        Assert.IsNull(subPoint);
        Assert.AreEqual(AccessoryMessageKind.Notification, kind);
    }

    [TestMethod]
    public void AddressWithSubPointAndNsuffix_Works()
    {
        var (address, subPoint, kind) = "840an".ToAddressWithSubPointAndKind();
        Assert.AreEqual(840, address);
        Assert.AreEqual('a', subPoint);
        Assert.AreEqual(AccessoryMessageKind.Notification, kind);
    }

    [TestMethod]
    public void AddressWithSubPointAndCNsuffix_Works()
    {
        var (address, subPoint, kind) = "840acn".ToAddressWithSubPointAndKind();
        Assert.AreEqual(840, address);
        Assert.AreEqual('a', subPoint);
        Assert.AreEqual(AccessoryMessageKind.Both, kind);
    }

    [TestMethod]
    public void EmptyString_ReturnsDefaultCommand()
    {
        var (address, subPoint, kind) = "".ToAddressWithSubPointAndKind();
        Assert.AreEqual(0, address);
        Assert.IsNull(subPoint);
        Assert.AreEqual(AccessoryMessageKind.Command, kind);
    }

    [TestMethod]
    public void NullString_ReturnsDefaultCommand()
    {
        var (address, subPoint, kind) = ((string?)null).ToAddressWithSubPointAndKind();
        Assert.AreEqual(0, address);
        Assert.IsNull(subPoint);
        Assert.AreEqual(AccessoryMessageKind.Command, kind);
    }

    [TestMethod]
    public void ToAddressWithSubPoint_StillWorksWithSuffixes()
    {
        // Existing method should return correct address/subPoint, ignoring message kind
        var (address, subPoint) = "840an".ToAddressWithSubPoint();
        Assert.AreEqual(840, address);
        Assert.AreEqual('a', subPoint);
    }

    #endregion

    [TestMethod]
    public void ToEdgeSignal_ReadsTheDotAsTheLayoutEdge()
    {
        var beyond = "21.".ToEdgeSignal();
        Assert.IsNotNull(beyond, "A trailing dot names the track running out past the signal");
        Assert.AreEqual(21, beyond.Value.Signal);
        Assert.IsTrue(beyond.Value.Beyond);

        var fromEdge = ".21".ToEdgeSignal();
        Assert.IsNotNull(fromEdge, "A leading dot names the track running in to the signal");
        Assert.AreEqual(21, fromEdge.Value.Signal);
        Assert.IsFalse(fromEdge.Value.Beyond);
    }

    [TestMethod]
    public void ToEdgeSignal_RefusesAnythingThatIsNotAnEdgeForm()
    {
        Assert.IsNull("2131".ToEdgeSignal(), "A plain signal pair names no edge");
        Assert.IsNull("21.31".ToEdgeSignal(), "A composite route has a signal on both sides of the dot");
        Assert.IsNull(".".ToEdgeSignal());
        Assert.IsNull("..".ToEdgeSignal());
        Assert.IsNull("2a.".ToEdgeSignal(), "Only digits name a signal");
        Assert.IsNull(((string?)null).ToEdgeSignal());
    }
}
