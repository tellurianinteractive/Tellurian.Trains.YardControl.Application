# Operator Input Devices

**Stream Deck Module (32 keys) as an alternative to the numeric keypad**

Status: implemented, except where marked · Applies to: `YardController.Web`, `YardController.Panel`

---

## 1. Purpose and scope

The application is operated today with a standard USB numeric keypad. This specification defines
support for an **Elgato Stream Deck Module (32 keys)** as an *alternative* input device, and the
changes to the Blazor UI that both devices share.

Two principles govern everything below.

1. **The numeric keypad remains fully supported and its grammar is unchanged.** A Stream Deck is not
   a prerequisite for operating a yard. Every existing keystroke sequence continues to work exactly
   as it does today.
2. **The two devices are mutually exclusive per station.** A station is configured for one or the
   other. They are not intended to be used simultaneously, and no attempt is made to reconcile
   concurrent input from both.

Out of scope: hardware signal control for the new signal commands (§9), physical panel construction,
and any change to LocoNet or Z21 communication.

---

## 2. Decisions

| # | Decision |
|---|---|
| D1 | The Stream Deck is driven over its **documented USB HID protocol**. The Elgato Stream Deck application is not installed and not required. |
| D2 | The device is selected by configuration (`InputDevice`), per station. Default is `NumericKeypad`. |
| D3 | The left 4×4 is a command pad carrying **two sets of buttons**, main and alternate; the right 4×4 replicates the **numeric keypad layout**. |
| D4 | Deck entry is **command-first**: `{command}{parameters}⏎`. The keypad keeps its existing postfix grammar. |
| D5 | `✳` entered where a number is expected means **all**, replacing the doubled commands `==`, `**`, `//`. |
| D6 | Signal numbers are matched **incrementally**; `.` is only needed to settle a number that is also the prefix of a longer one. |
| D7 | Partial command entry is published as an **event**, so the yard view can show it character by character. This applies to **both** input devices. |
| D8 | Point locks are released **immediately** when a train has cleared the route, and **after the configured delay** when an operator cancels. |
| D9 | The **alternate buttons** are reached with the key at position **4·4**. What they carry is deliberately not fixed — the name says nothing about their content. |
| D10 | The pad **returns to the main buttons automatically** once an alternate command is sent, so the alternate set is momentary from the operator's point of view. Cancelling an entry does not return. |

---

## 3. Hardware and protocol

### 3.1 Device

| Property | Value |
|---|---|
| Product | Elgato Stream Deck Module, 32 keys |
| USB vendor / product ID | `0x0FD9` / `0x00BA` |
| Key grid | 8 columns × 4 rows |
| Key image | 96 × 96 px, **JPEG**, rotated **180°** before upload |
| Connection | USB-C, USB 2.0 |

The module is an OEM board intended for panel integration. Elgato publish the HID protocol at
<https://docs.elgato.com/streamdeck/hid/>; the module belongs to the XL command-set family.

### 3.2 Protocol summary

| Operation | Report | Detail |
|---|---|---|
| Key state | Input `0x01`, command `0x00` | One byte per key from offset `+0x04`; `0x00` released, `0x01` pressed. Poll at ~50 ms. |
| Set key image | Output `0x02`, command `0x07` | Chunked: `+0x02` key index, `+0x03` transfer-done, `+0x04` chunk size, `+0x06` chunk index, `+0x08` data. |
| Fill key with colour | Feature `0x03`, command `0x06` | Solid colour, no image encoding. |
| Backlight brightness | Feature `0x03`, command `0x08` | |
| Unit information | Feature `0x08` | Confirms key count and image geometry at runtime. |

Report size limits: input ≤ 512 B, output ≤ 1024 B, feature ≤ 32 B. All multi-byte integers are
little-endian.

**Fill-key-with-colour is preferred for state feedback.** It costs one 32-byte feature report and no
JPEG encoding, which is enough for point position and signal aspect. Rendered JPEGs are reserved for
keys that carry a label.

### 3.3 Library

