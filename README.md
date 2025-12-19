# Yard Control Application

A .NET console application for controlling model train yard switches via LocoNet protocol using numeric keypad input.

The application reads input from a numeric keypad (or UDP network input), translates commands to LocoNet turnout commands, and sends them to the layout hardware. It supports individual switch control and predefined train paths between signals with automatic conflict detection.

## Numpad Commands

### Switch Commands
Control individual switches by entering the switch number followed by direction:

| Command | Description |
|---------|-------------|
| `[number]-` | Set switch to straight (e.g., `1-`) |
| `[number]+` | Set switch to diverging (e.g., `1+`) |

### Train Path Commands
Set or clear predefined routes between signals:

| Command | Description |
|---------|-------------|
| `[from][to]⏎` | Set train route (e.g., `2131⏎` sets path from signal 21 to 31) |
| `[from][to]*` | Clear train route (e.g., `2131*`) |
| `[from].[via].[to]⏎` | Multi-signal route using `.` as divider (e.g., `21.33.41⏎`) |
| `[signal]*` | Clear all route up to a signal (e.g., `31*`) |
| `/` | Cancel all train routes and clear all locks |

### Other
| Command | Description |
|---------|-------------|
| `⌫` | Clear current input buffer |

## Configuration

### Switches (Data/Switches.txt)
Maps switch numbers to LocoNet addresses. Format: `number:address1,address2,...`

```
1:840
3:842
7:835,836
```

A switch can have multiple addresses if it controls multiple LocoNet turnouts.

### Train Routes (Data/TrainRoutes.txt)
Defines paths between signals with required switch positions. Format: `from-to:switch1±,switch2±,...`

```
21-31:1-,3-,7-
21-33:1-,3+,11+
21-41:1-,3-,7-,16-,19-,25-,27-,4-,2-
```

- `from-to` are signal numbers (typically 2 digits)
- `-` after switch number means straight
- `+` after switch number means diverging

When a train path is set, the involved switches are locked to prevent conflicting paths until the path is cleared.

## Controlling Signals

Setting a train route also should sets signals along the train route up to the destination signal, which is set to stop. 

- On inbound routes, the entry signal is set to proceed, and all intermediate signals are set to proceed as well.
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
