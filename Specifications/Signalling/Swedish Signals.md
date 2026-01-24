# Swedish Railway Signals Reference

This document describes Swedish railway signals as a reference for implementing signal control in the yard controller application.

## Overview

Swedish railway signals use a **speed signalling** system rather than route signalling. Signals inform the driver of the permitted speed, not which route they are taking. Sweden switched to speed signalling in the early 1900s.

**Important**: Swedish main signals have inverted logic compared to many other countries - **more green lights mean slower speeds**. This requires lamp proving to ensure safety, as a lamp failure could give a false "faster" aspect.

**ATC Note**: Throughout this document, "Drive 80" (Kör 80 / Kör) with ATC means the maximum speed that ATC indicates; without ATC it is max 80 km/h. This applies to all signal types.

## Signal Types

### Huvudsignal (Main Signal)

Main signals are located at station entries/exits and line sections with block zones. Mounted on tall poles, typically to the left of the track.

| Swedish Name | English | Speed | Lamp Configuration |
|--------------|---------|-------|-------------------|
| **Stopp** | Stop | 0 | Red light (steady) |
| **Kör 80** | Proceed 80 | Max 80 km/h* | Single green light (steady) |
| **Kör 40, varsamhet** | Proceed 40, caution | Max 40 km/h | Two green lights (steady) |
| **Kör 40, kort väg** | Proceed 40, short route | Max 40 km/h | Three green lights (steady) |

> **Note**: A single green light allows the highest speed. More green lights = more caution = slower speed. *See ATC Note.

### Försignal (Distant/Advance Signal)

Distant signals provide advance warning about the next main signal. Positioned 800-1,200 meters before the main signal with a characteristic round shape and two white markings.

| Swedish Name | English | Meaning |
|--------------|---------|---------|
| **Vänta stopp** | Expect stop | Next signal shows stop |
| **Vänta kör 80** | Expect proceed 80 | Next signal allows 80 km/h |
| **Vänta kör 40** | Expect proceed 40 | Next signal allows 40 km/h |

### Kombinerad Signal (Combined Main and Distant Signal)

Combined signals show both the current aspect and preview the next signal's aspect. Steady light for current aspect + blinking light for preview.

| Swedish Name | English | Current | Next |
|--------------|---------|---------|------|
| **Kör 80, vänta stopp** | Proceed 80, expect stop | 80 km/h* | Stop ahead |
| **Kör 80, vänta kör 80** | Proceed 80, expect 80 | 80 km/h* | 80 km/h ahead |
| **Kör 80, vänta kör 40** | Proceed 80, expect 40 | 80 km/h* | 40 km/h ahead |
| **Kör 40** | Proceed 40 | 40 km/h | - |
| **Kör 40, avkortad tågväg** | Proceed 40, short route | 40 km/h | Short distance to stop |

### Växlingsdvärgsignal (Shunting Dwarf Signal)

Used for shunting operations only. Has **four white/yellow lamps** in a 2x2 grid, with different pairs lit to show aspects.

```
  Lamp Layout:        Stopp        Tillåten      Hinder       Kontrollera
  [1] [2]             ○ ○          ○ ●           ● ○          ○ ●
  [3] [4]             ● ●          ○ ●           ○ ●          ● ○
                     (3+4)        (2+4)         (1+4)        (2+3)
```

| Swedish Name | English | Lamps | Pattern |
|--------------|---------|-------|---------|
| **Stopp** | Stop | 3 + 4 | Lower horizontal |
| **Rörelse tillåten** | Movement allowed | 2 + 4 | Right vertical |
| **Rörelse tillåten, hinder finns** | Movement, obstacles exist | 1 + 4 | NW diagonal (↘) |
| **Rörelse tillåten, kontrollera växlar** | Movement, check points | 2 + 3 | NE diagonal (↙) |

### Huvuddvärgsignal (Main Dwarf Signal)

Extension of the shunting dwarf, adding:
- **One red lamp** on the extended NW axis (above-left of lamp 1) - train stop indicator
- **Two green lamps** below the white lamps (under lamps 3 and 4) - train proceed

```
  Lamp Layout:
       [R]              R = Red (train stop)
     [1] [2]            1-4 = White/Yellow (shunting patterns)
     [3] [4]
    [G1] [G2]           G1, G2 = Green (train proceed)
```

#### Shunting Aspects (Red lit = train stop, shunting permitted)

When red is lit, white lamp patterns work as shunting dwarf (same four aspects). Red means stop for trains but not necessarily for shunting.

#### Train Aspects (Green + White 2+4 vertical)

| Swedish Name | English | G1 | G2 | Meaning |
|--------------|---------|----|----|---------|
| **Kör** | Drive 80 | off | ● | Proceed at 80 km/h* |
| **Kör 40** | Drive 40 | ● | off | Proceed at 40 km/h |
| **Kör 40, vänta kör 40 eller stopp** | Drive 40, expect 40/stop | off | ◐ | 40 km/h, next signal 40 or stop |
| **Kör 40, vänta stopp** | Drive 40, expect stop | ◐ | off | 40 km/h, next signal stop |

> Legend: ● = steady, ◐ = blinking. *See ATC Note.

```
  Train aspects (all include white 2+4 vertical):
  Kör           Kör 40        Vänta 40/stopp  Vänta stopp
     ○             ○             ○               ○
   ○ ●           ○ ●           ○ ●             ○ ●
   ○ ●           ○ ●           ○ ●             ○ ●
   ○ ●           ● ○           ○ ◐             ◐ ○
```