The Module is driven directly through **`HidSharp`** (2.6.4). It was first driven through
`StreamDeckSharp`, which carries the Module as part of its Stream Deck XL family (`0FD9:006C`,
`0FD9:008F`, `0FD9:00BA` — the same 8×4 grid of 96 px keys with the same JPEG driver), but that
package pulls in `SixLabors.ImageSharp`: version 4 needs a licence for Release builds and 3.x carries
security advisories. The protocol is small, so it now lives in `StreamDeckButtonPanel`:

- Key picture: a 96×96 JPEG, quality 100, 4:4:4, encoded with Skia, cut into 1024-byte output
  reports `02 07 key last lenLo lenHi seqLo seqHi` + up to 1016 bytes. The pixels go out turned a
  half turn; `Rotate180` is applied on top, so the two cancel at the default.
- Key states: input report `01`, one byte per key from offset 4.
- Brightness `03 08 percent` and return to the logo `03 02`: 32-byte feature reports.

The protocol was taken from StreamDeckSharp's `HidComDriverStreamDeckJpeg` and verified on the Module.
The dependency is isolated behind `IButtonPanel` (§10).

Two operational constraints:

- The Elgato Stream Deck application **must not be running**; it claims the device.
- Key labels require text rasterisation. Use **SkiaSharp**; `System.Drawing.Common` is Windows-only
  and unsupported for this purpose.

---

## 4. Configuration

Device selection is per station, in `appsettings.json`:

```json
"Stations": [
  {
    "Name": "Munkeröd",
    "DataFolder": "Data\\Munkeröd.txt",
    "InputDevice": "StreamDeck"
  },
  {
    "Name": "Steinsnes",
    "DataFolder": "Data\\Steinsnes.txt",
    "InputDevice": "NumericKeypad"
  }
]
```

`InputDevice` accepts `NumericKeypad` (default when absent) and `StreamDeck`.

**Absence of the device must never prevent start-up.** When `StreamDeck` is configured but no device
is present — development machines, CI, a disconnected cable — the panel service logs a warning and
runs as a no-op. This follows the existing development/production switching pattern in `Program.cs`.

Key assignment is defined in a new `[Buttons]` section of the unified station file rather than in
`appsettings.json`. It declares **both button sets** of the command pad. Key layout is
station-specific, and placing it in the station file inherits `YardDataService` file watching and
live reload, which makes laying out 32 keys across two sets iteratively practical.

---

## 5. Key layout

Sixteen keys cannot carry eighteen commands, and the numeric pad on the right is not negotiable —
every parameter is a number. The command pad therefore carries **two sets of buttons**, split by
*frequency* rather than by domain, with the alternate key at position **4·4**.

The two sets are named **main** and **alternate** — names that say nothing about what is on them.
Today the alternate set happens to hold locking commands, but nothing about the arrangement requires
that, and the naming must not imply it.

### 5.1 Main buttons

```
┌──────────────────────────────────────────────────┬─────────────────────────┐
│  POINT       POINT       SIGNAL      SIGNAL      │   7     8     9     −   │
│  Straight    Diverging   Set stop    Set go      │                    undo │
├──────────────────────────────────────────────────┼─────────────────────────┤
│  TRAIN RT    TRAIN RT    SHUNT RT    SHUNT RT    │   4     5     6     +   │
│  Set         Cancel      Set         Release     │                         │
├──────────────────────────────────────────────────┼─────────────────────────┤
│  TRAIN       TRAIN       —           —           │   1     2     3     ✳   │
│  Approved    Advance     spare       spare       │                     all │
├──────────────────────────────────────────────────┼─────────────────────────┤
│  SH AREA     SH AREA     —           BUTTONS     │   /     0     .     ⏎   │
│  Lock        Release     spare       Alternate   │ cancel        next  send│
└──────────────────────────────────────────────────┴─────────────────────────┘
```

Everything pressed while trains are running. Rows group by domain: elements, routes, train
reporting, shunting areas.

### 5.2 Alternate buttons

