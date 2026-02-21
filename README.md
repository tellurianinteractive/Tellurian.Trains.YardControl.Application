# Yard Control Application

A .NET Blazor Server application for controlling model train yard points (turnouts) via LocoNet protocol.
It provides a graphical web UI with interactive signal-based train route setting, as well as numeric keypad input for hands-free operation.

The application supports individual point control, train routes between signals with automatic route derivation from topology, point locking to prevent conflicting movements, signal control, and real-time position feedback from LocoNet.

### Highlights

- **Unified station configuration** — Define your entire station (topology, points, signals, routes, translations) in a single text file with a human-readable format.
- **Automatic route derivation** — Just specify the from and to signals; the application finds the shortest path through the topology and determines the required point positions automatically.
- **Multiple station support** — Configure several stations and switch between them at runtime from the UI.
- **Signal control** — Signals are set to go/stop automatically when routes are set or cleared, with hardware integration via LocoNet accessory addresses.
- **Live configuration reload** — Edit data files while the application is running; changes are detected and applied automatically.
- **Localisation** — UI and track labels available in English, Swedish, Danish, Norwegian, and German.

![Munkeröd yard](Specifications/Munkeröd.png)

## Usage

### Web Interface

The browser-based GUI displays the full yard topology as an interactive SVG diagram:

- **Signals** are shown as red (stop) or green (go) indicators with direction arrows.
- **Points** display their current position with colour coding (straight/diverging/unknown).
- **Active train routes** are highlighted in green along the track path, or in blue when cancelling (locks held).
- **Labels** identify tracks, signals, and points.

To set a train route, click a signal to select the *from* signal, then click a second signal to select the *to* signal. The route is set, the involved points are moved and locked, and the from signal turns green (go). CTRL+click on a green signal to cancel its route. Shift+click on any signal to toggle it between stop and go manually.

Above the yard diagram is an **All Signals Stop** emergency button that immediately sets all green signals to red.

The footer provides:
- **Show Grid** checkbox to toggle coordinate grid overlay (useful for editing topology files).
- **Query Point States** button to request current positions from hardware (or simulate random positions in development mode).
- **Reset All Points** button to set all unlocked points to straight position (locked points are skipped).
- **Language selector** to switch the UI language.

### Numpad Commands

All operations can be performed from a numeric keypad, which is useful for wireless hands-free control.

#### Point Commands

| Command | Description |
|---------|-------------|
| `[number]+` | Set point to straight (e.g., `1+`) |
| `[number]-` | Set point to diverging (e.g., `1-`) |

A point number can represent a single point or multiple coupled points (e.g., opposing crossover points) that move together.

#### Train Route Commands

| Command | Description |
|---------|-------------|
| `[from][to]⏎` | Set main train route (e.g., `2131⏎` sets path from signal 21 to 31) |
| `[from][to]*` | Set shunting route |
| `[from][to]/` | Clear train route (e.g., `2131/`) |
| `[from].[to]⏎` | When signal numbers have different digit counts, use `.` as divider (e.g., `121.33⏎`) |
| `[from].[via].[to]⏎` | Multi-signal route (e.g., `21.31.35⏎`) |
| `[signal]/` | Clear all routes up to a signal (e.g., `31/`) |
| `//` | Cancel all train routes and clear all locks |
| `**` | Set all signals to stop |

**Note:** Clearing up to a signal can be used to manually confirm that a train has reached its destination signal, releasing the locks on points used in the route. This is useful when occupancy detection is not available.

#### Turntable Commands

| Command | Description |
|---------|-------------|
| `+[track number]⏎` | Move turntable to the specified track position |

#### Other

| Command | Description |
|---------|-------------|
| `⌫` | Clear current input buffer |
| `+` `-` | Reload configuration |

## LocoNet Communication

### Setting Points

Point commands are sent as LocoNet turnout (accessory) commands. Each point number maps to one or more LocoNet addresses configured in `Points.txt`. A negative address inverts the direction: `Closed` becomes `Thrown` and vice versa.

When a train route is set, the application sends commands for all points in the route in sequence.

### Point Locking

When `LockOffset` is configured, hardware point locking is supported. Setting a train route:

