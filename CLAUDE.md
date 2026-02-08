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

Located in `YardController.Web/Data/`. Paths configured in `appsettings.json`.

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

### Testing

MSTest with test doubles: `TestKeyReader`, `TestYardController`, `InMemoryPointDataSource`, `InMemoryTrainRouteDataSource`. DI configured in `ServicesExtensions.cs`.

### Dependencies

- `Tellurian.Trains.Adapters.LocoNet` / `Tellurian.Trains.Protocols.LocoNet` - LocoNet hardware communication
- `Microsoft.Extensions.Hosting` - DI container and hosted service pattern
