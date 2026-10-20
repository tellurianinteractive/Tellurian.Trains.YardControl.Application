# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Test Commands

```bash
# Build the solution
dotnet build "Yard Control Application.slnx"

# Run the web application
dotnet run --project YardController.Web/YardController.Web.csproj

# Run all tests
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Run tests in a specific class
dotnet test --filter "FullyQualifiedName~NumericKeypadControllerInputTests"
```

## Architecture

A .NET 10.0 Blazor Server application for controlling model train yard points (switches/turnouts) via LocoNet protocol. Provides both a graphical web UI and numeric keypad input. Uses British English terminology: "Point" (not "Switch" or "Turnout").

### Projects

- **YardController.Model** - Pure domain models, graph data structures, validators. No external dependencies except logging abstractions.
- **YardController.Web** - Blazor Server app. Contains components, services, LocoNet integration, data loading, and file watching.
- **YardController.Panel** - The operator's button panel: the Stream Deck driver, and the key
  faces composed with SkiaSharp from the icons and label table embedded in it.
- **YardController.Keys** - Console tool. Renders key faces to files, reports which font a
  machine will letter them in, and drives a panel directly for testing (`--panel`).
- **YardController.Tests** - MSTest unit tests with in-memory test doubles.

### Topology & Graph System

The yard layout is modeled as a directed graph:

- **GridCoordinate** (struct) - 2D position (`row.column`). Ordered column-first (left-to-right).
- **TrackGraph** - Graph of `TrackNode`s connected by `TrackLink`s. Links are directional (left-to-right by convention).
  - `GetDirectedAdjacentCoordinates(coord, forward)` - Returns neighbors respecting direction (outgoing for forward, incoming for backward).
- **YardTopology** (record) - Complete yard: graph, points, signals, labels, gaps, crossings.
- **PointDefinition** - Label, SwitchPoint coordinate, DivergingEnd coordinate, Direction (Forward `>` / Backward `<`). Straight arm is deduced from graph topology via `TrackGraphExtensions.DeduceStraightArm()`.
- **SignalDefinition** - Name (numeric), Coordinate, DrivesRight (direction), optional IsHidden.
- **TrackGraphExtensions** - `FindRoutePath()` uses directed BFS between signals and returns the links in the order a movement runs over them, which is what `RouteLinksInOrder()` and `SectionPositionOnRoute()` build on. `BuildPointNumberMapping()` extracts numeric part from labels like "27a" → 27.

### Blazor Web UI

- **YardView.razor** - Main SVG-based yard display. Renders track links, clickable signals, point labels, occupancy gaps. Active route links highlighted in green. Takes a `Role` parameter and sends every command through `EnqueueCommand()`, which stamps the role onto the keys.
  - Signal click: first click selects from-signal, second click selects to-signal and sets route.
  - Parity constraint: from/to signals must have same odd/even parity.
  - Reachability constraint: to-signal must be reachable via directed BFS from from-signal.
  - Click on the from-signal of an active route arms a clear; a second click on it sends `{to}/`. `_pendingClearSignal` holds the arming and lapses after `PendingClearMilliseconds`.
  - Click on a *terminal* signal that ends an active route toggles it stop/go. `_terminalSignals` is every route destination that is never a `FromSignal` and never an `IntermediateSignals` entry — the signals nothing follows.
  - Click on a point label (a transparent `PointHitWidth`×`PointHitHeight` rect over it) throws the point to the other position.
  - CTRL+click on a Go signal cancels its route; Shift+click toggles stop/go. Both are dispatcher-only.
  - A third status line under the diagram lists the areas a shunter is asking for, blinking via `.release-request` in the scoped CSS. It is rendered unconditionally because Razor reserves `<text>` inside a code block.
  - The status line wears the colour of whatever its message regards, so the words match the picture: `StatusSubject` names the object (train route, shunting route, releasing route, point, shunting area) and `SubjectColour()` maps it to the EBICOS colour that object is drawn in. A refused command is red whatever it regards — a message saying nothing happened must not read like one saying something did. The queued-routes line follows the same rule per entry: its heading is grey and each queued route is drawn in the colour of the route it is waiting to become.
- **SignalView.razor** - Individual signal drawn with the EBICOS symbol set (TRVINFRA-00305 §6.2.3-6.2.5): chevron = dwarf (`d`), pierced triangle = main (`u`/`i`), both = main dwarf (`h`) with the dwarf part on the approach side. Colour carries state via `SignalAspect` (grey stop / green proceed / red route end / magenta releasing); the symbol breaks the track line so its notch and hole stay readable. Signal numbers are yellow, above the symbol facing right and below facing left (§6.3). A signal on a diagonal track turns with it: `YardView.SignalSlopeAt()` deduces the slope from the links on the signal's approach side (lower column when driving right, higher when driving left; level wins, edge signals use the leaving track) and passes it as `TrackAngle`; the number stands off perpendicular to the rails but is never itself turned.
- **EbicosColours.cs** - The eight normative symbol colours from §6.1 plus the display background. All diagram colours come from here; do not hard-code hex values in components.
- Points of a *released* shunting area are drawn blue (§6.2.12 priority 9, "Ingår i växlingsområde"). Blue sits directly above grey in the priority order, so any route colour wins over it. Flank protection points keep the normal grey — they are held for the area, not free for the shunter.
- **Home.razor** - Hosts YardView with grid toggle and live reload on data file changes. Routed as `/`, `/{station}` and `/{station}/{view}`, where `view` is `shunter` or `dispatcher` (anything else, including nothing, is the dispatcher). The role decides what the page shows: the emergency bar, the hardware buttons and keyboard capture are the dispatcher's; the shunting-area chips are the shunter's. A footer button switches views.

### Operator Roles

`OperatorRole` (Dispatcher / Shunter) travels with every key: `IBufferedKeyReader.EnqueueKeys(keys, station, role)` and `ReadKeyWithStation()` return it, so `NumericKeypadControllerInputs` — not the view — enforces the rules. Input without a role (console, UDP) is the dispatcher's.

- Dispatcher-only commands (`//`, `ESC ESC`, `**`, `==`, `+-`, turntable, every `=` train-number form, `S{signal}±`, `A{area}+`) go through `IsDispatcherOnly()`.
- `IsPointPermitted()` refuses the dispatcher a point in a released area and refuses the shunter everything else. The slave cascade is checked as well, since a coupled point cannot move on its own.
- `IsRoutePermitted()` refuses the dispatcher any route touching a released area. For the shunter it applies `ShuntingAreaExtensions.IsWithinShuntingAreas()`, which takes the released areas themselves, not just their point numbers: every on-route point must be owned (declared by *any* released area, or cascaded from one that is), there must be at least one such point, and no route flank protection may pin a point the shunter is free to move. Ownership spans every released area at once, so a route may run out of one and into the next. A point a released area holds as *its own* flank protection counts as owned where the route asks for the position it is already held in — the route passes over it without altering it — and is refused where the route asks for the opposite. Teardown is checked against the route that is standing.
- `ResolveRouteState()` demotes any route the shunter sets to `SetShunting`.
- `YardView` applies the same `IsWithinShuntingAreas()` rule to decide whether the shunter may toggle a route's end signal.