1. Sends turnout commands to move points to the correct positions.
2. Sends lock commands (`Closed`) to each point's lock address (*address + offset*).

When a train route is cleared:

1. Signals are immediately set to stop.
2. The route is shown in blue on the UI, indicating that locks are still held.
3. After a configurable delay, unlock commands (`Thrown`) are sent to the lock addresses and the route is fully cleared.

This two-phase cancellation mirrors real railway operations where point locks are held for a safety period after signal clearance. The delay is configured in `TrainRoutes.txt` (see below). In development mode, the delay is always 5 seconds.

This feature is designed for **Möllehem** switch decoders that support individual point locks via a parallel address range. When a point is locked, it cannot be altered via LocoNet, XpressNet, or buttons connected to the decoder.

Logical locking is always active regardless of hardware support: the application prevents conflicting train routes from being set when the same point would need different positions.

### Position Feedback

The application listens for LocoNet switch report messages to track the actual position of each point. When a switch report is received, the LocoNet address is mapped back to the corresponding point number and the position is updated in real-time on the UI.

This means that point changes made from other sources (e.g., ROCO Z21 app, other throttles) are reflected in the yard display.

For paired points with sub-point suffixes (e.g., `1a`, `1b`), each sub-point tracks its position independently from the same or different LocoNet addresses.

## Configuration

Configuration files are located in `YardController.Web/Data/` (paths can be changed in `appsettings.json`). Files are watched for changes and automatically reloaded.

Multiple stations can be configured in `appsettings.json`:

```json
{
  "Stations": [
    { "Name": "Munkeröd", "DataFolder": "Data\\Munkeröd\\Munkeröd.txt" },
    { "Name": "Steinsnes", "DataFolder": "Data\\Steinsnes" }
  ]
}
```

If `DataFolder` points to a `.txt` file, the unified single-file format is used. If it points to a directory, the legacy multi-file format is used. Both formats are fully supported.

### Unified Station Format (recommended)

The recommended way to configure a station is a single text file with named sections. This keeps all station data together and enables automatic route derivation.

```
Munkeröd

[Settings]
LockOffset:1000
LockReleaseDelay:30

[Tracks]
1.1-1.2-1.3-1.4                         ' track segments (left-to-right)

[Points]
2.3(1a>)-2.4  @840a,843b                ' forward point with LocoNet address
2.5(<2)-2.6  @842                        ' backward point

[Signals]
1.1:21>:u  @900                          ' outbound main signal driving right
1.4:<31:i  @901;951                      ' inbound main signal with feedback address

[Routes]
21-31                                    ' auto-derived from topology
21-31:x25+                               ' auto-derived + explicit flank protection
21-35:21.31.35                           ' composite route via intermediate signal

[Labels]
1.1[Goods track]1.4

[Gaps]
2.3|2.4                                  ' occupancy gap on a link

[Translations]
en;sv;da;nb;de
Track;Spår;Spor;Spor;Gleis
Goods track;Godsspår;Godsspor;Godsspor;Güterspur

[Turntable]
Tracks:1-17
Offset:196
```

Comments start with a single quote (`'`). Section names are case-insensitive.

**Route auto-derivation:** When a route line contains only signal numbers (e.g., `21-31` without a colon), the application uses the topology graph to find the shortest path between the signals and automatically determines which points need to be set to straight or diverging. You can also specify only flank protection points (prefixed with `x`) and let the on-route points be auto-derived.

**Signal types:** `u`=OutboundMain, `i`=InboundMain, `h`=MainDwarf, `d`=ShuntingDwarf, `x`=Hidden.

A conversion tool (`StationFileConverter`) is available to migrate from the legacy multi-file format to the unified format.

### Legacy Multi-File Format

The legacy format uses separate files in a directory. This is still fully supported.

#### Points (Points.txt)

Maps point numbers to LocoNet addresses.

**Basic format:** `number:address1,address2,...`

```
1:840
3:842
7:835,-836
```

- A point number can have multiple addresses if it controls multiple turnouts.
- Negative addresses flip direction: `Thrown` becomes `Closed` and vice versa.

**Sub-point suffixes:** Append a letter to an address to track sub-points independently.

```
1:840a,843b
```

This maps address 840 to sub-point `1a` and address 843 to sub-point `1b`. Each sub-point displays its own position feedback in the UI, which is important for crossovers where external applications can move each motor independently.

