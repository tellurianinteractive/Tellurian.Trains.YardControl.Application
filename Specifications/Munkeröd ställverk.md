# Munkeröd Ställverk - Web UI Design

## Overview

Web-based visualization and control of Munkeröd station layout showing:
- Point positions (clickable for control)
- Train routes (green when set)
- Point lockings (visual indicator when locked)

**Key Design Decisions:**
- Display + Control mode (clicking operates points/routes)
- SVG generated from text configuration (same pattern as Points.txt and TrainRoutes.txt)

---

## Technology Stack

### Blazor Server + SignalR

**Why Blazor Server:**
- Real-time updates via SignalR (built-in)
- C# end-to-end (shares models with YardController.App)
- Direct access to yard state
- No JavaScript required for state management

### Graphics: SVG

**Why SVG:**
- Scalable to any screen size
- CSS styling for colors (routes, point positions)
- Easy to change colors programmatically
- Generated from configuration data

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Blazor Server                        │
│  ┌──────────────┐   ┌─────────────┐   ┌───────────────┐ │
│  │ Station.razor│◄──│  YardHub    │◄──│ YardController│ │
│  │ (SVG render) │   │  (SignalR)  │   │ Listener      │ │
│  └──────────────┘   └─────────────┘   └───────┬───────┘ │
└───────────────────────────────────────────────┼─────────┘
                                                │
                    ┌───────────────────────────┴──────┐
                    │         LocoNet Hardware         │
                    │             (Points)             │
                    └──────────────────────────────────┘
```

---

## Data Models

### Grid Coordinate
```csharp
public readonly record struct GridCoordinate(int Row, int Column)
{
    // Parse "x.y" format where x=row, y=column
    public static GridCoordinate Parse(string s)
    {
        var parts = s.Split('.');
        return new GridCoordinate(int.Parse(parts[0]), int.Parse(parts[1]));
    }
}
```

### Yard Topology (Parsed from Topology.txt)
```csharp
public record YardTopology(
    string Name,
    IReadOnlyList<TrackSegment> TrackSegments,
    IReadOnlyList<PointDefinition> Points,
    IReadOnlyList<LabelDefinition> Labels);

// A continuous track path parsed from one line
public record TrackSegment(IReadOnlyList<GridCoordinate> Coordinates);

public record PointDefinition(
    int Number,                    // Point number from (+n) or (-n)
    GridCoordinate SwitchPoint,    // Where the point mechanism is
    GridCoordinate StraightEnd,    // End of straight arm
    GridCoordinate DivergingEnd);  // End of diverging arm

public record LabelDefinition(string Text, GridCoordinate Coordinate);
```

### Yard State (Runtime)
```csharp
public class YardState
{
    private readonly Dictionary<string, PointPosition> _pointPositions = new();
    private readonly HashSet<string> _lockedPoints = new();
    private readonly HashSet<string> _activeRoutePoints = new();

    public void UpdatePointPosition(string pointId, PointPosition position)
        => _pointPositions[pointId] = position;

    public void SetPointLocked(string pointId, bool locked)
    {
        if (locked) _lockedPoints.Add(pointId);
        else _lockedPoints.Remove(pointId);
    }

    public void SetActiveRoutePoints(IEnumerable<string> pointIds)
    {
        _activeRoutePoints.Clear();
        foreach (var p in pointIds) _activeRoutePoints.Add(p);
    }

    public bool IsPointInActiveRoute(string pointId)
        => _activeRoutePoints.Contains(pointId);

    public bool IsPointLocked(string pointId)
        => _lockedPoints.Contains(pointId);

    public PointPosition GetPointPosition(string pointId)
        => _pointPositions.GetValueOrDefault(pointId, PointPosition.Undefined);
}
```

---

## Text Layout Configuration (Topology.txt)

SVG is generated from a text configuration file, following the same pattern as `Points.txt` and `TrainRoutes.txt`.

### File Format

```
Legend:
    x.y = grid coordinate (row.column)
    - = track segment to next coordinate
    | = detection section isolation (for future use)
    (<n) = signal n facing left (for future use)
    (n>) = signal n facing right (for future use)
    (+n) = point n, straight arm continues to column+1, same row
    (-n) = diverging arm of point n (connects to previous coordinate)
    [Label] = text label at current coordinate

