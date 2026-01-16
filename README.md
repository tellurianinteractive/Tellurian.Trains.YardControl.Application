# Yard Control Application

A .NET console application for controlling model train yard points via LocoNet protocol using numeric keypad input.
The application supports individual point control and predefined train routes between signals.
The application also supports locking train routes, that prevents changing of points in train routes.

The application reads input from a numeric keypad, translates commands to LocoNet turnout commands, and sends them to the layout hardware
that control points.
The hardware must be a motorised point control, and it should not be possible to change points by other means than motors/servos.

Configuration of points and train routes is made in the application's configuration. There is usually no need for changing
configuration of hardware.


## Numpad Commands
All operation can be made from a numeric keypad. You get more flexibility by using a wireless numeric keypad.

### Point Commands
Control points by entering the point number followed by direction:

| Command | Description |
|---------|-------------|
| `[number]+` | Set point to straight (e.g., `1+`) |
| `[number]-` | Set point to diverging (e.g., `1-`) |

A point number can represent a single point or 
for example two opposing points that are to be changed at the same time.

### Train Route Commands
Set or clear predefined routes between signals:

| Command | Description |
|---------|-------------|
| `[from][to]⏎` | Set main train route (e.g., `2131⏎` sets path from signal 21 to 31) |
| `[from][to]/` | Clear train route (e.g., `2131/`) |
| `[from].[to]⏎` | When signal numbers have different number of digits using `.` as divider (e.g., `121.33⏎`) |
| `[from].[via].[to]⏎` | Multi-signal route using `.` as divider (e.g., `21.31.35⏎`) |
| `[signal]/` | Clear all routes up to a signal (e.g., `31/`) |
| `//` | Cancel all train routes and clear all locks |
| `*` | instead of `⏎` sets shunting route. |

**Note:** Clearing up to a signal can be used to manually confirm that a train has reached its destination signal,
releasing the locks on points used in the route. Useful when occupancy detection is not implemented.

### Turntable Commands
| Command | Description |
|---------|-------------|
| `+[track number]⏎` | Moves turntable track to the specified position |


### Other
| Command | Description |
|---------|-------------|
| `⌫` | Clear current input buffer |
| `+` `-` | Reload configuration |

## Configuration

### Points (Turnouts)
Maps point numbers to LocoNet addresses.

**Basic format:** `number:address1,address2,...`

```
1:840
3:842
7:835,-836
```

- A point number can have multiple addresses if it controls multiple turnouts.
For example, opposing track points where both are either in a straight position or in a diverging position.
- Negative addresses flip point direction: `thrown` becomes `closed` and vice versa.

**Address range format:** Creates multiple points where number equals address.

```
Adresses:1-100
```

This creates points 1-100, each with its number as the LocoNet address.
This can be useful when verifying the working of single switches.

**Lock offset configuration:** Enables hardware locking support.

```
LockOffset:1000
1:840
3:842
```
 NOTE: Lock offset only affect points defined after the **LockOffset** definition.

When `LockOffset` is set, setting a point will also send a lock command to *address + offset*.
For example, point 1 with address 840 will have a lock address 1840.

This feature is intended for **Möllehem** switch decoders that will support
individual point locks from spring 2026.

> Their switch decoders uses an address offset for point locking in their hardware.
A point is locked when its *address + offset* is set to `Closed`,
and the lock is released when its *address + offset* is set to `Thrown`.
When a point is locked, it cannot be altered in any way via LocoNet, XpressNet or
buttons connected to their switch decoder.

**Turntable configuration:** Defines turntable track positions.

```
Turntable:1-32;1000
```

This creates turntable tracks 1-32 with addresses 1001-1032 (track number + offset).
Issuing a turntable track command will move the turntable to the desired track.
The turnout command sent is always `Closed`. 

**Examples** 
See example file: `Data/Switches.txt`

### Train Routes
Defines paths between signals with required point positions.

**Basic format:** `from-to:point1±,point2±,...`

```
21-31:1+,3+,7+
21-33:1+,3-,11+
31-35:16+,19+
31-37:16+,19-
35-41:25+,27+,4+,2+
```

- `from-to` are signal numbers between adjacent signals
- `+` after point number means straight
- `-` after point number means diverging

**Composite format:** `from-to:from.via.to` to build longer routes from shorter ones

```
21-35:21.31.35
21-41:21.35.41
```

This defines route 21-35 as the combination of routes 21-31 and 31-35.
The referenced routes must be defined earlier in the file.

When a train route is set, the involved points are locked to prevent conflicting paths until the path is cleared.
Note that these locks are logical and do not affect physical point operation through other means,
so manual point changes using other means than the app can still occur.

Some hardware may support point lockings preventing locked points to be altered via
LocoNet, XpressNet or buttons.

## Controlling Locks
If the hardware supports locking and unlocking points through LocoNet commands, this will also be supported.

To enable hardware locking, add `LockOffset:1000` (or your desired offset) in `Data/Switches.txt` before your point definitions.
When a train route is set:
1. The turnout commands are sent to move points to correct positions
2. Lock commands (close) are sent to the lock addresses (address + offset)

When a train route is cleared:
1. Unlock commands (throw) are sent to the lock addresses

> **Möllehem** will have a solution for locking points during 2026. This will use a set of parallel point addresses
with an offset. If offset is 1000, then locking point with address 1 will be using address 1001.

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

The feedback will be used to automatically clear train routes when a train reaches the last occupancy secition just prior to the destination signal.
This will automatically release the point locks.

The yards internal signal logic should also set passed signals to red,
based on occupancy detection.