**Grouped format:** Different addresses for straight vs diverging positions.

```
23:(823,820)+(823,-816,820)-
```

The `(...)` before `+` lists straight addresses, and `(...)` before `-` lists diverging addresses. Addresses can also have sub-point suffixes in grouped format.

**Lock offset:** Enables hardware locking for subsequent point definitions.

```
LockOffset:1000
```

Points defined after this line will use *address + 1000* as their lock address.

**Address range:** Creates points where the number equals the address.

```
Adresses:800-853
```

This is useful for verifying individual switches during initial setup.

**Turntable:**

```
Turntable:1-17;196
```

Creates turntable tracks 1-17 with addresses 197-213 (track number + offset). The turntable command is always `Closed`.

#### Train Routes (TrainRoutes.txt)

Defines paths between signals with required point positions.

**Basic format:** `from-to:point1±,point2±,...`

```
21-31:1+,3+,7+
35-41:x25+,27+,4+,2+
```

- `from-to` are signal numbers.
- `+` means straight, `-` means diverging.
- `x` prefix marks flank protection points (locked but not on the active path).

**Composite format:** Builds longer routes from shorter ones.

```
21-35:21.31.35
```

This combines routes 21-31 and 31-35. Referenced routes must be defined earlier in the file.

**Lock release delay:** Configures the delay (in seconds) between signal stop and lock release when cancelling routes.

```
LockReleaseDelay:30
```

Routes cancelled during this period are shown in blue on the UI. After the delay expires, unlock commands are sent and the route is fully cleared. This prevents conflicting route requests from being accepted too soon after cancellation. In development mode, the delay is always 5 seconds regardless of this setting.

Comments start with a single quote (`'`).

#### Topology (Topology.txt)

The yard topology is modeled as a directed graph:

- **Track node** - 2D position (`row.column`).
- **Track graph** - Graph of `TrackNode`s connected by `TrackLink`s. Links are directional (left-to-right by convention).
- **Point definition** - Label, SwitchPoint coordinate, DivergingEnd coordinate, Direction (Forward `>` / Backward `<`). 
- **Signal definition** - Name (numeric), Coordinate, DrivesRight (direction), optional IsHidden.

#### Signals (Signals.txt)

Maps signal numbers to LocoNet addresses for stop/go control.

**Basic format:** `signalNumber:address`

```
21:900
31:901
```

**With feedback address:** `signalNumber:address;feedbackAddress`

```
21:900;950
```

The feedback address is used to receive state confirmations from the hardware.

Signals without an address entry are still tracked internally (e.g., for route-based signal control) but no hardware commands are sent.

Comments start with a single quote (`'`).

## Translations

The application UI is currently localised in English, Swedish, Danish, Norwegian, and German.

Track labels in the yard diagram (e.g., "Goods track", "Headshunt") can be translated per language. In the unified format, translations are defined in the `[Translations]` section of the station file. In the legacy format, a separate CSV file (`Data/LabelTranslations.csv`) is used:

```
en;da;de;nb;sv
Track;Spor;Gleis;Spor;Spår
Goods track;Godsspor;Güterspur;Godsspor;Godsspår
Headshunt;Træktilbagespor;Ausziehgleis;Uttrekksspor;Utdrag
Munkeröd;Munkerød;Munkeröd;Munkerød;Munkeröd
```

The station name is also translated through this file.

## Signals

When a train route is set, the from signal and any intermediate signals are set to go (green). When a route is cleared, those signals are set back to stop (red), unless they are still needed by another active route.

The `//` command (cancel all routes) also sets all route signals to stop before releasing locks. The `**` command sets all signals to stop regardless of route state.

Detailed signal aspects are typically implemented in the yard's internal control system and are not part of this application.

## Occupation Feedback

The intention is that when Munkeröd gets *occupation feedback*, this will also be reflected in the UI, so that green train routes become red when occupied.
The train route will also automatically reset when the train reaches the final signal.

## Environment

The application requires .NET 10.0. In development mode, a simulated controller is used so no LocoNet hardware is needed. Run with `dotnet run --project YardController.Web/YardController.Web.csproj` and open the displayed URL in a browser.
