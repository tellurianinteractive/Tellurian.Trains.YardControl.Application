# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Test Commands

```bash
# Build the solution
dotnet build "Yard Control Application.slnx"

# Run the application
dotnet run --project YardController.App/YardController.App.csproj

# Run all tests
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Run tests in a specific class
dotnet test --filter "FullyQualifiedName~NumericKeypadControllerInputTests"
```

## Architecture

This is a .NET 10.0 console application for controlling model train yard points (switches/turnouts) via LocoNet protocol. It uses numeric keypad input to send point commands to hardware. The codebase uses British English terminology: "Point" (not American "Switch" or "Turnout").

### Core Components

**Domain Models** (record types):
- `Point` - Configuration mapping point numbers to LocoNet addresses with optional lock address offset
- `PointCommand` - Command to set a point straight (+) or diverging (-), with optional lock/unlock support
- `PointPosition` - Enum for point positions: Straight, Diverging, Undefined
- `TurntableTrack` - Configuration for turntable track positions
- `TrainRouteCommand` - Multi-point path between signals with conflict detection
- `PointLock` - Tracks point command with commit state for locking

**Main Abstractions**:
- `IYardController` - Sends point commands (implementations: `LoggingYardController`, `TestYardController`, `LocoNetYardController`)
- `IKeyReader` - Reads input (implementations: `ConsoleKeyReader`, `UdpKeyReader`, `TestKeyReader`)
- `IPointDataSource` / `ITrainRouteDataSource` - Configuration sources

**Locking System**:
- `TrainRouteLockings` - Manages point locks for train routes with reserve → commit → clear lifecycle
  - `CanReserveLocksFor()` - Pre-flight check for conflicts
  - `ReserveLocks()` / `CommitLocks()` / `ClearLocks()` - Lock lifecycle management
  - Prevents conflicting train routes from being set simultaneously

**Application Flow**:
`NumericKeypadControllerInputs` (BackgroundService) reads input → parses commands → validates against `TrainRouteLockings` → sends via `IYardController`

### Command Syntax

- **Point**: `[number][+/-]` (e.g., `1+` sets point 1 to straight, `1-` sets to diverging)
- **Train Route**: `[from][to]⏎` sets route (e.g., `2131⏎`), `[from][to]/` clears route
- **Multi-signal**: `[from].[via].[to]⏎` uses `.` as divider
- **Turntable**: `+[track]⏎` moves turntable to track position
- **Clear all**: `//` cancels all train routes and clears all locks

### Data Files

Located in `Data/`:
- `Switches.txt` - Point configuration
  - Basic format: `number:address1,address2,...`
  - Lock offset: `LockOffset:1000` (applies to all subsequent points)
  - Address range: `Adresses:1-100` (creates points 1-100 with matching addresses)
  - Turntable: `Turntable:1-32;1000` (tracks 1-32 with address offset 1000)
- `TrainRoutes.txt` - Format: `from-to:point1±,point2±,...`

### Testing Strategy

Uses MSTest with test implementations (`TestKeyReader`, `TestYardController`, `InMemoryPointDataSource`, `InMemoryTrainRouteDataSource`) that allow manual command injection and assertion on accumulated commands. Test DI container is configured in `ServicesExtensions.cs`.

### Dependencies

- `Tellurian.Trains.Adapters.LocoNet` / `Tellurian.Trains.Protocols.LocoNet` - LocoNet hardware communication
- `Microsoft.Extensions.Hosting` - DI container and hosted service pattern