**Messages are addressed; the diagram is not.** `TrainRouteResult` and `PointResult` implement
`IOperatorMessage`, whose `Audience` is the role that asked for the message — null where it is
addressed to both. Every `Notify*` on `ITrainRouteNotificationService` / `IPointNotificationService`
takes it as a trailing optional argument, and `NumericKeypadControllerInputs` passes the `role` that
came in with the keys; a delayed teardown carries it too, which is why `ExecutePhase2Release()` and
the queue's `QueuedRoute` hold a role. `YardView.SetFeedback(IOperatorMessage, …)` drops what
`IsFor(Role)` says is not this operator's, so each screen answers only its own commands and a
refusal reaches the person who was refused. Only the *words* are filtered — the same handler has
already updated the route links, the point positions and the signals, because both operators are
looking at the same tracks.

Addressed to both (`Audience` null): `//` and `ESC ESC`, which take down the shunter's routes as
much as the dispatcher's; every shunting-route teardown they cause; and the shunting-area events,
which travel by `IShuntingAreaService` and carry no audience at all — an area handed over, taken
back, or asked for is the one thing the two operators must agree about. Notifications with no
command behind them (`LoggingYardController` simulating hardware feedback) default to null for the
same reason: they answer nobody.

The pending-entry line (§8) is filtered the same way, on `EntryState.Role` — `OnEntryChanged` returns
early for the other role, so a view shows only what its own operator is typing and only its own
signals blink as candidates. `EntryStateService.NotifyEntryCleared()` keeps the previous entry's role
for exactly this reason: a clear addressed to nobody would leave abandoned characters standing on the
screen that was showing them.

### Services

- **YardDataService** - Central data manager. Loads Topology, Points, and TrainRoutes files. Watches files for changes and auto-reloads. Validates consistency (signals exist in topology, points referenced in routes exist).
- **NumericKeypadControllerInputs** (BackgroundService) - Reads input → parses commands → validates against lockings → sends via IYardController.
- **TrainRouteLockings** - Point lock lifecycle: `CanReserveLocksFor()` → `ReserveLocks()` → `CommitLocks()` → `ClearLocks()`. Conflict = same point locked at different position. Shunting area flank protection is held in a separate list (`LockForShuntingArea()` / `UnlockShuntingArea()`) that `IsLocked()` also consults but route teardown and `ReleaseAllLocks()` never touch.
- **ShuntingAreaService** (`IShuntingAreaService`) - Which areas are released, per station, and which ones a shunter is asking for. No hardware behind it, so state is written through the same interface; `ShuntingAreaChanged` and `ReleaseRequestChanged` drive the UI. Acting on an area clears its pending request either way.
- **BufferedKeyReader** - In-memory key queue bridging UI clicks to the input processing pipeline.

### Main Abstractions

- `IYardController` - Sends point commands. Implementations: `LoggingYardController` (dev), `LocoNetYardController` (production), `TestYardController` (tests).
- `IKeyReader` - Reads input. Implementations: `ConsoleKeyReader`, `UdpKeyReader`, `BufferedKeyReader` (UI), `TestKeyReader`.
- `IPointDataSource` / `ITrainRouteDataSource` - Configuration sources (text file and in-memory implementations).

### Domain Models (record types)

- `Point` - Maps point numbers to LocoNet addresses with optional lock address offset
- `PointCommand` - Point number, position (Straight/Diverging), optional lock offset, `IsOnRoute` flag
- `TrainRouteCommand` - FromSignal, ToSignal, state, list of PointCommands. Properties: `OnRoutePoints`, `OffRoutePoints`
- `PointLock` - Tracks point command with commit state

### Command Syntax

- **Point**: `[number][+/-]` (e.g., `1+` straight, `1-` diverging)
- **Train Route**: `[from][to]⏎` sets route, `[from][to]/` clears route
- **Multi-signal**: `[from].[via].[to]⏎` uses `.` as divider
- **Turntable**: `+[track]⏎` moves turntable to track position
- **Train number at the edge**: a dot beside the signal is the layout edge, and the side it sits on says which side of the signal the edge is on — `[signal].=⏎` runs a departing train out past the signal, `[signal].=[tn]⏎` and `.[signal]=[tn]⏎` put a number straight onto the outbound or inbound edge stretch
- **Signal**: `S[signal]+` gives the signal go, `S[signal]-` puts it to stop (dispatcher only)
- **Shunting area**: `A[area]+` releases the area to a shunter (dispatcher only), `A[area]-` takes it back
- **Track detection stretch**: `T[section]+` holds the stretch occupied by hand, `T[section]-` frees a held one, `T-` frees every held stretch of the station (dispatcher only)
- **Clear all**: `//` cancels all train routes and clears all locks

### Data Files

Located in `YardController.Web/Data/`. Paths configured in `appsettings.json` under `"Stations"` array. Each station uses a single unified `.txt` file (e.g., `"Data\\Munkeröd.txt"`).

#### Station Format (`UnifiedStationParser`)

Section names are case-insensitive. Comments start with `'`. Address info separated by `@`.

