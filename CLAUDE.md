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
- **YardController.Tests** - MSTest unit tests with in-memory test doubles.

### Topology & Graph System

The yard layout is modeled as a directed graph:

- **GridCoordinate** (struct) - 2D position (`row.column`). Ordered column-first (left-to-right).
- **TrackGraph** - Graph of `TrackNode`s connected by `TrackLink`s. Links are directional (left-to-right by convention).
  - `GetDirectedAdjacentCoordinates(coord, forward)` - Returns neighbors respecting direction (outgoing for forward, incoming for backward).
- **YardTopology** (record) - Complete yard: graph, points, signals, labels, gaps.
- **PointDefinition** - Label, SwitchPoint coordinate, DivergingEnd coordinate, Direction (Forward `>` / Backward `<`). Straight arm is deduced from graph topology via `TrackGraphExtensions.DeduceStraightArm()`.
- **SignalDefinition** - Name (numeric), Coordinate, DrivesRight (direction), optional IsHidden.
- **TrackGraphExtensions** - `FindRoutePath()` uses directed BFS between signals. `BuildPointNumberMapping()` extracts numeric part from labels like "27a" → 27.

### Blazor Web UI

- **YardView.razor** - Main SVG-based yard display. Renders track links, clickable signals, point labels, occupancy gaps. Active route links highlighted in green.
  - Signal click: first click selects from-signal, second click selects to-signal and sets route.
  - Parity constraint: from/to signals must have same odd/even parity.
  - Reachability constraint: to-signal must be reachable via directed BFS from from-signal.
  - CTRL+click on a Go signal cancels its route.
- **SignalView.razor** - Individual signal with direction arrow, red/green state, click handling.
- **Home.razor** - Hosts YardView with grid toggle and live reload on data file changes.

### Services

- **YardDataService** - Central data manager. Loads Topology, Points, and TrainRoutes files. Watches files for changes and auto-reloads. Validates consistency (signals exist in topology, points referenced in routes exist).
- **NumericKeypadControllerInputs** (BackgroundService) - Reads input → parses commands → validates against lockings → sends via IYardController.
- **TrainRouteLockings** - Point lock lifecycle: `CanReserveLocksFor()` → `ReserveLocks()` → `CommitLocks()` → `ClearLocks()`. Conflict = same point locked at different position.
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
- **Clear all**: `//` cancels all train routes and clears all locks

### Data Files

Located in `YardController.Web/Data/`. Paths configured in `appsettings.json` under `"Stations"` array. Two formats coexist:

- **Unified format**: `DataFolder` ends in `.txt` (e.g., `"Data\\Munkeröd\\Munkeröd.txt"`) — single file with `[Section]` headers.
- **Legacy multi-file format**: `DataFolder` is a directory containing `topology.txt`, `points.txt`, `signals.txt`, `TrainRoutes.txt`.

`YardDataService.SetStationPaths()` auto-detects which format to use. `StationFileConverter` converts legacy → unified (one-time migration).

#### Unified Station Format (`UnifiedStationParser`)

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

[Points]
row.col(label>)-row.col  @address        ' forward point with hardware address
row.col(<label)-row.col  @address        ' backward point
row.col(label>)-row.col+  @address       ' + suffix = explicit end is straight (default: diverging)
row.col(<1a)-row.col(1b>)  @840a,843b   ' paired crossover
row.col(<9a)-row.col(9b>)  @(830a,-836b)+(830a,-836b,835)-  ' grouped addresses

[Signals]
row.col:name>:type  @address             ' signal driving right
row.col:<name:type  @address;feedback    ' driving left with feedback address
' Signal types: x=Hidden, u=OutboundMain, i=InboundMain, h=MainDwarf, d=ShuntingDwarf

[Labels]
row.col[Text label]row.col

[Gaps]
row.col|row.col                          ' gap on a specific link
row.col|                                 ' gap at node only

