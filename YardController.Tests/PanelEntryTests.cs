using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Tests;

/// <summary>
/// Command-first entry on a button panel (Specifications/InputDevices.md §6). The dot rule is
/// what these mostly exercise: dropping the mandatory separator is only safe if a complete number
/// that is also the start of a longer one really does hold.
/// </summary>
[TestClass]
public class PanelEntryTests
{
    /// <summary>Munkeröd's numbering: every signal two digits, so no number is a prefix of another.</summary>
    private static PanelNumbers TwoDigitStation => new(
        Signals: new HashSet<int> { 11, 21, 22, 31, 33, 35, 41, 51, 61, 71 },
        Points: new HashSet<int> { 1, 3, 7, 10, 11, 12, 27 },
        ShuntingAreas: new HashSet<int> { 2, 5 });

    /// <summary>A contrived station where a complete number is also the start of a longer one.</summary>
    private static PanelNumbers OverlappingStation => new(
        Signals: new HashSet<int> { 11, 111, 113, 13, 31 },
        Points: new HashSet<int> { 1, 11 },
        ShuntingAreas: new HashSet<int> { 1 });

    private static PanelEntry TwoDigit() => new(TwoDigitStation);

    private static PanelEntryResult PressAll(PanelEntry entry, string keys)
    {
        var result = new PanelEntryResult(PanelEntryOutcome.Accepted);
        foreach (var key in keys) result = entry.Press(key);
        return result;
    }

    [TestMethod]
    public void AFreshEntryIsIdle()
    {
        var entry = TwoDigit();
        Assert.IsTrue(entry.IsIdle);
        Assert.AreEqual(PanelCommand.None, entry.Command);
        Assert.AreEqual(ButtonSet.Main, entry.ButtonSet);
        Assert.IsFalse(entry.IsComplete);
    }

    [TestMethod]
    public void KeysWithNoCommandChosenPassThroughToTheKeypadGrammar()
    {
        // §5.4: before a command is chosen the right pad alone behaves as a numeric keypad.
        var entry = TwoDigit();
        foreach (var key in "0123456789*.#/-")
            Assert.AreEqual(PanelEntryOutcome.PassThrough, entry.Press(key).Outcome, $"'{key}' should pass through");
    }

    [TestMethod]
    public void TwoDigitSignalsNeedNoDotAnywhere()
    {
        // §6.3: "2131⏎ → 21 → 31. No dot needed anywhere."
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        PressAll(entry, "2131");

        CollectionAssert.AreEqual(new[] { "21", "31" }, entry.Parameters.ToArray());
        Assert.AreEqual("", entry.Current);
        Assert.IsTrue(entry.IsComplete);

        var result = entry.Press('#');
        Assert.AreEqual(PanelEntryOutcome.Completed, result.Outcome);
        Assert.AreEqual("21.31#", result.KeypadKeys);
    }

    [TestMethod]
    public void ACompleteNumberThatStartsALongerOneHolds()
    {
        // §6.3: "11" with 11, 111, 113 present — complete, but longer matches remain.
        var entry = new PanelEntry(OverlappingStation);
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        PressAll(entry, "11");

        Assert.AreEqual(0, entry.Parameters.Count, "11 must not settle while 111 and 113 remain");
        Assert.AreEqual("11", entry.Current);
        CollectionAssert.AreEqual(new[] { 11, 111, 113 }, entry.Candidates.ToArray());
    }

    [TestMethod]
    public void ADotSettlesANumberThatWouldOtherwiseHold()
    {
        // §6.3: "11.31⏎ — the dot settles on 11."
        var entry = new PanelEntry(OverlappingStation);
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        PressAll(entry, "11.31");

        CollectionAssert.AreEqual(new[] { "11", "31" }, entry.Parameters.ToArray());
        Assert.AreEqual("11.31#", entry.Press('#').KeypadKeys);
    }

    [TestMethod]
    public void ALongerNumberResolvesAsSoonAsNothingElseStartsWithIt()
    {
        // §6.3: "111 — resolves at once; nothing else starts with 111."
        var entry = new PanelEntry(OverlappingStation);
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        PressAll(entry, "111");

        CollectionAssert.AreEqual(new[] { "111" }, entry.Parameters.ToArray());
        Assert.AreEqual("", entry.Current);
    }