```
StationName                              ' first non-comment line

[Translations]
en;sv;da;nb;de                           ' language codes (semicolon-separated)
Track;Spår;Spor;Spor;Gleis              ' translation rows

[Settings]
LockOffset:1000
LockReleaseDelay:30

[Tracks]
row.col-row.col-row.col                  ' track segments (left-to-right)
row.col!                                 ' forced necessary coordinate
row.col-(row.col-row.col)                ' parenthesised run = outside the controlled area

[Points]
row.col(label>)-row.col  @address        ' forward point with hardware address
row.col(<label)-row.col  @address        ' backward point
row.col(label>)-row.col+  @address       ' + suffix = explicit end is straight (default: diverging)
row.col(<1a)-row.col(1b>)  @840a,843b   ' paired crossover
row.col(<9a)-row.col(9b>)  @(830a,-836b)+(830a,-836b,835)-  ' grouped addresses
row.col(8-|29+)(29-|8+)                  ' plain crossing: one way per track through it

[Signals]
row.col:name>:type  @address             ' signal driving right
row.col:<name:type  @address;feedback    ' driving left with feedback address
row.col:<name s                          ' the colon before the type may be left out: 7.1:<47s
' Signal types: x=Hidden, u=OutboundMain, i=InboundMain, h=MainDwarf, d=ShuntingDwarf, s=BufferStop, f=Fictitious, g=FictitiousShunting

[Labels]
row.col[Text label]row.col

[Platforms]
row.col,row.col:u                        ' platform under the track between the two coordinates
row.col,row.col:o                        ' o = over the track

[Gaps]
row.col|row.col                          ' gap on a specific link
row.col|                                 ' gap at node only

[OccupancySections]
1:2.0,2.2 @901                      ' track detection stretch, address optional
2:2.2,2.4                           ' number:from,to — no direction, ends are coordinates
3!:3.0,3.2 @903                     ' ! = holds its occupied indication until freed by hand

[Turntable]
Tracks:1-17
Offset:196

[ShuntingAreas]
2:Västra:10,12,14,31,33,35          ' number:name:points the shunter operates
5:Rundgång 4a:4,27+,19              ' 27+ = flank protection, held straight while released
                                    ' a route may still pass over 27 straight, never diverging

[Routes]
21-31:1+,3+,7+                          ' fully manual route
21-31                                    ' auto-derived from topology
21-31:x25+,27+                           ' x prefix = flank protection
21-31:x25+                               ' only flank = auto-derive on-route + flank
21-35:21.31.35                           ' composite from sub-routes
21-31:1+,3+  @500                        ' route with hardware address
```

### Route Auto-Derivation

`FindRoutePath()` uses edge-based Dijkstra weighted by Euclidean distance. `DeriveRoutePoints()` computes point positions based on which arm (straight vs diverging) appears in the path. Routes in `[Routes]` can be fully manual, fully auto-derived (no colon after signal pair), or mixed (explicit flank `x`-prefixed points + auto-derived on-route points).

### TrainRouteState Lifecycle

`Undefined` → `Unset` (loaded from config) → `SetMain`/`SetShunting` (active) → `Cancel` (teardown, locks still held) → `Clear` (fully done). `IsSet` = SetMain or SetShunting. `IsTeardown` = Cancel or Clear.

`LockReleaseDelay` applies to every teardown the operator asks for — `[signal]/` and `//` as much as `ESC` and `ESC ESC` — because none of them says where the train is: the route stays magenta (`NotifyRouteCancelling`) with its points locked until the delay is out. The one exception is a train reported at the route's *final signal* (`=[tn]⏎`, `[signal]=⏎`), which puts it standing clear of the points: `ClearArrivedRoute()` passes `trainHasArrived: true` to `TrySetTrainRoute()` and `ExecutePhase2Release()` runs at once. Shunting routes never wait either way.

A shunting route clears only signals that have a dwarf part: `NumericKeypadControllerInputs` drops every `SignalTypeExtensions.IsMainOnly` signal (`u`, `i`) from the Go list, from signal included. `YardView.GetSignalAspect()` gives those the `ProceedShunting` aspect at the from signal and at every `IntermediateSignals` entry of an active shunting route, so a main dwarf signal shows a green dwarf part against a grey main part.

Every message about a route says which kind it is: `RouteMessages` (in `YardController.Web/Resources`) picks
between the train-route and the shunting-route wording of each resource string — *tågväg* / *växlingsväg*,
*Zugstraße* / *Rangierstraße*. A teardown command carries `Clear` or `Cancel`, not the kind of the route it
ends, so the caller reads the kind off the route that is standing and passes it in.

A `SetMain` request whose from-signal is shunting-only (`SignalType.ShuntingDwarf` or `SignalType.BufferStop`, see `SignalTypeExtensions.IsShuntingOnly`) is demoted to `SetShunting` in `NumericKeypadControllerInputs.ResolveRouteState()`. For composite routes the first sub-route's resolved state becomes the merged route's state.

### Train Numbers

A train number stands on an `OccupancySection`, not at a signal: a stretch is a piece of track a
train can be on, which is what a train number names. `ITrainNumberService` is keyed by
`(stationName, sectionNumber)` — stretch numbers restart at 1 in every station, so every call
passes one. `AssignTrainNumber` frees wherever that number stood before, so one train can never
show on two stretches at once.

`TrainPositionExtensions` (extension block on `YardTopology`) holds the geometry:
- `SectionLeadingUpTo(signal, sections, tracks, routeLinks?, travelDrivesRight?)` — the stretch a
  movement waits on at a signal, on the side it approaches from (`travelDrivesRight` ? `section.To`
  : `section.From` equals the signal coordinate, because stretches are spelled column-first). The
  side is the *movement's* direction, not the signal's: a route runs past signals facing the other
  way, and a train still comes up to those from behind. So every caller holding a route passes
  `RouteDrivesRight(route)` — the from signal's direction, since a route is set for one movement —
  and only a caller without one lets the signal's own facing stand in. Where the track branches
  several stretches end at one signal; the route is asked first and only its stretch is taken,
  otherwise the shortest, which is the track alongside the signal rather than a run across the
  throat.
- `SectionBeyond(signal, …)` — the stretch on the far side running out to column 0 or `MaxColumn`.
  Null at a signal nothing leaves the station through.
- `DepartureSectionFrom(section, …)` / `RouteLinksInOrder(route)` / `SectionPositionOnRoute(…)` /
  `IsSectionAheadOnRoute(…)`. The ordered path is what tells one stretch along a route from
  another; a stretch not on the route has position -1, which is where a train waiting at its from
  signal stands, so the whole route is ahead of it.
- `SectionsAlongRoute(routeLinks)` (extension on the stretch→track map) — the stretches a route
  runs over, one per run between consecutive boundaries. A stretch spanning several of those is
  dropped, or the derived stretches, which overlap at the points, would write the number over the
  same rails several times.

**Lifecycle without hardware.** Nothing on the layout reports a train number, so its position is
deduced from the commands given, on the assumption that a train follows the route it was given:
- accepted from the previous station → the stretch in front of the from signal, which for an
  inbound route is the run in from the layout edge (`[from][to]=[tn]⏎`, `.[signal]=[tn]⏎`)
- arrived → the stretch in front of the route's final signal (`[signal]/`, `[signal]=⏎`, `=[tn]⏎`)
- departed → the stretch between the last signal and the edge (`=[tn]⏎`, `[signal].=⏎`)
- arrived at the next station → no stretch at all (`=[tn]⏎` once more)

A route is set for one movement, so reporting the train at the end of it ends the route:
`ClearArrivedRoute()` runs the same teardown `[signal]/` does (`TrainRouteState.Clear`, dispatcher),
skipping a route already counting down its lock release. Arrival does it, and so does departure for
the outbound route the train left on — that route is also what names the signal the train ran out
past, so `SectionBeyond()` is asked for its `ToSignal` and only without a route is the signal looked
up from where the train stands (`DepartureSectionFrom()`).

