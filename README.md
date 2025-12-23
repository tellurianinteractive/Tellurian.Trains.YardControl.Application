# Yard Control Application

A .NET console application for controlling model train yard switches via LocoNet protocol using numeric keypad input.

The application reads input from a numeric keypad (or UDP network input), translates commands to LocoNet turnout commands, and sends them to the layout hardware. It supports individual switch control and predefined train paths between signals with automatic conflict detection.

## Numpad Commands

### Switch Commands
Control individual switches by entering the switch number followed by direction:

| Command | Description |
|---------|-------------|
| `[number]+` | Set switch to straight (e.g., `1+`) |
| `[number]-` | Set switch to diverging (e.g., `1-`) |

### Train Path Commands
Set or clear predefined routes between signals:

| Command | Description |
|---------|-------------|
| `[from][to]⏎` | Set main train route (e.g., `2131⏎` sets path from signal 21 to 31) |
| `[from][to]*` | Set shunting train route (e.g., `2131*`) |
| `[from][to]/` | Clear train route (e.g., `2131/`) |
| `[from].[to]⏎` | When signal numbers have different number of digits using `.` as divider (e.g., `21.33⏎`) |
| `[from].[via].[to]⏎` | Multi-signal route using `.` as divider (e.g., `21.31.35⏎`) |
| `[signal]/` | Clear all routes up to a signal (e.g., `31/`) |
| `//` | Cancel all train routes and clear all locks |

**Note:** Clearing up to a signal can be used to manually confirm that a train has reached its destination signal,
releasing the locks on switches used in the route. Useful when occupancy detection is not implemented.

### Other
| Command | Description |
|---------|-------------|
| `⌫` | Clear current input buffer |

## Configuration

### Switches (Data/Switches.txt)
Maps switch numbers to switch addresses. Format: `number:address1,address2,...`

```
1:840
3:842
7:835,836
```

A switch number can have multiple addresses if it controls multiple turnouts.
For example, opposing track switches where both are either in a straight position or in a diverging position.

### Train Routes (Data/TrainRoutes.txt)
Defines paths between signals with required switch positions.

**Basic format:** `from-to:switch1±,switch2±,...`

```
21-31:1+,3+,7+
21-33:1+,3-,11+
31-35:16+,19+
31-37:16+,19-
35-41:25+,27+,4+,2+
```

- `from-to` are signal numbers between adjacent signals
- `+` after switch number means straight
- `-` after switch number means diverging

**Composite format:** `from-to:from.via.to` to build longer routes from shorter ones

```
21-35:21.31.35
21-41:21.35.41
```

This defines route 21-35 as the combination of routes 21-31 and 31-35.
The referenced routes must be defined earlier in the file.

When a train route is set, the involved switches are locked to prevent conflicting paths until the path is cleared.
Note that these locks are logical and do not affect physical switch operation, so manual switch changes 
using other means than the app can still occur.

## Controlling Signals

Setting a train route also should sets signals along the train route up to the destination signal, which is set to stop. 

- On inbound routes, the entry signal is set to proceed, and all intermediate signals are set to proceed as well, up to the destination signal which is set to stop.
- On outbound routes, the exit signal is set to proceed only after getting permission from the next station. Here, two stategies can be used:
  1. Wait to set the train route until permission is received from the next station, then set all signals to proceed.
  2. Set the train route, and then wait for permission from the next station. When permission is received, set the exit signal to proceed.

Signal control is not part of this application, because it usually is implemented in the yard's
internal control system.

## Feedback from Occupancy Detectors

This application currenly does not handle feedback from occupancy detectors. 
This is a feature planned for future versions.

The feedback will be used to automatically clear train routes when a train reaches its destination signal.
This will automatically release the switch locks.

The yards internal signal logic should also set passed signals to red.