'Comment lines start with single quote
```

### Syntax Elements

| Element | Meaning | Example |
|---------|---------|---------|
| `x.y` | Grid coordinate (row.column) | `2.7` = row 2, column 7 |
| `-` | Track continues to next coordinate | `2.0-2.3` |
| `(+n)` | Point n, straight arm | `2.7(+2)` = point 2 at row 2, col 7 |
| `(-n)` | Point n, diverging arm | `1.10(-6)` = diverging arm of point 6 |
| `[text]` | Label at coordinate | `1.13[1c]` = label "1c" at 1.13 |
| `\|` | Detection isolation (future) | `2.3\|2.4` |
| `(<n)` | Signal n facing left (future) | `(<69)` |
| `(n>)` | Signal n facing right (future) | `(22>)` |

### Example

```
'Track 1c with diverging from point 6
1.10(-6)-1.11-1.13[1c]

'Main line with points 2 and 6
2.0-2.3-2.7(+2)-2.10(+6)-2.13[2b]

'Track 3
3.0-3.3-3.13[3b]
```

### Point Geometry

When a point `(+n)` is encountered at coordinate `x.y`:
- **Switch point**: `x.y` (where the point mechanism is)
- **Straight arm ends**: `x.y+1` (same row, next column)
- **Diverging arm**: connects to the coordinate marked with `(-n)`

### Rendering Logic

1. Parse each line as a track segment path
2. Convert `x.y` coordinates to pixels: `px = column * cellSize`, `py = row * cellSize`
3. Draw lines between consecutive coordinates
4. Draw circles at each coordinate for smooth corners
5. Extract points from `(+n)` and `(-n)` markers
6. Place labels from `[text]` elements
7. Apply colour based on state (white = normal, green = route active, orange = locked)

---

## SVG Structure

```xml
<svg viewBox="0 0 800 320">
  <!-- Background -->
  <rect fill="black" width="100%" height="100%"/>

  <!-- Track layer -->
  <g id="tracks">
    <g id="track-1" class="track">
      <line x1="40" y1="160" x2="80" y2="160"/>
      <circle cx="40" cy="160" r="3"/>
      <circle cx="80" cy="160" r="3"/>
    </g>
  </g>

  <!-- Points layer -->
  <g id="points">
    <g id="point-1" class="point straight clickable">
      <line class="arm-straight" x1="160" y1="120" x2="200" y2="120"/>
      <line class="arm-diverging" x1="160" y1="120" x2="200" y2="80"/>
      <circle cx="160" cy="120" r="10" class="point-circle"/>
      <text class="point-label" x="160" y="120">1</text>
    </g>
  </g>

  <!-- Labels layer -->
  <g id="labels">
    <text class="track-label" x="200" y="150">Spår 1</text>
  </g>
</svg>
```

### CSS Classes

```css
.yard-view {
    --track-colour: white;
    --route-active-colour: lime;
    --point-back-colour: blue;
    --point-text-colour: white;
    --point-locked-colour: orange;
}

/* Track styling */
.track line, .track circle {
    stroke: var(--track-colour);
    stroke-width: 6;
    fill: var(--track-colour);
}

/* Point arms */
.point .arm-straight,
.point .arm-diverging {
    stroke: var(--track-colour);
    fill: var(--track-colour);
}
.point.straight .arm-straight { stroke: var(--point-back-colour); fill: var(--point-back-colour); }
.point.diverging .arm-diverging { stroke: var(--point-back-colour); fill: var(--point-back-colour); }

/* Point circle */
.point .point-circle {
    fill: var(--point-back-colour);
}
.point.locked .point-circle {
    fill: var(--point-locked-colour);
}
.point .point-label {
    fill: var(--point-text-colour);
    font-size: 10px;
    font-weight: bold;
}