`NumericKeypadControllerInputs.MoveTrainNumberToSignal()` backs `[signal]=`: it refuses a signal
that is not on the route the train is running, and one that is not ahead of where it stands.
`AdvanceTrainNumber()` walks the four steps above, and a train it cannot advance stays where it
stands: a train waiting at the from signal of a route it has been given is running that route,
so where that route names no destination stretch the command is refused rather than read as the
last step. The stretch a train waits on is a run in from the layout edge at every inbound
signal, so the track alone never tells an arrival from a departure — only the route does. Both resolve stretches through
`RouteSection()` (a route in hand) or `ResolveSectionLeadingUpTo()` (ask every active route, then
fall back to the shortest).

`HandleEdgeTrainNumberCommand()` backs the dot forms. What the dot resolves to is `SectionBeyond()`
or its mirror `SectionFromEdge()`, and those answer only for a signal the track really does leave or
enter the station through — so a train can never be sent out past an inbound signal nor accepted
through an outbound one, with no rule saying so beyond the topology itself.
`DepartTrainNumberAtSignal()` requires the train to be standing in front of the signal named, since
the command says where the movement goes and a number further back is not picked up instead;
`DepartTrainNumber()` is the step itself, shared with `AdvanceTrainNumber()`, and ends the outbound
route as an arrival does. `.[signal]=` without a train number is refused: back onto the track it
came in on is the one direction a train number never goes.

**Drawing.** `YardView.RecomputeDisplayedTrainNumbers()` writes the number on every stretch of the
route the train is running, as EBICOS does, and then over that on the stretch it actually stands
on, so nothing a route put there displaces it. `SectionLabelAnchor()` turns the stretch's
`LabelRun` into pixels: the middle of that run, and the angle to set the text at — level for a
level run, ±45° for a slanting one, never upside down. `TrainNumberBaseline()` lifts the baseline
`TrainNumberOffset` clear of the rails *perpendicular to that run*, so a number over a slope keeps
the same gap its whole length; over a level run that is straight up, where it always was.
`TrainNumberDisplay` holds
the monospaced font stack and pads to five digits — a longer number is left alone, since cutting
it would name a different train. `TrainNumberColour()` gives each copy the colour of the stretch
beneath it, in §6.2.1 priority order: red where occupied (a stretch holding a train number always
is), then magenta, green, yellow.

### Held Occupancy

A current detector reports the locomotive, not the train. A train arrives at a goods track, the loco
is uncoupled and driven away, and the rail falls silent with the wagons still standing on it — the one
failure of track detection that no tuning fixes, because there is genuinely no current left to read.
A stretch declared `number!:from,to @address` therefore keeps its occupied indication when the
detector lets go, and only an operator takes it back. Holding is the safe direction: the alternative
is a track the diagram calls free with stock standing on it.

The flag needs an `Address`. A stretch with no detector never reports free on its own, so there is no
automatic indication to override and a hold could never be answered; `UnifiedStationParser` drops the
flag and warns, and `OccupancySection.HoldsOccupancy` is the two conditions together. The parser also
warns when a flagged stretch is not `IsExclusive` — stock is left standing on the tracks trains stand
on, not in a throat, and a throat is shared by every stretch running over it.

`LatchingOccupancySectionService` wraps whichever `IOccupancySectionService` is registered
(`AddLatchingOccupancy<TInner>()`), so the same latch applies to the LocoNet and Z21 detectors in
production and to the diagram's simulation in development, and neither implementation knows about it.
It swallows the detector's free report for a flagged stretch and raises `HoldChanged` instead;
`IsOccupied` keeps saying occupied, because it is. A stretch reported occupied again drops its hold
and may latch afresh the next time the detector lets go. State is in memory only — a restart clears
the board, which is what the start of an operating session wants.

`IOccupancyHoldService` is what the diagram and the keypad talk to: `T{section}+` holds by hand (the
way to put a stretch back after a restart), `T{section}-` frees one, `T-` frees them all, and a click
on a held stretch in `YardView` frees it — those hit lines are rendered in production too while
anything is held, since a held indication is the one thing on the diagram that will not clear itself.
Releasing a hold only puts the indication out if the detector is not reporting the stretch as well:
it says a person looked, not that the rail is quiet. A held stretch stays red (§6.2.1 has one colour
for occupied) and carries a magenta dot — magenta being what this diagram already gives to everything
the interlocking is holding until someone acts.

### Buffer Stops

A buffer stop (*stoppbock*, §6.2.9) is declared in `[Signals]` with type `s` and stands for the physical end of a dead-end track rather than for a signal. `UnifiedStationParser.ValidateBufferStops()` warns unless the coordinate is a graph node of degree 1, is away from the layout edge (column 0 or `MaxColumn`, where the track leaves the station rather than ends), and the declared direction is the direction of the single link leading up to it — the symbol is drawn facing that way.

`SignalView` draws it as a bar just beyond the coordinate with a stem of track width leading back into it, grey at rest and red for every other aspect: §6.2.9 grants it no proceed colour. The symbol turns with the track it closes: `YardView.TrackAngleAt()` measures the angle from the coordinate to its one neighbour in the rendered frame (so the inverted view is handled by `ToPixel`) and passes it as `SignalView.TrackAngle`; the symbol facing right stands half a turn from that approach and has it taken back out. Only the buffer stop turns — signal symbols stay level — and the object text never turns, it only follows the symbol round. `YardView` refuses it as a route start point (`IsBufferStop()`) and excludes it from `IsToggleableEndSignal()`, since nothing lies beyond it to let a movement continue to.

### Fictitious Signals (Information Points)

A signal of type `f` (train and shunting routes) or `g` (shunting routes only) is an *informationspunkt* (§6.2.23): a place a route can start or end at that
has no signal standing there. Unlike the hidden type `x` it is drawn, so it can be clicked.
`SignalView` draws it as a diamond with a diamond hole, centred on its coordinate and coloured by
aspect as the section's table says (green train-route start, yellow shunting-route start, red end,
magenta releasing, grey at rest). An `f` is neither shunting-only nor main-only, so it takes part in
train and shunting routes alike, as start or end; a `g` is shunting-only (`IsShuntingOnly`), so a route
starting or ending at it is demoted to a shunting route like one at a shunting dwarf. `YardView` never offers to give it go
(`IsToggleableEndSignal()` excludes it, as for a buffer stop), since there is no signal to give go.

### Platforms

A platform (*plattform*) is declared in `[Platforms]` as `from,to:side`. The coordinate pair names the
stretch of track the platform serves — it is not track of its own, so it is spelled column-first like
every other pair — and the side letter says where the platform is drawn: `u` under that track, `o` over
it. `UnifiedStationParser.ValidatePlatforms()` warns unless both ends are graph nodes, they differ, and
they share a row; passengers board along one track, so a platform spanning rows is almost certainly a typo.