### Other Signal Types

**Vägskyddssignal (Level Crossing)**: Distant signal (triangular, amber) warns of crossing; main V-signal shows white (secured) or red (stop).

**Stopplykta (Stop Light)**: Fixed red light at buffer stops, derails, gates.

**Brosignal (Bridge Signal)**: Controls passage at movable bridges.

**A-signal (Departure Signal)**: Letter/arrow displays for departure clearance (A, T, L, K).

## Lamp Colours

| Colour | Swedish | Usage |
|--------|---------|-------|
| Red | Röd | Stop aspects |
| Green | Grön | Proceed aspects (speed) |
| White/Yellow | Vit/Gul | Shunting aspects |
| Amber | Gul | Level crossing warnings |

---

## Munkeröd Station Configuration

Munkeröd uses the following signal types:

| Location | Signal Type | Description |
|----------|-------------|-------------|
| Line entries | 5-lamp combined signal | Entry signals (infartssignaler) |
| Line exits | 2-lamp main signal | Exit signals (utfartssignaler) |
| Throughout yard | Shunting dwarf | 4-lamp växlingsdvärgsignal |
| Key junctions | Main dwarf | 7-lamp huvuddvärgsignal |

### Exit Signal (2-lamp)

Simple main signal: Green (top) = Kör, Red (bottom) = Stopp.

```
  ┌───┐
  │ G │  Kör (clear to next signal)
  ├───┤
  │ R │  Stopp
  └───┘
```

### Entry Signal (5-lamp Combined)

Combined main and distant signal with lamps (top to bottom): **Green, Red, Green, White, Green**.

```
  Lamp Layout:          Lamp Patterns (●=steady, ◐=blink):
  ┌───┐
  │ G │ ← G1 (main)     ○ ● ○ ○ ○  Stopp
  ├───┤                 ● ○ ● ○ ○  Kör 40
  │ R │ ← Red           ● ○ ◐ ○ ○  Kör 80, vänta stopp
  ├───┤                 ● ○ ● ○ ●  Kör 40, kort väg
  │ G │ ← G2 (distant)  ● ○ ◐ ○ ◐  Kör 80, vänta kör 40
  ├───┤                 ● ○ ○ ◐ ○  Kör 80, vänta kör 80
  │ W │ ← White
  ├───┤
  │ G │ ← G3
  └───┘
```

---

## Implementation

### Signal Types and Aspects

```csharp
public enum SignalType
{
    Huvudsignal,            // Main signal (2-3 lamps)
    Försignal,              // Distant signal
    KombiSignal,            // Combined main + distant (5 lamps)
    Växlingsdvärgsignal,    // Shunting dwarf (4 white lamps)
    Huvuddvärgsignal,       // Main dwarf (7 lamps: 1R + 4W + 2G)
    Vägskyddssignal         // Level crossing signal
}

/// <summary>
/// All Swedish signal aspects. Not all aspects apply to all signal types.
/// </summary>
public enum SignalAspect
{
    // Stop
    Stopp,

    // Main signal / train aspects (see ATC Note for 80 km/h)
    Kör,                    // Drive 80
    Kör40,                  // Drive 40
    Kör40KortVäg,           // Drive 40, short route

    // Combined signal aspects (main + distant preview)
    Kör80VäntaStopp,        // Drive 80, expect stop
    Kör80VäntaKör80,        // Drive 80, expect 80
    Kör80VäntaKör40,        // Drive 80, expect 40

    // Distant signal aspects
    VäntaStopp,             // Expect stop
    VäntaKör80,             // Expect 80
    VäntaKör40,             // Expect 40

    // Main dwarf train aspects
    Kör40VäntaKör40Stopp,   // Drive 40, expect 40 or stop
    Kör40VäntaStopp,        // Drive 40, expect stop

    // Shunting aspects (dwarf signals)
    RörelseTillåten,                    // Movement allowed
    RörelseTillåtenHinderFinns,         // Movement allowed, obstacles
    RörelseTillåtenKontrolleraVäxlar    // Movement allowed, check points
}
```

### Signal Definition

```csharp
public record SignalDefinition(
    int Id,
    SignalType Type,
    string Coordinate,          // Grid position
    int Orientation,            // Degrees (0, 90, 180, 270)
    int[] LocoNetAddresses);
```

### Möllehem Signal Decoder (MGP SV10)

Möllehem (MGP) produces LocoNet signal decoders for Swedish signals:

- **Capacity**: Up to 10 signals, max 7 lamps each (64 total)
- **Features**: Smooth lamp fade transitions, LED support via LED3/LED12 boards
- **Power**: External 5V DC or LocoNet power
- **Supported**: Main signals, dwarf signals, level crossing signals

---

## References

- [Swedish railway signalling - Wikipedia](https://en.wikipedia.org/wiki/Swedish_railway_signalling)
- [jarnvag.net - Swedish railway signals guide](https://www.jarnvag.net/banguide/signaler)
- [Möllehem Signal Decoder](http://www.mollehem.se/index.php/en/signals/electronic/signaldecoder-sv10-detail)

## Notes

1. The last semaphore signals on Swedish railways were replaced in 1999.
2. Swedish signalling is not compatible with Norwegian/Danish due to different green light meanings.