```
┌──────────────────────────────────────────────────┐
│  POINT       POINT       SIGNAL      SIGNAL      │
│  Lock        Unlock      Lock stop   Unlock stop │
├──────────────────────────────────────────────────┤
│  —           —           —           —           │
│                                                  │
├──────────────────────────────────────────────────┤
│  —           —           —           —           │
│                                                  │
├──────────────────────────────────────────────────┤
│  —           —           —           BUTTONS     │
│                                      Main        │
└──────────────────────────────────────────────────┘
```

Everything that is not needed while trains are running. Four commands today and **eleven keys
free**, which is the point of the split: operations added later land here without disturbing a key an
operator already has in their fingers, whatever those operations turn out to be.

### 5.3 Button-set rules

- **Counterparts keep their position.** Where an alternate button is the counterpart of a main one,
  it sits in the same place — Point · Lock where Point · Straight was, Signal · Lock stop where
  Signal · Set stop was. Nothing has to be hunted for. A button with no counterpart in the main set
  is free to go anywhere.
- **The pad returns to the main set by itself** as soon as an alternate command is sent, so the panel
  is never left sitting on the alternate set with Point · Lock under the finger that expects
  Point · Straight. From the operator's point of view the alternate set is **momentary**: it never
  persists past one command. Two alternate commands in a row cost one extra press. Cancelling an
  entry with `/` does **not** return, so a mistyped number can simply be retyped.
- **The alternate key is inert while a command is being entered.** Switching mid-entry could only
  mean abandoning the entry, so the key reports that rather than doing it silently.
- **The alternate set is announced redundantly.** All sixteen key images change, and every key on the
  pad carries the alternate tint. A wrong-set press is the one new error this arrangement can
  produce, so it is signalled twice over.
- Positions 3·3, 3·4 and 4·3 on the main set are **spare**. The first two came free when the
  three lifecycle keys collapsed into one — see §12.

### 5.4 Entry control lives on the right pad

`−` acts as undo and `/` as cancel *while a command is being entered*. Before a command is chosen,
both retain their postfix meanings, so the right pad alone behaves exactly like a numeric keypad.
This keeps all sixteen keys of the command pad for commands and the alternate key.

`+` has no role once a command has been chosen and is unlit.

---

## 6. Command grammar (Stream Deck)

### 6.1 Form

```
{command} {parameter} [. {parameter}]… ⏎
```

The command is chosen first. This is the property that makes the pending-action display possible: the
intent is known from the first press, before any parameter is entered.

### 6.2 The `✳` wildcard

`✳` entered where a number is expected means **all**. It must be the first press of the parameter and
takes no further parameters. It absorbs all three of the existing doubled commands:

| Deck | Keypad equivalent |
|---|---|
| Point Straight `✳` | `==` |
| Signal Set stop `✳` | `**` |
| Train route Cancel `✳` | `␛␛` |

Ten of the eighteen commands accept it. Without the wildcard the pad would need ten more keys, which
no arrangement of button sets would rescue.

`✳` is deliberately **not** accepted by Point Diverging or Signal Set go; neither "all points
diverging" nor "all signals go" is a meaningful operation.

### 6.3 Signal number entry

Each digit narrows the set of signals whose number starts with the digits entered so far.

> When exactly one signal remains **and** the digits entered are that signal's full number, entry
> advances to the next parameter automatically.

The dot is therefore **not a mandatory separator**. It is a tie-breaker, needed only when a complete
number is also the start of a longer one.

| Typed | Signals present | Result |
|---|---|---|
| `2131⏎` | all two-digit (Munkeröd) | `21 → 31`. No dot needed anywhere. |
| `111` | 11, 111, 113 | Resolves at once; nothing else starts with 111. |
| `11` | 11, 111, 113 | Holds — complete, but longer matches remain. |
| `11.31⏎` | 11, 111, 113 | The dot settles on 11. |
| `13` | 13 only | Resolves; complete and nothing longer begins with it. |

Auto-advance fills only the **required** parameters. Adding a via-signal to a route stays an explicit
`.`, so a two-signal route never grows a stray empty parameter and `21.31.35` keeps its existing
meaning.

