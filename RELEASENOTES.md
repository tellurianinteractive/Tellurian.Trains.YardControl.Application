# Release Notes

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
- **All Signals Stop** — emergency button to immediately set all signals to red.
- **Reset all points** — set all unlocked points to straight position with a single command.
- **Numeric keypad input** — all operations can be performed from a numeric keypad for hands-free wireless control.
- **Turntable control** — move turntable to specified track positions via numpad commands.
- **LocoNet integration** — point commands, signal control, and position feedback via LocoNet protocol.
- **Real-time position feedback** — point changes from any source (other throttles, apps) are reflected in the UI.
- **Live configuration reload** — edit data files while the application is running; changes are detected and applied automatically.
- **Localisation** — UI and track labels available in English, Swedish, Danish, Norwegian, and German.
- **Serial port validation** — startup diagnostics for LocoNet hardware connectivity in production mode.