`YardView` draws it as a band of `PlatformWidth` (150% of the track width) offset perpendicular to the
stretch, so a platform alongside a slanting run keeps the same `PlatformGap` its whole length. The colour
is `EbicosColours.Platform`, a shade darker than `Grey` — outside the normative palette, because a
platform is neither track nor symbol and never changes colour. That is also why the `platforms` group is
drawn **first**, under every other layer: nothing on the diagram may end up hidden behind it. The label
background patch is sized to clear the track line rather than the glyphs for the same reason — a patch
the height of the text would reach past the gap and cut a notch out of the platform behind it.

`PlatformOffset()` reads the side off the *drawn* diagram, so the inverted view — a half turn of the whole
yard — draws an `Under` platform above its track: the same side of the same track, seen from the other end.

### Uncontrolled Track

Track outside the controlled area — no signals, points thrown by hand — is written in parentheses
in `[Tracks]`. A link is `TrackLink.IsUncontrolled` when both its ends lie inside the same pair of
parentheses, so `2.0-2.3-(2.3-2.5)` leaves 2.2–2.3 controlled. `YardView` draws such links the full
track width but as an outline: grey at full width, then a hollow in the background colour at half
width, which leaves an outline a quarter of the width thick on each side. All outer lines are drawn
before any hollow so a hollow never cuts a neighbouring link's outline.

### Plain Crossings

A plain crossing (*korsning*) is where two tracks cross with nothing on them that moves. It is
declared in `[Points]` as `5.8(8-|29+)(29-|8+)`: the coordinate, then one parenthesised **way**
through it per track. Each way spells the point position that lays it, a `|`, and the positions
that must hold while it is laid. Commas separate several conditions, and a comma between the ways
is allowed. The `|` is what tells a crossing line from a point line, so `ParsePointLine` reads it
first.

Because the crossing has no point of its own, everything about it is read off the points leading
into it. `CrossingExtensions.ArmsOf()` finds the links a way reaches the crossing by, stepping out
of the crossing towards each `PointDefinition` carrying the governing number — a crossover's switch
point lies at the far end of the diagonal, so `TrackGraphExtensions.ArmLink()` takes the first step
towards it, the same helper `ArmsTurnedAway` uses. `UnifiedStationParser.ValidateCrossings()` warns
unless the coordinate is a node of degree 4 crossed by exactly two ways, each reaching it by two
arms, together covering every link at the coordinate and claiming none of them twice.

**The conditions are the crossing's flank protection**, and they are enforced in two places:

- `ApplyCrossingConditions()` gives every route that lays a way that way's conditions, as off-route
  point commands, at parse time — so locking, validation and display need to know nothing about
  crossings. A route naming the point at the opposite position contradicts the crossing and is
  warned about rather than patched, since nothing could be added that would make it safe.
- `NumericKeypadControllerInputs.IsCrossingInTheWay()` refuses a hand-given point command that lays
  a way while a condition does not hold, naming the point that is in the way and saying whether it
  is locked (`Messages.PointBlockedByCrossingLocked`) or merely standing wrong
  (`Messages.PointBlockedByCrossing`). The whole slave cascade is asked, since a coupled point lays
  a way as surely as the one that was typed, and a condition the cascade itself satisfies is no
  condition at all.

A point nothing has reported yet counts as in the way: the crossing must be known clear, not merely
not known to be blocked. That never traps the operator, because turning the *other* point away lays
nothing and so is never refused — which is how a crossing is worked in any case.

`YardView` draws the arms of a way that is not laid with the same kind of break that shows a point
turned away — `TrackCutOff.AtCrossing()` — cut parallel to the track that passes over, which at a
plain crossing is the other way through it. A way whose governing point has no reported position is
left undrawn, exactly as an undefined point draws no gap. With both points straight the crossing is
cut off on all four sides.

**The two breaks are measured in different frames, and `TrackCutOff` keeps them apart** —
`AtPoint()` and `AtCrossing()`, one call site each. A point's arms are the yard's own geometry, one
level and one diagonal, so its break is measured in the **diagram's** frame: level top and bottom a
track's thickness apart, the other pair of edges parallel to the explicit end. That one rule serves
both positions — standing diverging the level edges lie along the closed level arm and the diagonal
ones cut it; standing straight the diagonal edges run along the closed diagonal arm and the level
ones cut it, so it ends level with the track that passes over. A crossing has no level arm to
measure against, so `AtCrossing()` measures **in the closed arm's own frame**: one pair of edges
along that arm, the other parallel to the cut, which is what keeps a 45° arm from being cut with
horizontal ends against a track that is not. Serving the points from that measurement instead turned
every break on the diagram square to its own arm, which is not what a point draws.

`AtCrossing()` also begins the break where the track that passes over *ends*, not where the closed
arm leaves the crossing: the two ways cross, so the first stretch of a closed arm lies under the
other's rails, and a break laid there paints a bite out of the way that is laid. The near edge runs
parallel to that track, so it stands the same distance from it its whole length — half a track's
thickness and a pixel, divided by the sine of the angle between the ways, since the shallower they
cross the further along the closed arm those rails reach. A point needs no such clearance: its arms
meet at the switch rather than crossing, so a pixel from the switch point is already clear.

Munkeröd's 5.8 is the case: the diagonal 4.7–7.10 carried by point 8 crossing the diagonal 7.6–4.9
carried by point 29. Point 10 is not a condition of it — the double slip at 7.10 couples 8 and 10
already (`&+:10+`, `&-:8-`), and 8- is legal with 10 either way.

### Shunting Areas

A shunting area is a named group of points handed to a shunter as a unit, declared in `[ShuntingAreas]` as `number:name:point[,point…]`. A bare point number is one the shunter operates; a `+`/`-` suffix marks flank protection, set and held locked while the area is released. `A{area}+` releases, `A{area}-` takes it back (`NumericKeypadControllerInputs.HandleShuntingAreaCommand`). A release is refused while any of the area's points is locked by an active route; taking an area back
first clears every route using one of its points. While released, the area's points are the shunter's:
the dispatcher is refused them, and the shunter is refused everything else.

Flank protection is declared where a point both protects the area and lies on the way through it — the
double slip at Munkeröd's `7.10` is one point the shunter owns (10) and one held straight (`8+`). Holding
it straight is what keeps the approach from track 4b closed, and it is also the position every route
through the slip wants, so those routes are the shunter's while routes that would throw it are not.

Releasing several areas gives the shunter their union, not each in isolation: a route is theirs as long
as every point it sets is owned by *some* released area.

### Track Detection Stretches