### 6.3.1 A teardown names only where the route ends

Setting a route needs both ends — which movement, from where to where. **Tearing one down needs only
the destination.** A route is identified by where it ends, which is what the keypad's `{signal}/`
form has always relied on, and the pipeline builds `TrainRouteCommand(0, toSignal, …)` however the
command arrives: the from-signal is discarded before anything is done with it.

So Train route · Cancel and Shunting route · Release take one signal, and the divider is dark on
them — there is no second parameter for it to settle onto.

Entry is rejected as soon as no signal matches the digits entered.

### 6.4 Commands

| Key | Buttons | Parameters | `✳` | Keypad equivalent | Pipeline |
|---|---|---|---|---|---|
| Point · Straight | 1 | `{point}` | yes | `{point}+`, `==` | exists |
| Point · Diverging | 1 | `{point}` | — | `{point}−` | exists |
| Signal · Set stop | 1 | `{signal}` | yes | `S{signal}−`, `**` | exists |
| Signal · Set go | 1 | `{signal}` | — | `S{signal}+` | **partly new** |
| Train route · Set | 1 | `{from}{to}` | — | `{from}{to}⏎` | exists |
| Train route · Cancel | 1 | `{signal}` | yes | `{signal}␛`, `␛␛` | exists |
| Shunting route · Set | 1 | `{from}{to}` | — | `{from}{to}*` | exists |
| Shunting route · Release | 1 | `{signal}` | yes | `{signal}/`, `//` | exists |
| Train · Approved | 1 | `{signal}{train}` | — | `.{signal}={train}⏎` | exists |
| Train · Advance | 1 | `{train}` | — | `={train}⏎` | exists |
| Shunting area · Lock | 1 | `{area}` | yes | `A{area}−` | exists |
| Shunting area · Release | 1 | `{area}` | yes | `A{area}+` | exists |
| Point · Lock | 2 | `{point}` | yes | none | **new** |
| Point · Unlock | 2 | `{point}` | yes | none | **new** |
| Signal · Lock stop | 2 | `{signal}` | yes | none | **new** |
| Signal · Unlock stop | 2 | `{signal}` | yes | none | **new** |

Rows are listed in physical key order, main buttons then alternate.

**Twelve of the sixteen reduce to character streams the existing parser already accepts**, and cost
only a translation table. The four locking commands on the alternate set are the ones with nowhere
to go (§12). Signal · Set go has its unconditional form but still wants condition evaluation.

`PanelCommandExtensions.ToKeypadKeys()` is that table. Routes are always spelled with the dot
divider even for the two-signal case: the undotted form works only because the keypad parser reads
a five-character command as two two-digit signals, and a panel that knows the real numbers has no
reason to inherit that assumption.

Validation before a command is accepted:

- point number exists at the station;
- signal number exists at the station;
- for routes, all signals share the same parity (odd or even), as today;
- from-signal and to-signal differ.

---

## 7. Numeric keypad

**The existing keypad grammar does not change.** All existing sequences — `{point}+`, `{from}{to}⏎`,
`{from}{to}*`, `{from}{to}/`, `{from}.{via}.{to}⏎`, `+{track}⏎`, `//`, `**`, `==`, `+−`, `␛␛` — remain
exactly as specified in `CLAUDE.md`. Two pairs have been added since: `A{area}+` / `A{area}−` for
shunting areas, and `S{signal}+` / `S{signal}−` for a single signal.

The seven remaining new commands are **not offered on the keypad**, from either button set. A keypad
station operates as it does today, plus the shunting area commands `A{area}+` / `A{area}−` and the
signal commands `S{signal}+` / `S{signal}−`, which were given keypad encodings because they are
useful without a deck. This is a deliberate limitation,
not an oversight: inventing keypad encodings for nine more operations would complicate a grammar that
works, for the benefit of stations that by definition have not bought the hardware.

What the keypad *does* gain is §8.

---

## 8. Character-by-character entry display