[Turntable]
Tracks:1-17
Offset:196

[Routes]
21-31:1+,3+,7+                          ' fully manual route
21-31                                    ' auto-derived from topology
21-31:x25+,27+                           ' x prefix = flank protection
21-31:x25+                               ' only flank = auto-derive on-route + flank
21-35:21.31.35                           ' composite from sub-routes
21-31:1+,3+  @500                        ' route with hardware address
```

#### Legacy Format (separate files)

**Topology.txt** - Yard diagram defining track layout:
```
StationName
[Tracks]
row.col-row.col-row.col           ' track segments (left-to-right)
row.col!                          ' forced necessary coordinate
[Features]
row.col|                          ' occupancy gap at node
row.col(label>)-row.col           ' point, forward direction
row.col(<label)-row.col           ' point, backward direction
row.col(label>)-row.col(<label)   ' paired points (crossover)
row.col:name>:                    ' signal driving right
row.col:<name:                    ' signal driving left
row.col:name:x                    ' hidden signal
row.col[text]row.col              ' label
```

**Points.txt** - Point hardware configuration:
```
1:840                             ' point 1 at LocoNet address 840
3:842a,845b                       ' multiple addresses
LockOffset:1000                   ' lock address offset for subsequent points
23:(823)+(-816,823,820)-          ' grouped: different addresses for straight/diverging
Adresses:800-853                  ' address range (bulk)
Turntable:1-17;196                ' turntable tracks with offset
```

**TrainRoutes.txt** - Route definitions:
```
' Comments start with single quote
21-31:1+,3+,7+                    ' basic route with point positions
35-41:x25+,27+,4+,2+             ' x prefix = flank protection (off-route, locked but not on path)
21-35:21.31.35                    ' composite route = route 21-31 + route 31-35
```

### Route Auto-Derivation

`FindRoutePath()` uses edge-based Dijkstra weighted by Euclidean distance. `DeriveRoutePoints()` computes point positions based on which arm (straight vs diverging) appears in the path. Routes in `[Routes]` can be fully manual, fully auto-derived (no colon after signal pair), or mixed (explicit flank `x`-prefixed points + auto-derived on-route points).

### TrainRouteState Lifecycle

`Undefined` → `Unset` (loaded from config) → `SetMain`/`SetShunting` (active) → `Cancel` (teardown, locks still held) → `Clear` (fully done). `IsSet` = SetMain or SetShunting. `IsTeardown` = Cancel or Clear.

### Signal System

Signal state managed by `ISignalStateService` (like `IPointPositionService` pattern). Signal types: `x`=Hidden, `u`=OutboundMain, `i`=InboundMain, `h`=MainDwarf, `d`=ShuntingDwarf. Dev mode uses `LoggingSignalStateService`; production uses `LocoNetSignalStateService` (subscribes to both LocoNet hardware and `ISignalNotificationService` for unaddressed signals).

### Testing

MSTest with `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` — all tests run in parallel. Test doubles: `TestKeyReader`, `TestYardController`, `TestYardDataService`. DI configured in `ServicesExtensions.cs` (uses C# 14 extension methods). `YardController.Tests/Data/Munkeröd/` contains a copy of legacy files for converter integration tests.

### DI Patterns

Singleton registration: `AddSingleton<Concrete>()` + `AddSingleton<IInterface>(sp => sp.GetRequiredService<Concrete>())`. Dev vs prod switching via `builder.Environment.IsDevelopment()` for `IYardController`, `IPointPositionService`, `ISignalStateService`. Exception: `KeyboardCaptureService` is **scoped** (not singleton) because `IJSRuntime` is circuit-scoped in Blazor Server.

### Dependencies

- `Tellurian.Trains.Adapters.LocoNet` / `Tellurian.Trains.Protocols.LocoNet` - LocoNet hardware communication
- `Microsoft.Extensions.Hosting` - DI container and hosted service pattern