/* Route active state */
.route-active line, .route-active circle {
    stroke: var(--route-active-colour);
    fill: var(--route-active-colour);
}

/* Interaction */
.clickable { cursor: pointer; }
.clickable:hover { filter: brightness(1.3); }
```

---

## SignalR Hub

```csharp
public class YardHub : Hub
{
    public async Task SendPointUpdate(string pointId, string position)
        => await Clients.All.SendAsync("PointChanged", pointId, position);

    public async Task SendPointLockUpdate(string pointId, bool locked)
        => await Clients.All.SendAsync("PointLockChanged", pointId, locked);

    public async Task SendRouteUpdate(int from, int to, bool active)
        => await Clients.All.SendAsync("RouteChanged", from, to, active);
}
```

---

## Control Service

```csharp
public class YardControlService
{
    private readonly IYardController _yardController;
    private readonly IPointDataSource _pointDataSource;
    private readonly TrainRouteLockings _routeLockings;

    public async Task TogglePointAsync(string pointId, CancellationToken ct)
    {
        var point = await _pointDataSource.GetPointAsync(pointId);
        var currentPosition = _routeLockings.GetPointPosition(pointId);
        var newPosition = currentPosition == PointPosition.Straight
            ? PointPosition.Diverging
            : PointPosition.Straight;

        var command = PointCommand.Create(pointId, newPosition, point.Addresses, point.LockAddressOffset);
        await _yardController.SetAsync(command, ct);
    }

    public async Task SetRouteAsync(int fromSignal, int toSignal, CancellationToken ct)
    {
        // Uses existing TrainRouteCommand infrastructure
    }

    public async Task ClearRouteAsync(int fromSignal, int toSignal, CancellationToken ct)
    {
        // Clears route and releases point locks
    }
}
```

---

## Implementation Phases

### Phase 1: Topology Parser + Static SVG
- Define `YardTopology` record types and `GridCoordinate` struct
- Create `TextFileTopologyDataSource` (same pattern as `TextFilePointDataSource`)
- Implement `YardView` Blazor component (rendering only)
- Create initial `Topology.txt` file for Munkeröd

### Phase 2: Click Handling + Control
- Add click handlers to `YardView` component
- Create `YardControlService` to bridge UI with `IYardController`
- Implement point toggling via UI

### Phase 3: Real-time Updates
- SignalR hub for pushing state changes
- Live updates from hardware reflected in UI
- Point position feedback

### Phase 4: Route Integration
- Connect to existing `TrainRouteLockings`
- Highlight active routes in green
- Show locked points with visual indicator (orange)

---

## Related Data Files

### TrainRoutes.txt - Two-Layer Structure

Train routes are defined in two layers:

**1. Adjacent signal routes** - direct routes with point positions:
```
21-31:1+,3+,7+
31-35:16+,19+
```

**2. Compound routes** - sequences of adjacent routes using `.` separator:
```
21-35:21.31.35       ' Route 21→35 = 21→31 then 31→35
51-41:51.61.65.41    ' Route 51→41 = 51→61→65→41
```

The parser expands compound routes by looking up each adjacent segment and combining their point requirements.

---

## Project Structure

```
Tellurian.Trains.YardController.App/
├── YardController.App/           (existing console app)
│   └── Data/
│       ├── Points.txt            (existing)
│       ├── TrainRoutes.txt       (existing)
│       └── Topology.txt          (NEW - track layout)
├── YardController.Tests/         (existing tests)
├── YardController.Shared/        (NEW - shared models/services)
│   ├── Models/
│   │   ├── GridCoordinate.cs
│   │   ├── YardTopology.cs
│   │   └── YardState.cs
│   └── Services/
│       ├── ITopologyDataSource.cs
│       ├── TextFileTopologyDataSource.cs
│       └── YardControlService.cs
└── YardController.Web/           (NEW - Blazor Server)
    ├── Program.cs
    ├── Components/
    │   ├── YardView.razor
    │   └── Pages/
    │       └── Station.razor
    └── Hubs/
        └── YardHub.cs
```
