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

This is a .NET 10.0 console application for controlling model train yard switches via LocoNet protocol. It uses numeric keypad input to send switch commands to hardware.

### Core Components

**Domain Models** (record types):
- `Switch` - Configuration mapping switch numbers to LocoNet addresses
- `SwitchCommand` - Command to set a switch straight (+) or diverging (-)
- `TrainRouteCommand` - Multi-switch path between signals with conflict detection

**Main Abstractions**:
- `IYardController` - Sends switch commands (implementations: `LoggingYardController`, `TestYardController`, `LocoNetYardController`)
- `IKeyReader` - Reads input (implementations: `ConsoleKeyReader`, `UdpKeyReader`, `TestKeyReader`)
- `ISwitchDataSource` / `ITrainPathDataSource` - Configuration sources

**Application Flow**:
`NumericKeypadControllerInputs` (BackgroundService) reads input → parses commands → validates against `SwitchLockings` → sends via `IYardController`

### Command Syntax

- **Switch**: `[number][+/-]` (e.g., `1+` sets switch 1 to diverging)
- **Train Path**: `[from]-[to][=/*/]` (e.g., `21-31=` sets path from signal 21 to 31)
- **Multi-signal**: `[from].[via].[to]/` uses `.` as divider

### Data Files

Located in `Data/`:
- `Switches.txt` - Format: `number:address1,address2,...`
- `TrainPaths.txt` - Format: `from-to:switch1±,switch2±,...`

### Testing Strategy

Uses MSTest with test implementations (`TestKeyReader`, `TestYardController`, `InMemory*DataSource`) that allow manual command injection and assertion on accumulated commands. Test DI container is configured in `ServicesExtensions.cs`.

### Dependencies

- `Tellurian.Trains.Adapters.LocoNet` / `Tellurian.Trains.Protocols.LocoNet` - LocoNet hardware communication
- `Microsoft.Extensions.Hosting` - DI container and hosted service pattern
