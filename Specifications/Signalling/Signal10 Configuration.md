# Möllehem Signal10 Decoder Configuration Guide

Summary of key configuration concepts from Instruktion_Signal10.pdf v0.13.

## Your Conclusion is Correct

Controlling signals with the Signal10 decoder works in two ways:

1. **Stop/Go basic state** → LocoNet Switch commands on signal's address
2. **Speed aspects (Kör 40, försignalering, etc.)** → Decoder listens to point positions, occupancy sensors, and other signals

The decoder handles signal aspect logic internally based on configuration.

---

## Core Concept: Signal Base State (Grundläge)

Each signal has a **base state** controlled via LocoNet Switch messages:

| Switch Command | Base State | Result |
|----------------|------------|--------|
| CLOSED         | Kör (Go)   | Shows "Kör" if all rules allow |
| THROWN         | Stopp (Stop) | Always shows "Stopp" |

**Key insight**: Setting base state to "Kör" doesn't guarantee the signal shows green - it only *permits* green if configured rules are satisfied.

---

## Essential Per-Signal Configuration

### 1. Basic Settings (Required)

| Setting | SV | Description |
|---------|-----|-------------|
| Signal Type | 100 | HSI2, HSI3, HSI4, HSI5, DVSI, HVDv, etc. |
| First LED | 101 | Position on LED serial bus (1-64) |
| Startup Default | 100 bit 7 | Initial state: Stop (0) or Go (1) |

### 2. Diverging Switches (for Kör 40 aspects)

Set addresses of facing points that control speed:

| Setting | SV | Description |
|---------|-----|-------------|
| Diverging Switch 1 | 105 | Address of facing point |
| Diverging Switch 2 | 107 | Second facing point (optional) |
| Diverging Switch 3 | 109 | Third facing point (optional) |

**How it works**: When signal shows "Kör", decoder checks these switch positions:
- Switch in Closed/Straight → Signal shows "Kör 80"
- Switch in Thrown/Diverging → Signal shows "Kör 40"

### 3. Försignalering (Distant Signal Aspect)

| Setting | SV | Description |
|---------|-----|-------------|
| Next Signal | 103 | Address of the signal being pre-signaled |

**How it works**: Decoder reads status of "next signal" and automatically displays appropriate expect aspect (Vänta Kör, Vänta Stopp, etc.).

### 4. GO Rules (Conditions for "Kör")

Up to 6 conditions that MUST be satisfied for signal to show "Kör":

| Setting | Description |
|---------|-------------|
| GO 1-6 Type | Switch status, Occupancy sensor, Signal (SE), Extra rule |
| GO 1-6 Address | Address of the referenced device |
| GO 1-6 Status | Required state (Thrown/Closed or Occupied/Free) |
| GO 1-6 Logic | OR / AND relation to previous condition |

**Example**: Entry signal should only show "Kör" if:
- Track behind signal is free (occupancy sensor)
- Facing switch is set correctly (switch status)

```
GO Rule: "V100/Closed AND T200/Free"
```

---

## Dwarf Signals (Dvärgsignaler)

Dwarf signals need extra configuration because they have multiple movement aspects.

### Shunting Dwarf (DVSI) Control

| Control | Source | Result |
|---------|--------|--------|
| Signal address | Switch command | Stop ↔ Movement allowed |
| Diverging Sw 1 | Switch status | Selects aspect when thrown |
| Diverging Sw 2 | Switch status | Selects aspect when thrown |

**Aspect selection** (both switches Thrown = 0):
| Sw 1 | Sw 2 | Aspect |
|------|------|--------|
| 0 | 0 | Movement allowed (vertical) |
| 1 | 0 | Movement allowed, track not clear (NW diagonal) |
| 0 | 1 | Movement allowed, check points (NE diagonal) |

**Important**: SV 106 and 108 must be set to "Yes, handle feedback" for the extra addresses to work.

### Main Dwarf (HVDv)

The 7-lamp main dwarf is defined as one signal with type HVDv (16).
Uses same control scheme as shunting dwarf, but with additional train aspects.

---

## Trigger Rules (Auto-Return to Stop)

For automatic signal behavior (e.g., return to stop after train passes):

| Setting | Description |
|---------|-------------|
| Signal Number | Which signal (1-10) |
| Target State | State to set (usually Stop) |
| Conditions | When to trigger (e.g., occupancy goes occupied) |

**Example**: Set exit signal back to Stop when track behind becomes occupied.

---

## Key System Variables (SV) Quick Reference

| SV | Name | Purpose |
|----|------|---------|
| 21 | Decoder Address | Base address (default 80) |
| 23 | Options | Feedback mode, startup flash, etc. |
| 85 | LED Intensity | Global brightness (0-255) |
| 100-389 | Signal Definitions | Type, LED, rules for each signal |
| 400-489 | Extra Rules | Reusable rule sets |
| 600-699 | Trigger Rules | Auto-state-change conditions |
| 700-725 | Signal Selectors | Switch-dependent signal routing |
| 800-899 | Special Controls | Extra functions for complex signals |

---

## Swedish Signal Types

| Token | Value | LEDs | Description |
|-------|-------|------|-------------|
| Hsi 2 | 1 | 2 | Main signal 2 lights |
| Hsi 3 | 3 | 3 | Main signal 3 lights |
| Hsi 4 | 4 | 4 | Main signal 4 lights (with försignal) |
| Hsi 5 | 5 | 5 | Main signal 5 lights (with försignal) |
| Dvsi | 7 | 4 | Shunting dwarf signal |
| HVDv | 16 | 7 | Main dwarf signal |
| Fsi 2 | 8 | 2 | Distant signal 2 lights |
| Fsi 3 | 9 | 3 | Distant signal 3 lights |

---

## Practical Configuration for Munkeröd

For your station, likely configuration approach:

### Entry Signals (5-lamp KombiSignal - HSI5)
- Type: Hsi 5 (value 5)
- Configure "Next Signal" to point to the exit signal
- Configure "Diverging Switch" addresses for switches after the signal
- Configure "GO Rules" for track occupancy and switch positions

### Exit Signals (2-lamp - HSI2)
- Type: Hsi 2 (value 1)
- Configure "GO Rules" for line clear conditions

### Dwarf Signals
- Shunting dwarf: Type Dvsi (value 7)
- Main dwarf: Type HVDv (value 16)
- Configure Diverging Sw 1 and 2 for aspect control
- Set SV 106/108 to enable feedback generation

---

## What You Control from Your Application

Your YardController application only needs to send:

1. **Signal base state** (Stop/Go) via LocoNet Switch commands
2. **Point positions** via LocoNet Switch commands

The Signal10 decoder handles:
- Which exact aspect to display based on configured rules
- Försignalering automatically from configured "Next Signal"
- Speed aspects (Kör 40/80) based on configured diverging switches
- GO rule evaluation for safety conditions

---

## Configuration Tools

- Use MGP's programming app to configure SVs
- The app shows all options in clear text
- Decoder address defaults to 80
- Signals 1-10 are addressed at decoder_address + 0 to decoder_address + 9