    [TestMethod]
    public void EntryIsRejectedAsSoonAsNoNumberMatches()
    {
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        entry.Press('2');

        var result = entry.Press('9');    // no signal starts with 29
        Assert.AreEqual(PanelEntryOutcome.Rejected, result.Outcome);
        Assert.AreEqual(PanelEntryRejection.NoSuchNumber, result.Rejection);
        Assert.AreEqual("2", entry.Current, "the refused digit must not be taken");
    }

    [TestMethod]
    public void ASingleParameterCommandKeepsItsNumberForTheSendKey()
    {
        // A point command wants one number, so resolving it must not advance to a second.
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.PointDiverging);
        PressAll(entry, "27");

        Assert.AreEqual(0, entry.Parameters.Count);
        Assert.AreEqual("27", entry.Current);
        Assert.IsTrue(entry.IsComplete);
        Assert.AreEqual("27-", entry.Press('#').KeypadKeys);
    }

    [TestMethod]
    public void ARouteTakesViaSignalsBeyondTheTwoRequired()
    {
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        PressAll(entry, "213135");

        CollectionAssert.AreEqual(new[] { "21", "31", "35" }, entry.Parameters.ToArray());
        Assert.AreEqual("21.31.35#", entry.Press('#').KeypadKeys);
    }

    [TestMethod]
    public void ARouteFromASignalToItselfIsNotComplete()
    {
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        PressAll(entry, "2121");

        Assert.IsFalse(entry.IsComplete);
        Assert.AreEqual(PanelEntryRejection.Incomplete, entry.Press('#').Rejection);
    }

    [TestMethod]
    public void SendingBeforeTheCommandIsWholeIsRefused()
    {
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        PressAll(entry, "21");

        var result = entry.Press('#');
        Assert.AreEqual(PanelEntryOutcome.Rejected, result.Outcome);
        Assert.AreEqual(PanelEntryRejection.Incomplete, result.Rejection);
    }

    [TestMethod]
    [DataRow(PanelCommand.PointStraight, "==")]
    [DataRow(PanelCommand.SignalStop, "**")]
    [DataRow(PanelCommand.ShuntingRouteRelease, "//")]
    public void TheWildcardAbsorbsTheDoubledKeypadCommands(PanelCommand command, string expected)
    {
        // §6.2: the wildcard replaces ==, ** and //, and ␛␛ likewise.
        var entry = TwoDigit();
        entry.ChooseCommand(command);

        Assert.AreEqual(PanelEntryOutcome.Accepted, entry.Press('*').Outcome);
        Assert.IsTrue(entry.IsComplete);
        Assert.AreEqual(expected, entry.Press('#').KeypadKeys);
    }

    [TestMethod]
    public void CancelAllIsTheWildcardOnTheCancelKey()
    {
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteCancel);
        entry.Press('*');
        Assert.AreEqual("\x1b\x1b", entry.Press('#').KeypadKeys);
    }

    [TestMethod]
    [DataRow(PanelCommand.PointDiverging)]
    [DataRow(PanelCommand.SignalGo)]
    public void TheWildcardIsRefusedWhereAllOfThemIsNotAnOperation(PanelCommand command)
    {
        // §6.2: neither "all points diverging" nor "all signals go" is meaningful.
        var entry = TwoDigit();
        entry.ChooseCommand(command);

        var result = entry.Press('*');
        Assert.AreEqual(PanelEntryOutcome.Rejected, result.Outcome);
        Assert.AreEqual(PanelEntryRejection.WildcardNotAccepted, result.Rejection);
    }

    [TestMethod]
    public void TheWildcardMustBeTheFirstPressOfAParameter()
    {
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.PointStraight);
        entry.Press('1');

        Assert.AreEqual(PanelEntryRejection.WildcardNotFirst, entry.Press('*').Rejection);
    }

    [TestMethod]
    public void AShuntingAreaTakesANumberButNotYetTheWildcard()
    {
        // §12: the area commands reach the yard, but their wildcard form is not built.
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.ShuntingAreaRelease);
        Assert.AreEqual(PanelEntryRejection.WildcardNotImplemented, entry.Press('*').Rejection);

        entry.Press('5');
        Assert.AreEqual("A5+", entry.Press('#').KeypadKeys);
    }

    [TestMethod]
    [DataRow(PanelCommand.PointLock)]
    [DataRow(PanelCommand.PointUnlock)]
    [DataRow(PanelCommand.SignalLockStop)]
    [DataRow(PanelCommand.SignalUnlockStop)]
    public void ACommandWithNoPipelineIsRefusedAtTheKey(PanelCommand command)
    {
        // §12: these are drawn on the alternate buttons but nothing holds a point or a signal
        // against the interlocking yet. Refusing at the key beats appearing to work.
        var entry = TwoDigit();
        var result = entry.ChooseCommand(command);

        Assert.AreEqual(PanelEntryOutcome.Rejected, result.Outcome);
        Assert.AreEqual(PanelEntryRejection.NotImplemented, result.Rejection);
        Assert.IsTrue(entry.IsIdle);
    }

    [TestMethod]
    public void AnotherCommandKeyIsRefusedOnceOneIsChosen()
    {
        // §10.2: once a command is chosen, the other command keys dim.
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteSet);

        var result = entry.ChooseCommand(PanelCommand.PointStraight);
        Assert.AreEqual(PanelEntryRejection.CommandAlreadyChosen, result.Rejection);
        Assert.AreEqual(PanelCommand.TrainRouteSet, entry.Command);
    }

    [TestMethod]
    public void TheButtonSetSwitchesAndComesBackByItselfAfterAnAlternateCommand()
    {
        // D10: the alternate set is momentary — it never persists past one command.
        var entry = TwoDigit();
        Assert.AreEqual(PanelEntryOutcome.ButtonSetChanged, entry.ChooseCommand(PanelCommand.ButtonsAlternate).Outcome);
        Assert.AreEqual(ButtonSet.Alternate, entry.ButtonSet);

        entry.ChooseCommand(PanelCommand.ShuntingAreaRelease);
        entry.Press('5');
        entry.Press('#');

        Assert.AreEqual(ButtonSet.Main, entry.ButtonSet, "sending returns the pad to the main buttons");
    }

    [TestMethod]
    public void TheButtonSetIsInertWhileACommandIsBeingEntered()
    {
        // §5.3: switching mid-entry could only mean abandoning it, so the key reports instead.
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteSet);

        var result = entry.ChooseCommand(PanelCommand.ButtonsAlternate);
        Assert.AreEqual(PanelEntryRejection.ButtonSetLocked, result.Rejection);
        Assert.AreEqual(ButtonSet.Main, entry.ButtonSet);
    }

    [TestMethod]
    public void CancellingAnEntryDoesNotReturnToTheMainButtons()
    {
        // §5.3: so a mistyped number can simply be retyped on the set it was typed on.
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.ButtonsAlternate);
        entry.ChooseCommand(PanelCommand.ShuntingAreaLock);
        entry.Press('5');

        Assert.AreEqual(PanelEntryOutcome.Cancelled, entry.Press('/').Outcome);
        Assert.IsTrue(entry.IsIdle);
        Assert.AreEqual(ButtonSet.Alternate, entry.ButtonSet);
    }

    [TestMethod]
    public void UndoTakesBackOneDigitAtATime()
    {
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        PressAll(entry, "2131");

        entry.Press('-');
        CollectionAssert.AreEqual(new[] { "21" }, entry.Parameters.ToArray());
        Assert.AreEqual("3", entry.Current, "a settled parameter comes back as digits");

        entry.Press('-');
        Assert.AreEqual("", entry.Current);

        entry.Press('-');
        CollectionAssert.AreEqual(Array.Empty<string>(), entry.Parameters.ToArray());
        Assert.AreEqual("2", entry.Current);
    }

    [TestMethod]
    public void UndoPastTheFirstDigitTakesBackTheCommandItself()
    {
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.PointStraight);
        entry.Press('1');
        entry.Press('-');
        entry.Press('-');

        Assert.AreEqual(PanelCommand.None, entry.Command);
        Assert.IsTrue(entry.IsIdle);
    }

    [TestMethod]
    public void ATrainNumberIsNotMatchedAgainstAnything()
    {
        // A train number is whatever the timetable says, so entry cannot narrow it.
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainAdvance);
        PressAll(entry, "2145");

        Assert.AreEqual("2145", entry.Current);
        Assert.AreEqual(0, entry.Candidates.Count);
        Assert.IsTrue(entry.IsComplete);
        Assert.AreEqual("=2145#", entry.Press('#').KeypadKeys);
    }

    [TestMethod]
    public void AcceptingATrainNamesTheSignalItComesInAtAndTheTrain()
    {
        // The number goes onto the run in from the layout edge: .{signal}={train}⏎
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainApproved);
        PressAll(entry, "21");
        Assert.AreEqual(1, entry.Parameters.Count, "the signal settles, then the train number is typed");

        PressAll(entry, "2145");
        Assert.AreEqual(".21=2145#", entry.Press('#').KeypadKeys);
    }

    [TestMethod]
    public void CandidatesAreEmptyUntilADigitIsTyped()
    {
        // §8.2 blinks every signal still matching; with nothing typed that would be all of them.
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.SignalGo);
        Assert.AreEqual(0, entry.Candidates.Count);

        entry.Press('3');
        CollectionAssert.AreEqual(new[] { 31, 33, 35 }, entry.Candidates.ToArray());
    }

    [TestMethod]
    public void PointsAndSignalsAreMatchedAgainstTheirOwnNumbers()
    {
        // Signal 51 exists and no point number starts with 5, so the same digit narrows
        // differently depending on what the command asked for.
        var signals = TwoDigit();
        signals.ChooseCommand(PanelCommand.SignalStop);
        signals.Press('5');
        CollectionAssert.AreEqual(new[] { 51 }, signals.Candidates.ToArray());

        var points = TwoDigit();
        points.ChooseCommand(PanelCommand.PointStraight);
        Assert.AreEqual(PanelEntryRejection.NoSuchNumber, points.Press('5').Rejection);
    }

    [TestMethod]
    public void SwitchingStationAbandonsWhateverWasBeingEntered()
    {
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteSet);
        PressAll(entry, "21");

        entry.UseNumbers(OverlappingStation);
        Assert.IsTrue(entry.IsIdle);
    }

    [TestMethod]
    public void SettingARouteCarriesBothEndsAndItsOwnTerminator()
    {
        foreach (var (command, terminator) in new[]
        {
            (PanelCommand.TrainRouteSet, "#"),
            (PanelCommand.ShuntingRouteSet, "*"),
        })
        {
            var entry = TwoDigit();
            entry.ChooseCommand(command);
            PressAll(entry, "2131");
            Assert.AreEqual($"21.31{terminator}", entry.Press('#').KeypadKeys, $"{command}");
        }
    }

    [TestMethod]
    [DataRow(PanelCommand.TrainRouteCancel, "\x1b")]
    [DataRow(PanelCommand.ShuntingRouteRelease, "/")]
    public void TearingARouteDownNamesOnlyWhereItEnds(PanelCommand command, string terminator)
    {
        // A route is identified by its destination, exactly as the keypad's {signal}/ form is: the
        // pipeline builds TrainRouteCommand(0, toSignal, ...) and never looks at a from-signal, so
        // asking the operator for one would be a number typed for nothing.
        var entry = TwoDigit();
        entry.ChooseCommand(command);
        PressAll(entry, "31");

        Assert.AreEqual(0, entry.Parameters.Count, "one signal stays in hand for the send key");
        Assert.AreEqual("31", entry.Current);
        Assert.IsTrue(entry.IsComplete, "a destination alone is a whole command");
        Assert.AreEqual($"31{terminator}", entry.Press('#').KeypadKeys);
    }

    [TestMethod]
    public void WhatTheKeysShowAndWhatTheEntryAcceptsNeverDisagree()
    {
        // The panel darkens a key by asking IsPressLegal, and then the press itself goes to
        // Press(). If those two ever part company, a dark key still does something — which is how
        // the divider slipped through on a teardown. Walked over every command and every stage of
        // entry rather than trusting the two to be kept in step by hand.
        foreach (var command in Enum.GetValues<PanelCommand>())
        {
            if (command.SwitchesButtonSet || !command.IsImplemented) continue;

            foreach (var typed in new[] { "", "3", "31", "3131" })
            {
                foreach (var key in "0123456789*.#/-+")
                {
                    var entry = TwoDigit();
                    if (entry.ChooseCommand(command).Outcome != PanelEntryOutcome.Accepted) continue;
                    PressAll(entry, typed);

                    var expected = entry.IsPressLegal(key);
                    var actual = entry.Press(key).Outcome != PanelEntryOutcome.Rejected;
                    Assert.AreEqual(expected, actual,
                        $"{command} after '{typed}': the key says legal={expected} but the press says {actual} for '{key}'");
                }
            }
        }
    }

    [TestMethod]
    public void ATeardownTakesNoViaSignals()
    {
        // Nothing runs "through" a teardown: only the destination is used.
        var entry = TwoDigit();
        entry.ChooseCommand(PanelCommand.TrainRouteCancel);
        PressAll(entry, "31");

        Assert.AreEqual(PanelEntryRejection.NothingToSettle, entry.Press('.').Rejection);
    }
}