`OccupancySection` (number, From, To, optional Address) is a *spårledning*: an isolated piece of track
the hardware reports as occupied. It has no direction, so `From`/`To` are only ordered column-first to
give it one spelling. Declared in `[OccupancySections]` as `number:from,to  @address`. A `!` after
the number (`3!:3.0,3.2 @903`) marks a stretch an operator frees by hand — see **Held Occupancy** below.

`OccupancySectionExtensions` (extension block on `YardTopology`) holds the whole geometry:
- `OccupancySectionBoundaries` — every signal coordinate plus every coordinate at column 0 or `MaxColumn`. Signals sharing a coordinate (22/71 at Munkeröd's `2.2`) count once, because the set holds coordinates.
- `DeriveOccupancySections()` — one section per pair of boundaries reachable from each other without passing a third. Sections overlap where the track branches; a train on a throat is on all of them. Munkeröd derives 171, Steinsnes 17. The derived set is a snapshot of the signals as they stood: a signal added or moved afterwards leaves the declared list describing the yard as it used to be, and nothing rewrites it, so `ValidateOccupancySections` warns about a signal no declared stretch ends at — which is a signal no route can name a destination for and no train number can move to.
- `MapOccupancySectionTracks()` — returns an `OccupancySectionTrack` (links + `IsExclusive`) per section. The file records only the two ends, so the run between them is recovered by Euclidean-weighted Dijkstra (as `FindRoutePath`) that stops at other boundaries, falling back to the unrestricted shortest path for a hand-written section spanning several signals. Euclidean weighting is what makes a straight run beat an equal-link detour over points. `IsExclusive` is true when no other section covers any of the section's links — true of the tracks trains stand on, false through the throats. `Links` run in order from one end of the section to the other.

- `ArmsTurnedAway(positionOf)` — the link by which every point of known position leaves towards the arm it is *not* set to. A slip's arm runs the length of a diagonal, so where the two ends are not neighbours (Munkeröd's 8a, 4.7 to 7.10) the link taken is the first step towards it. A point of unknown position is turned away from nothing: not knowing where a point stands must never hide a train.
- `ConnectedPart(armsTurnedAway)` (extension on `OccupancySectionTrack`) — the part of a section its points still connect to one of its ends: the whole of it where the points set it end to end, otherwise the run before the first arm turned away plus the run after the last. What falls out is track no movement could have reached.
- `StraightRuns` (extension on `OccupancySectionTrack`) — the path along the section, cut wherever it changes direction, so a track with a throat at either end comes back as three runs rather than as half a dozen links. The links come in path order but each is spelled left to right, so the path is walked coordinate by coordinate rather than read off their ends: Munkeröd's 7.10–1.12 leaves the slip westwards before climbing east, and read off the ends it would fall into four runs instead of two.
- `LabelRun` (extension on `OccupancySectionTrack`) — the run a label belonging to the section is set along: the longest level one where the section has any, otherwise the longest slope, ties settled by whichever lies nearest the middle. Text set across a slope reads as belonging to neither of the tracks it slants between, and Munkeröd is what says a level run is nearly always there to be had — 170 of its 171 stretches have one, the shortest a whole cell, which carries a five-digit train number with room to spare. Only 7.10–1.12 never runs level, so it is the one number written at 45°.

`YardDataService` deduces and writes the section into the station file when the file has **no**
`[OccupancySections]` header at all (`UnifiedStationWriter.ContainsOccupancySections` /
`InsertOccupancySections`). An empty section is an answer — the station has no detection — and is
left alone. The write bumps `_lastReloadByStation` so the file watcher does not treat it as an edit.

`IOccupancySectionService` mirrors `ISignalStateService`: `LoggingOccupancySectionService` (dev) and
`AccessoryOccupancySectionService` (production, maps `OccupancyNotification.FeedbackAddress` →
every section declared on it — a throat is one piece of isolated rail and the sections over it are
the ways through, which the points rather than the detector tell apart). `SetOccupied()` is on the interface for sources other than a detector, as
`IShuntingAreaService` does. The Z21 subscription adds `BroadcastSubjects.OccupiedStretch`.

### Z21 Broadcast Subscription

`Program.cs` subscribes to `RunningAndSwitching | LocoNetTurnouts | OccupiedStretch`. The first
carries `LAN_X_TURNOUT_INFO`, which is the Z21's record of the position last *commanded* — that is
what a third-party client (Z21 App, WLANMaus) both reads and writes, and it is why accessory
commands go out as native XpressNet (`useLocoNetForAccessories: false`): the Z21 does not
cross-broadcast accessory changes between its XpressNet and LocoNet-over-UDP streams. The second
carries the switch traffic on the Z21's own LocoNet bus (`OPC_SW_REQ` / `OPC_SW_REP`), which is
where a LocoNet-connected accessory decoder reports the position it actually *reached*. The third
carries the detector broadcasts the track detection stretches are read from.

**A subscription is not kept by asking once.** The Z21 drops a client that has sent it nothing for
about a minute (§1.1) and the client's broadcast flags go with it, so a yard standing idle between
point commands stops being told anything — and does not start again when the next command
re-registers the client, because the re-registered client has no flags. `Z21KeepAliveService`
re-sends `LAN_SET_BROADCASTFLAGS` every 30 seconds, which is both the activity that keeps the client
registered and what restores the subscription if the Z21 has already forgotten it. The interval is
half the timeout so a dropped UDP packet costs one renewal rather than the subscription.

Nothing queries the points at start-up, so a position is unknown until something reports it. The
Z21 answers `LAN_X_GET_TURNOUT_INFO` (X-header 0x43), which the XpressNet library has no command
for — `IAccessory.QueryAccessoryStateAsync` sends the classic 0x42 accessory-info request, which
the Z21 does not implement and answers with `LAN_X_UNKNOWN_COMMAND`. `SendPointStateRequestAsync`
therefore has no caller on this path.

`YardView` paints occupied links red **last**, over every route colour: §6.2.1 puts occupied at
priority 2, above magenta/orange/green/yellow/blue/grey (lower number wins; white 1 = fault).

A stretch holding a train number counts as occupied and is shown **whole**, exclusive or not: a
train number names one stretch exactly, so there is nothing for the points to resolve. It is read
alongside the detector rather than written into `IOccupancySectionService`, so a train number
leaving a stretch cannot erase a detector reading and vice versa.

`RecomputeOccupiedLinks()` decides *which* links of an occupied section turn red, and asks the
**points**, not the routes. Sections overlap at the points, where a detector cannot say which way
through them the movement went — but the points can, whether or not a route is set over them, which
is why this needs no setting to turn on or off. So an occupied section is drawn on
`ConnectedPart(Topology.ArmsTurnedAway(PositionOf))`: whole where the points set it end to end,
otherwise only what a movement could still have reached. `OnPointPositionChanged` recomputes it,
since a point that moves changes which overlapping section the detector is reporting.
`RecomputeActiveRouteLinks()`, `RecomputeActiveShuntingRouteLinks()`,
`RecomputeCancellingRouteLinks()` and the `AllCleared` branch still call it, now for the train
numbers it redraws along the routes.