**Requested independently by operators; specified here because both devices depend on the same
mechanism.**

Today `NumericKeypadControllerInputs` accumulates keystrokes in a private `StringBuilder` and
publishes nothing until a command commits. The UI therefore cannot tell that a command is being
typed at all.

**Implemented.** `IEntryStateService` / `EntryStateService` publish it; `YardView` renders the
pending line and blinks the candidates; both input devices raise it.

### 8.1 New service

An entry-state notification service alongside the existing
`IPointNotificationService` / `ISignalNotificationService` / `ITrainRouteNotificationService`,
following the same singleton concrete-plus-interface registration pattern:

```csharp
public interface IEntryStateService
{
    event Action<EntryState>? EntryChanged;
    void NotifyEntryChanged(EntryState state);
    void NotifyEntryCleared(string stationName);
}

public record EntryState(
    string StationName,
    string? CommandName,          // null for postfix keypad entry
    IReadOnlyList<string> Parameters,
    string PendingText,           // "Set train route 21 → 31"
    IReadOnlyList<int> Candidates,// signal numbers still matching
    string RawKeys);              // the literal characters entered so far
```

It is raised on **every** keystroke, not only on commit.

### 8.2 Yard view

`YardView.razor` subscribes in `OnInitialized()` and unsubscribes in `Dispose()`, exactly as it does
for `IPointPositionService`, and renders:

- a **pending-action line** stating in plain language what is about to happen;
- the **raw characters** entered so far, for keypad operators;
- **blinking signal icons** in the SVG for every signal still matching the digits entered, the cue
  EBICOS gives. Blinking stops as soon as the number resolves.

Blinking candidates are what make dropping the mandatory dot safe: they turn an invisible parser state
into something the operator can read.

### 8.3 Applies to both devices

A keypad station gets the same pending-action line and the same blinking candidates. The work sits in
the yard view and the input pipeline, not in the hardware.

---

## 9. Point lock release semantics

Real practice, confirmed with an operating signaller: a train route is released **immediately** once
the train has arrived at its final signal, or has departed — meaning it has passed the last outbound
signal. Locks are held for the configured delay only when the route is cancelled for any other
reason.

| Cause | Entered as | Point locks |
|---|---|---|
| Train reached its final signal | Train · Arrived | released at once |
| Train passed last outbound signal | Train · Departed | released at once |
| Operator cancelled the route | Train route · Cancel | held for `LockReleaseDelay` |
| Shunting movement finished | Shunting route · Release | released at once |

### 9.1 Change required

The current implementation applies `LockReleaseDelay` to **both** Clear (`/`) and Cancel (`␛`) for
train routes; only shunting routes bypass it:

```csharp
var delaySeconds = isShuntingRoute ? 0 : GetLockReleaseDelaySeconds();
```

Clear and Cancel differ today only in train-number handling — Cancel removes the number, Clear moves
it from the origin to the destination signal.

To match §9, the delay must be derived from **cause** rather than from which key was pressed. The
train-driven path (Train · Arrived, Train · Departed) releases with zero delay; the operator
cancellation path keeps the delay. The cancellation path therefore needs no change.

> **Note:** `README.md` previously stated that cancelling releases locks immediately, which is not what
> the code does. It has been corrected: the difference between `/` and `␛` is what happens to the
> train number, not to the locks.

---

## 10. Key feedback

The reason to prefer this hardware over a keypad is that every key is an LCD.

### 10.1 State reflected on keys

**The pad has no point keys or signal keys.** It has *command* keys — one Point · Straight, not one
key per point — so there is nothing for a per-object state table to be shown on. What a command key
can say is the state of **the object the entry currently names**: once the digits resolve to point
5, the Point · Straight key can say where point 5 is standing right now.

That is what is implemented. The chosen command's key carries an accent bar in the colour of the
state the named object is in:

| Command, once its parameter resolves | Accent |
|---|---|
| Signal · Set stop / Set go | green where the signal is at go, grey where it is at stop |
| Point · Straight / Diverging | grey where the position is known, white where it is not |
| Train route · Set / Cancel | green where that route is standing, magenta where it is releasing |
| Shunting route · Set / Release | yellow where that route is standing, magenta where it is releasing |
| Shunting area · Lock / Release | blue where the area is released to a shunter |

**The colours are `EbicosColours`, not the list this section used to carry.** That list gave route
green as `#00cc00`, shunting yellow `#ffcc00`, cancelling purple `#aa44ff` and signal blue
`#aaddff`; the normative values from TRVINFRA-00305 §6.1, which `YardView` has always drawn with,
are `#008000`, `#FFFF00`, `#FF3399` and `#3366FF`. Only orange matched. A key showing an object in
a different colour from the diagram showing the same object is worse than a key showing nothing, so
there is one source for the palette and it is `EbicosColours`.

White for an unknown point position is deliberate: §6.2.1 gives white priority 1, the fault colour,
and a point whose position the interlocking cannot state is exactly that.

### 10.2 Illegal keys are unlit

During entry, keys that cannot legally be pressed next are darkened:

- once a command is chosen, the other command keys dim;
- `✳` lights only where the command accepts it and only as the first press of a parameter;
- `.` lights only when the current parameter is complete and another may follow;
- `⏎` lights only when the entry is complete and valid;
- `+` is unlit during command entry;
- the **alternate key** is unlit once a command is chosen, because the button set cannot change mid-entry (§5.3).

This is the strongest operational argument for the hardware: a malformed command cannot be entered,
because the keys that would malform it are dark.

---

## 11. Architecture

### 11.1 New components

| Component | Kind | Responsibility |
|---|---|---|
| `IButtonPanel` | interface | Abstracts the physical panel: key events in, key images out. |
| `StreamDeckButtonPanel` | singleton | `HidSharp` implementation of `IButtonPanel`. |
| `NullButtonPanel` | singleton | No-op used when no device is present. |
| `PanelControllerInputs` | `BackgroundService` | Key presses to the entry machine, completed entries to `IBufferedKeyReader`, key faces back. |
| `PanelEntry` | class (Model) | The entry state machine itself, knowing only the station's numbers. Holds the dot rule and the key-legality rules, and so is testable without a device or a yard. |
| `PanelKeyFaces` / `KeyFaceRenderer` | singleton | Composes key faces with SkiaSharp from the icons and label table embedded in `YardController.Panel`. |
| `IEntryStateService` / `EntryStateService` | singleton | Publishes partial entry (§8). |
| `ButtonLayout` | record | Parsed `[Buttons]` section from the station file — both button sets, and which key switches between them. |

### 11.2 Existing components

- `NumericKeypadControllerInputs` — **unchanged parsing**; gains calls to `IEntryStateService` so
  keypad entry is visible character by character.
- `BufferedKeyReader` — unchanged. `EnqueueKeys(IEnumerable<ConsoleKeyInfo>, string stationName)`
  already exists and is exactly what the panel needs: one completed entry becomes a complete,
  already-valid character sequence with station context attached.
- `YardView.razor` — subscribes to `IEntryStateService` and renders §8.2.

### 11.3 Command execution

The eight translatable commands are enqueued into `BufferedKeyReader`, reusing all existing locking,
validation, signal and queueing logic in `NumericKeypadControllerInputs` unchanged.

The nine new commands have no character representation and are invoked directly against the
relevant services. If that duplication of locking logic becomes awkward, the follow-up is to extract command
execution from `NumericKeypadControllerInputs` into a shared executor taking parsed commands, leaving
two thin front-ends: the keypad parser and the deck. **That refactor is deliberately deferred** — it
should be driven by the new commands' real needs, not guessed at up front.

---

## 12. New domain work

**Four** of the eighteen commands are not input plumbing. They are domain operations the model
does not have, and they are what remains of the effort:

