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
Set or clear predefined paths between signals:

| Command | Description |
|---------|-------------|
| `[from][to]=` | Set train path (e.g., `2131=` sets path from signal 21 to 31) |
| `[from][to]*` | Clear train path (e.g., `2131*`) |
| `[from].[via].[to]=` | Multi-signal path using `.` as divider (e.g., `21.33.41=`) |
| `[signal]*` | Clear all paths for a signal (e.g., `31*`) |
| `/` | Cancel all train paths and clear all locks |

### Other
| Command | Description |
|---------|-------------|
| `<` | Clear current input buffer |

## Configuration

### Switches (Data/Switches.txt)
Maps switch numbers to LocoNet addresses. Format: `number:address1,address2,...`

```
1:840
3:842
7:835,836
```

A switch can have multiple addresses if it controls multiple LocoNet turnouts.

### Train Paths (Data/TrainPaths.txt)
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