With `AllowOccupancySimulation` (development, set from `Home.razor`) a transparent hit line per track
link stands in for the detector that is not there. It carries every stretch covering the link; the
click reports the shortest one the points still connect end to end — the same stretch the diagram
then draws — and a further click frees whichever of them is reporting, so a point moved in between
cannot strand one.

### Operator Button Panel

An Elgato Stream Deck Module (32 keys, 8x4) as an alternative to the numeric keypad, specified in
`Specifications/InputDevices.md`. Selected per station by `InputDevice` in `appsettings.json`
(`NumericKeypad` default, `StreamDeck`). **Absence of the device never prevents start-up** - the
panel polls for hardware and does nothing until it appears.

The left 4x4 is a command pad; the right 4x4 replicates the numeric keypad. Entry is
**command-first** (`{command}{parameter}⏎`), which is what makes the intent displayable from the
first press.

- **`PanelCommand`** - the sixteen commands plus the two set-switching keys. The three lifecycle
  keys became one `TrainAdvance`: `AdvanceTrainNumber()` deduces *which* step is being taken from
  where the number stands, so three keys would have sent identical characters.
  - Setting a route takes both ends; **tearing one down takes only the destination**, as the
    keypad's `{signal}/` does. The pipeline builds `TrainRouteCommand(0, toSignal, …)` regardless,
    so a from-signal asked for here would be a number typed for nothing. Its `IconName` is one
  name for three things: the key's identity in a station file, its picture, and its label.
  `PanelCommandExtensions.ToKeypadKeys()` reduces a completed entry to characters the keypad
  grammar already accepts, so all existing locking, validation and queueing runs unchanged. Four
  commands (point and signal lock/unlock) have no pipeline yet and are refused at the key.
- **`PanelEntry`** - the entry state machine, knowing only the station's numbers. Signal numbers
  are matched incrementally and settle by themselves when the digits are a whole number that
  nothing longer begins with, which is what makes `.` a tie-breaker rather than a separator.
  `IsPressLegal()` / `IsCommandLegal()` are what darken the keys that would malform the command -
  asked of the machine, so what the keys show and what the entry accepts cannot disagree. That is an
  invariant, not a convention: `WhatTheKeysShowAndWhatTheEntryAcceptsNeverDisagree` walks every
  command at every stage of entry against every key. It was written after the divider turned out to
  be dark on a teardown and yet still accepted.
- **`ButtonLayout`** - both sets of sixteen, from the station file's `[Buttons]` section (a set
  header, then rows of icon names, `-` for blank). No section means the built-in arrangement.
  Live-reloads with the rest of the station file.
- **`IButtonPanel`** - presses in, pixels out. `StreamDeckButtonPanel` absorbs every mounting
  quirk: callers number keys row-major from the operator's top left and hand over pictures the
  right way up, and `Rotate180` / `InvertKeys` (the `Panel` section of `appsettings.json`) decide
  what that means on the wire. `NullButtonPanel` is the no-op.
- **`PanelKeyFaces`** - every face composed once and cached, keyed by command, language, button set
  and lighting.
  - **Dimming means "not pressable *now*" and nothing else.** It is an entry-state cue: once a
    command is chosen the others go out (§10.2). Do not also make it mean "no pipeline yet" - the
    painter once asked `IsCommandLegal`, which folds that in, and since all four alternate commands
    are still unbuilt the whole Alt pad came up dimmed and read as a panel that had stopped
    answering. Being refused on press is what says unimplemented, at the moment it matters.
  - The alternate hairline is drawn **after** the dimming, not before. It says which set is
    showing, which is true whether or not the key can be pressed - dimmed with everything else it
    is faintest exactly when a pad of unfamiliar keys is under the operator's fingers. Icons and `Labels.txt` are embedded resources: a key face is part of the program.
  **Do not trust font coverage** - Skia draws a missing character as an empty box rather than
  failing, and the minus sign, the wildcard and the return arrow all came out as the *same* box in
  Bahnschrift. Those three are drawn as icons, and `KeyFaceRenderer.CanDraw()` guards the rest.
  The plus is drawn as well, for the other reason: every face carries it, but set in type it is
  built to a digit's measure and came out visibly smaller than the drawn keys either side of it
  on the pad. `key-plus.svg` is the minus bar crossed with itself, so minus, plus and wildcard
  all span the same 56 of the 96 units. The icons are generated by `YardController.Panel/generate-icons.py`;
  edit that and re-run it rather than hand-patching an SVG.
  **Linux needs fontconfig and a font.** Digits and labels are set in a system font, found through
  fontconfig. `YardController.Panel` references `SkiaSharp.NativeAssets.Linux` because SkiaSharp 4
  ships no Linux native library of its own (without it `libSkiaSharp` fails to load). Do not swap
  in `.NoDependencies`: it has no fontconfig, so no font is found and every label comes out blank
  with no error - the same empty-box failure as above. A Linux target (a Raspberry Pi, say) must
  have `fontconfig` and a font package such as `fonts-dejavu-core` installed, and `release.yml`
  installs both on the runner before the tests, or the key-face tests fail there.
- **`PanelControllerInputs`** (BackgroundService) - key presses to the entry machine, completed
  entries to `IBufferedKeyReader`, and key faces back. Repaints only keys whose face changed.
  - **Subscribes in `StartAsync`, not `ExecuteAsync`.** A host does not guarantee `ExecuteAsync`
    has got anywhere by the time the service counts as started, and a key pressed in that gap goes
    to a panel nothing is listening to. Getting ready is what starting means; `ExecuteAsync` only
    holds the service alive.
  - A refused press is reported to the diagram through `EntryState.Rejection`, worded by
    `Messages.PanelRefusal` in the language the yard is worked in, and rendered by `YardView` on
    the status line in red via the same `SetFeedback` every other refusal uses. It rides with the
    entry and is carried once: a refusal reports the press that caused it, not every state
    afterwards. The four commands with no pipeline yet answer "Not supported yet" - which is what
    they say instead of being dimmed, since dimming means something else.
  `AccentFor()` puts a bar along the foot of the chosen command's key in the EBICOS colour of the
  state the named object is in. The pad has *command* keys, not object keys, so this is the only
  place per-object state can go - and the palette is `EbicosColours`, never the approximate one
  InputDevices.md §10.1 used to carry (only orange matched).