| Command | Present state |
|---|---|
| Point · Lock / Unlock | **Outstanding.** `AlsoUnlock` and lock-address offsets exist in the model, but no command reaches them, and an operator-applied hold has nowhere to live. |
| Signal · Lock stop / Unlock stop | **Outstanding.** A signal held at stop irrespective of route state. `ISignalStateService` reports state; it does not hold one *against* route logic. Needs §13 Q6 answered first. |
| Signal · Set stop (single) | **Done.** `S{signal}−`, alongside the `**` all-signals form. |
| Signal · Set go | **Unconditional form done.** `S{signal}+` sets the signal as told, which is what shift-clicking a signal on the diagram does; a buffer stop is refused. The conditional form still needs §13 Q2. |
| Train · Approved / Advance | **Done.** The train-number lifecycle was built after this specification was first written, and `ITrainNumberService` with `TrainPositionExtensions` now holds a number's position on a detection stretch. Approved is `.{signal}={train}⏎`; Advance is `={train}⏎`. |
| Shunting area · Lock / Release | **Done.** `A{area}+` / `A{area}−`. The wildcard forms are still unimplemented. |

### 12.1 Three lifecycle keys became one

The specification originally gave Train · Arrived, Train · Departed and Train · Arrived-next a key
each. All three reduce to the same characters: `AdvanceTrainNumber()` deduces *which* step is being
taken from where the number is standing — in front of the route's final signal, on the stretch
running off the layout, or nowhere at this station any more.

Three keys sending identical characters is three ways for an operator to be told they were wrong
about something the yard had already worked out. They are one key, **Train · Advance**, and two
keys came free.

The alternative — keeping three keys and having the yard *verify* the operator's claim, refusing a
"Departed" when it believes the train has only arrived — is a better design and more work. It is
new domain behaviour, not input plumbing, and it is deliberately not attempted here.

## 13. Open questions

| # | Question |
|---|---|
| ~~Q1~~ | ~~What is a shunting area?~~ **Settled.** A shunting area is a named group of points handed to a shunter as a unit. Station-file syntax: `[ShuntingAreas]` lines of `number:name:point[,point…]`, where a bare number is a point the shunter operates and a `+`/`-` suffix marks flank protection. Releasing sets the flank protection and locks it against the interlocking for as long as the area is released; a release is refused while an active route holds any of the area's points. Released points are drawn blue (TRVINFRA-00305 §6.2.12 priority 9), flank protection stays grey. |
| Q2 | What conditions must hold for Signal · Set go to clear? Route set, points detected in position, no conflicting locks — the list needs to be explicit. |
| Q3 | `//` (clear all, no delay) has no deck key now that Train route Cancel `✳` maps to cancel-all. It remains available on the keypad. Confirm this is acceptable, or it takes the spare key at 4·3. |
| Q4 | Should Train · Approved and Train · Arrived next affect this station at all, or are they purely informational for the neighbouring station? |
| ~~Q5~~ | ~~Does a station configured for Stream Deck still accept postfix entry from the right pad?~~ **Settled: yes.** §5.4 already says both `−` and `/` keep their postfix meanings before a command is chosen, and the cheapest way to honour that is to pass every press through to the keypad grammar while no command is chosen. `PanelEntry.Press()` returns `PassThrough` for exactly that case. The right pad alone therefore behaves as a numeric keypad, at no cost. |
| Q6 | Does a signal stop-lock block only that signal from clearing, or also the *setting* of any route that needs it? Needed before §12. |

**Settled:** whether the alternate key should be a toggle or a held momentary key. The behaviour in
§5.3 — press to switch, automatic return once a command is sent — is momentary in effect and was
confirmed acceptable on the prototype.

---

## 14. Working prototype

An interactive prototype of the layout, entry model, candidate matching and key lighting — using real
Munkeröd signal, point and route data — is available at:

<https://claude.ai/code/artifact/b77e3adf-88b3-4171-9495-cbc0eabebfed>

Both button sets are live: the key at 4·4 switches between them, and sending an alternate command
returns the pad to the main set. It also includes a toggle between Munkeröd's all-two-digit signal
numbers and a contrived set with overlapping prefixes, so the dot rule in §6.3 can be exercised.
