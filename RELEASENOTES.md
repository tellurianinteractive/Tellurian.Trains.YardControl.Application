# Release Notes

## Version 1.2.0

### New features

- **Coupled-point cascade (single-slip support)** — `[Points]` lines may now declare slave clauses (`&<masterPos>:<slave><slavePos>[,...]`) so that setting one point automatically sets another. Setting a master point on the keypad cascades to all slaves; routes that include a master point are auto-expanded with the slave commands at load time. Routes or commands that would force a single point to two different positions are hard-rejected with a clear error.
- **Revised train number lifecycle** — train numbers now stay where the train physically is. `=[trainnumber]` on a route-set assigns the number to the **origin** signal (previously: destination). Three new arrival commands let the operator advance the number explicitly:
  - `[signal]/` — clear the route up to that signal AND move the train number there
  - `[signal]=⏎` — move the train number to that signal without clearing the route
  - `=[trainnumber]⏎` — advance that train one signal forward along its current route, or remove it if there is no further signal (e.g. next station confirms arrival from an outbound signal)
- **Train number visible at outbound signals** — clearing a route to an outbound main signal now keeps the train number visible at the outbound until next-station arrival is confirmed via `=[trainnumber]⏎` (previously: removed immediately).
- **Train number on queued routes** — when a route is queued due to a conflict, the queued-routes display now shows the train number alongside the signal pair.
- **EBICOS-aligned colour palette** — inactive track is now light gray (was white) and shunting routes are yellow (was orange). Train routes remain green; cancelling routes remain blue.

### Behaviour changes

- **Active route takes precedence over queued route** — when both an active and a queued route exist to the same signal, `[signal]/` clears the active one first (previously: cancelled the queued one).
- **Cancel of non-existent route is silently ignored** — `[signal]/` and `[signal]ESC` produce no message when no route ends at that signal (previously: error feedback).

### Bug fixes

- **File watcher catches editor saves** — auto-reload of station files now subscribes to `Created` and `Renamed` events in addition to `Changed`, so editors that save atomically (Visual Studio, VS Code) trigger reload.
- **`=[trainnumber]⏎` is now deterministic** — when a train number existed at multiple signals (e.g. after re-setting a route over a previously-arrived train), the advance command would non-deterministically pick one. It now prefers the signal that is the origin of an active route.

## Version 1.1.0

### New features

- **Z21 command station support** — The application can now connect to a Roco/Fleischmann Z21 directly over UDP, in addition to LocoNet over serial. Select the command station with `CommandStation:Type` in `appsettings.json` (`"Serial"` or `"Z21"`).
- **Full interoperability with Z21 App and WLANMaus** — When using the Z21, point changes from the yard app are visible in the Z21 App and WLANMaus, and vice versa. All clients stay in sync regardless of who initiated the change.
- **Single-executable publish** — Release archives now contain a single executable plus the `Data` and `wwwroot` folders, instead of dozens of DLLs.

### Breaking configuration change

- The top-level `"SerialPort"` section in `appsettings.json` has been replaced by a nested `"CommandStation"` section:
  ```json
  "CommandStation": {
    "Type": "Serial",
    "SerialPort": { "PortName": "COM5", "BaudRate": 57600 },
    "Z21": { "Address": "192.168.0.111", "CommandPort": 21105, "FeedbackPort": 21106 }
  }
  ```
  Existing installations need to update their `appsettings.json` when upgrading. UDP port 21106 must be allowed through Windows Firewall for Z21 feedback to work.

## Version 1.0.0

First public release of the Yard Control Application.

### Features

- **Interactive web UI** — SVG-based yard diagram with clickable signals, colour-coded point positions, and active route highlighting (green for main routes, orange for shunting routes, blue during cancellation).
- **Signal-based train route setting** — click a from-signal and a to-signal to set a route; the application moves and locks all required points automatically.
- **Automatic route derivation** — routes can be fully derived from the yard topology graph, requiring only the from and to signal numbers in the configuration.
- **Main and shunting routes** — shunting routes use dwarf signals, skip destination signal go-aspects, and release locks immediately.
- **Train number labels** — assign train numbers to signals and see them displayed live on the yard diagram. Numbers follow the train automatically as routes are set.
- **Clear vs cancel semantics** — clearing a route keeps train numbers and releases locks after a safety delay; cancelling removes train numbers and releases locks immediately.
- **Multiple station support** — configure several stations and switch between them at runtime. Each station opens in its own browser window.
- **Unified station configuration** — define the entire station (topology, points, signals, routes, translations) in a single human-readable text file.
- **Flank protection** — routes can include flank protection points that are locked but not on the active path.
- **Point locking** — logical locking prevents conflicting routes; optional hardware locking via configurable address offset for Mollehem switch decoders.
- **Signal control** — signals are set to go/stop automatically when routes are set or cleared. Five signal types supported: OutboundMain, InboundMain, MainDwarf, ShuntingDwarf, and Hidden.
- **Train route queueing** — when a route conflicts with an active route, it is automatically queued and executed as soon as the blocking route is cleared or cancelled. Queued routes are shown in the UI and can be cancelled before they execute.
- **All Signals Stop** — emergency button to immediately set all signals to red.
- **Reset all points** — set all unlocked points to straight position with a single command.
- **Numeric keypad input** — all operations can be performed from a numeric keypad for hands-free wireless control.
- **Turntable control** — move turntable to specified track positions via numpad commands.
- **LocoNet integration** — point commands, signal control, and position feedback via LocoNet protocol.
- **Real-time position feedback** — point changes from any source (other throttles, apps) are reflected in the UI.
- **Live configuration reload** — edit data files while the application is running; changes are detected and applied automatically.
- **Localisation** — UI and track labels available in English, Swedish, Danish, Norwegian, and German.
- **Serial port validation** — startup diagnostics for LocoNet hardware connectivity in production mode.