- **`IOperatorLanguageService`** - which language the yard is worked in, and an event when it
  changes. A browser circuit takes its culture from the request; **a panel has no request**, and
  every one of its thirty-two keys carries a word, so the choice has to reach it as an event or the
  keys keep the language they were lettered in. `/culture` sets it alongside the thread cultures.
  - Reading `CultureInfo.CurrentUICulture` there was wrong twice over: nothing repainted on a
    change, and outside a request it answers with the *machine's* culture — Swedish on a Swedish
    machine — while the web UI defaulted to `en` from configuration. Two languages in one room.
  - **Two things seed it, and both are needed.** `"Language"` in `appsettings.json` sets what the
    installation starts in, so the panel is right before any browser has been opened; and a
    middleware after `UseRequestLocalization` tells the service what each request actually resolved
    to, because the operator's choice lives in a cookie and only a request can read one. Without
    the second, the diagram comes up in the cookie's language beside keys lettered in the
    configured one.
  - The panel follows the browser, never the other way round. A request resolving to English via
    `Accept-Language` moves the panel to English even where configuration said otherwise — which is
    right: what the operator is looking at and what is under their fingers must agree.
  - `OperatorLanguageService.SupportedLanguages` is the single list, used both to configure request
    localisation and to validate what reaches the panel. Validate against it, never merely parse:
    `CultureInfo.GetCultureInfo` does not reject nonsense, it builds a custom culture, and
    `"not-a-culture-at-all"` comes back as a culture named `"not"`.
- **`IEntryStateService`** - publishes the entry on every keystroke so the diagram can show a
  command being typed. **Both** devices raise it: `NumericKeypadControllerInputs` publishes the raw
  characters (its grammar is postfix, so there is no command to name until the terminator arrives).
  `YardView` renders a pending-action line under the diagram and passes `IsCandidate` to
  `SignalView`, which blinks every signal still matching the digits entered - the cue that makes
  dropping the mandatory dot safe.
  - The keypad's clear is published *before* its wait loop, not after: that loop restarts its own
    stopwatch every pass and so does not end until a key arrives, and a key arriving is exactly
    when the line must not be cleared.

**The Module is driven through HidSharp directly, not StreamDeckSharp.** StreamDeckSharp drags in
SixLabors.ImageSharp, which wants a licence from version 4 (Release builds fail without one) and has
advisories on 3.x. `StreamDeckButtonPanel` carries the XL-family protocol itself and encodes the key
JPEGs with Skia. Do not add a `SixLabors.ImageSharp` reference or any package that brings it in. The
key pixels go on the wire turned a half turn, which `Rotate180` cancels at its default — see
`Specifications/InputDevices.md` §3.3.

**Two processes can hold the panel at once.** HID reads are not exclusive here, so the
`--panel` diagnostic and the running web application both receive every key press. Harmless while
the diagnostic only logs, but do not add anything to it that acts on the yard, and do not read a
press appearing in both logs as the panel having sent it twice.

Orientation was settled against the hardware: with `Rotate180` and `InvertKeys` both true, key 0 is
at the operator's top left, the command pad is on the left, and labels read the right way up. The
verified path is panel press → `PanelEntry` → keypad characters → `NumericKeypadControllerInputs`;
`Lägg 2 1 3 1 ⏎` reaches the yard as `21.31[ENTER]` and takes the locks for route 21-31.

### Signal System

`S{signal}+` / `S{signal}-` (`NumericKeypadControllerInputs.HandleSignalCommand`) set one signal to
go or stop. The command asks nothing of the interlocking — it is the keypad's form of the dispatcher's
shift-click, so it reaches a signal no route covers. A buffer stop is refused go (`SignalTypeExtensions.IsBufferStop`,
§6.2.9 gives it no proceed colour). The two prefix letters are the only thing separating a signal
command from a point command, so `CharExtensions` keeps them in one place: `A` for a shunting area,
`S` for a signal, and `IsPointCommand` refuses anything carrying either.

Signal state managed by `ISignalStateService` (like `IPointPositionService` pattern). Signal types: `x`=Hidden, `u`=OutboundMain, `i`=InboundMain, `h`=MainDwarf, `d`=ShuntingDwarf, `s`=BufferStop. Dev mode uses `LoggingSignalStateService`; production uses `LocoNetSignalStateService` (subscribes to both LocoNet hardware and `ISignalNotificationService` for unaddressed signals).

### Testing

MSTest with `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` — all tests run in parallel. Test doubles: `TestKeyReader`, `TestYardController`, `TestYardDataService`. DI configured in `ServicesExtensions.cs` (uses C# 14 extension methods).

### DI Patterns

Singleton registration: `AddSingleton<Concrete>()` + `AddSingleton<IInterface>(sp => sp.GetRequiredService<Concrete>())`. Dev vs prod switching via `builder.Environment.IsDevelopment()` for `IYardController`, `IPointPositionService`, `ISignalStateService`. Exception: `KeyboardCaptureService` is **scoped** (not singleton) because `IJSRuntime` is circuit-scoped in Blazor Server.

### Hosting

Kestrel binds `http://0.0.0.0:5180` — every network interface — through `Kestrel:Endpoints:Http:Url`
in `appsettings.json`. That section overrides `--urls`, `ASPNETCORE_URLS` and the `applicationUrl` of
`launchSettings.json`, so it is the one place the binding is decided and it holds in development and
production alike; `applicationUrl` now only says which URL `dotnet run` opens the browser at. The yard
is worked from a second computer or a tablet on the layout network, which is what the wildcard is for.

Plain HTTP only: a certificate would have to be valid for whatever address the operator types, which
on a layout network is a DHCP address. So `Program.cs` calls neither `UseHttpsRedirection()` nor
`UseHsts()` — both would send the browser to a port nothing listens on, and HSTS would keep sending it
there long after the application had stopped.

`ListeningAddresses.LogListeningAddresses()` (extension block on `WebApplication`) writes the reachable
URLs to the log once the server has bound its ports: a wildcard address is spelled out as `localhost`
plus every non-loopback IPv4 address of the machine, since `0.0.0.0` is not something an operator can
type into a tablet.

### Dependencies

- `Tellurian.Trains.Adapters.LocoNet` / `Tellurian.Trains.Protocols.LocoNet` - LocoNet hardware communication
- `Microsoft.Extensions.Hosting` - DI container and hosted service pattern

## Local NuGet Development

This app consumes `Tellurian.Trains.Communications.*`, `Tellurian.Trains.Adapters.*`, and `Tellurian.Trains.Protocols.*` packages, which may come from the local feed `C:\NuGets` during development. When iterating on those libraries locally, bump this repo's `<PackageReference Version="…" />` to the library's local patch version. CI will reject any local-only version with NU1101 — the library must be publicly published before the app can ship with its changes.

Full workflow: user-level `local-nuget-development` skill.
