# Signalling Configuration Plan

Master configuration approach for Munkeröd station signalling system.

## The Problem: Duplicated Logic

Currently, signalling rules exist in two places:

| Location | What it controls |
|----------|------------------|
| **Möllehem Signal10 decoder** | Which aspect to display based on configured GO rules, diverging switches, etc. |
| **YardController.App** | Train route validation, point locking, conflict detection |

This creates risk of inconsistencies and maintenance burden.

## The Solution: Single Source of Truth

One master configuration table that generates:
1. **Möllehem decoder SV/LNCV values** via configuration tool
2. **YardController.App data files** (TrainRoutes.txt, Points.txt)
3. **Documentation** for reference

---

## Master Configuration Structure

### 1. Signal Definitions Table

| Column | Description | Example |
|--------|-------------|---------|
| Signal Number | Unique ID | 21 |
| Signal Type | HSI2, HSI3, HSI4, HSI5, DVSI, HVDv | HSI5 |
| Direction | Odd (udda) / Even (jämn) | Odd |
| Decoder Address | Base address of decoder | 100 |
| Slot | Signal slot 1-10 on decoder | 1 |
| First LED | LED position on serial bus | 1 |
| Next Signal | Address for försignalering | 31 |
| Notes | Location description | West entry |

### 2. Point Definitions Table

| Column | Description | Example |
|--------|-------------|---------|
| Point Number | Logical ID | 1 |
| LocoNet Addresses | Physical addresses | 840, 843 |
| Lock Address Offset | Offset for lock commands | 1000 |
| Paired With | Other point in pair | - |
| Location | Description | West throat |

### 3. Occupancy Blocks Table

| Column | Description | Example |
|--------|-------------|---------|
| Block ID | Unique identifier | T100 |
| LocoNet Address | Detection address | 500 |
| Location | Description | Track 1 station |
| Signals Affected | Signals that use this in GO rules | 21, 31 |

### 4. Train Routes Master Table (The Core)

Wide table with one row per train route:

| Column Group | Columns | Purpose |
|--------------|---------|---------|
| **Route ID** | From Signal, To Signal, Via Signals | Route identification |
| **Points Required** | P1+, P1-, P2+, P2-, ... P35+, P35- | Point positions for this route |
| **Occupancy Must Be Free** | T100, T101, ... | Blocks that must be free |
| **Entry Signal Aspect** | Kör80, Kör40, Kör40KortVäg | What entry signal shows |
| **Conflicting Routes** | List of route IDs | Routes that cannot be set simultaneously |
| **Next Signal for Försignal** | Signal number | For HSI4/HSI5 entry signals |
| **Diverging for Speed** | Point numbers | Which points cause Kör40 |
| **Notes** | Free text | Special conditions |

---

## Mapping to Möllehem Signal10 Configuration

### Signal Definition → Decoder SVs

| Master Config | Signal10 SV | Notes |
|---------------|-------------|-------|
| Signal Type | SV 100 bits 0-4 | Type value (1-16) |
| First LED | SV 101 bits 0-5 | LED position 1-64 |
| Next Signal | SV 103 | Address for försignalering |
| Diverging Points | SV 105, 107, 109 | Addresses of motväxlar |
| GO Rules | SV 111-128 | Conditions for Kör |

### Train Route → Signal GO Rules

For each entry signal, derive GO rules from the master table:

```
Train Route: 21-31
Points: 1+, 3+, 7+
Occupancy: T100 (block behind 31)

Signal 21 GO Rules:
  GO1: V1/Closed AND      (point 1 straight)
  GO2: V3/Closed AND      (point 3 straight)
  GO3: V7/Closed AND      (point 7 straight)
  GO4: T100/Free          (destination block free)
```

### Diverging → Kör40 Aspect

```
Train Route: 21-33
Points: 1+, 3-, 11-
Diverging: 3 (avvikande)

Signal 21:
  Diverging Sw 1 = address of point 3
  When point 3 is Thrown → Signal shows Kör 40 instead of Kör 80
```

---

## Mapping to YardController.App

### TrainRoutes.txt Generation

```
Master Table Row:
  From: 21, To: 31, Points: 1+, 3+, 7+

Generated:
  21-31:1+,3+,7+
```

### Points.txt Generation

```
Master Point Definition:
  Number: 1, Addresses: 840, 843, LockOffset: 1000

Generated:
  LockOffset:1000
  1:840,843
```

### Conflict Detection

From master table "Conflicting Routes" column:
- Used by TrainRouteLockings for CanReserveLocksFor()
- Prevents simultaneous reservation of conflicting routes

---

## Configuration Tool Architecture

```
┌─────────────────────────────────────────────────────────┐
│                 Master Configuration                     │
│              (Excel/CSV/JSON/Database)                   │
└─────────────────────┬───────────────────────────────────┘
                      │
        ┌─────────────┴─────────────┐
        │     Configuration Tool     │
        │         (C# Console)       │
        └─────────────┬─────────────┘
                      │
    ┌─────────────────┼─────────────────┐
    │                 │                 │
    ▼                 ▼                 ▼
┌──────────┐   ┌────────────┐   ┌─────────────┐
│ Signal10 │   │ YardController│  │ Documentation│
│ LNCV     │   │ Data Files  │   │ (Markdown)   │
│ Commands │   │ (txt/json)  │   │              │
└──────────┘   └────────────┘   └─────────────┘
```

### Tool Capabilities

1. **Validate** master configuration for consistency
2. **Generate** YardController data files
3. **Generate** LNCV commands for Möllehem decoders
4. **Send** LNCV commands via LocoNet (optional)
5. **Export** documentation

### LNCV Communication

Möllehem decoders use LNCV (LocoNet Configuration Variables) protocol:
- Similar to CV programming but for accessories
- Can be written via LocoNet messages
- Tool can generate command sequence or execute directly

---

## Implementation Steps

### Phase 1: Create Master Configuration

1. **Extract existing data** from TrainRoutes.txt and Points.txt
2. **Add signal definitions** (types, positions, LED assignments)
3. **Add occupancy blocks** and their addresses
4. **Derive GO rules** for each signal from route analysis
5. **Identify conflicts** between routes

### Phase 2: Build Configuration Tool

1. **Parse** master configuration (suggest JSON format)
2. **Validate** for completeness and consistency
3. **Generate** YardController data files
4. **Generate** LNCV commands for Signal10 decoders

### Phase 3: LNCV Transmission

1. **Add LocoNet LNCV support** to tool
2. **Implement write sequence** for decoder configuration
3. **Verify** written values by reading back

---

## Master Configuration Format Options

### Option A: Excel Spreadsheet
**Pros:** Easy to edit, visual, formulas for validation
**Cons:** Version control difficult, parsing required

### Option B: JSON Files
**Pros:** Version control friendly, easy to parse, typed
**Cons:** Harder to edit manually for large tables

### Option C: Structured Text (like current format)
**Pros:** Simple, works with existing parsers
**Cons:** Limited expressiveness for complex rules

### Recommendation: JSON with Excel for Editing

- **Primary storage:** JSON files (version controlled)
- **Editing:** Excel workbook with export-to-JSON macro
- **Validation:** Tool validates JSON structure and consistency

---

## Example Master Configuration (JSON)

```json
{
  "signals": [
    {
      "number": 21,
      "type": "HSI5",
      "direction": "odd",
      "decoderAddress": 100,
      "slot": 1,
      "firstLed": 1,
      "nextSignal": 31,
      "location": "West entry from line"
    }
  ],

  "points": [
    {
      "number": 1,
      "addresses": [840, 843],
      "lockAddressOffset": 1000,
      "location": "West throat"
    }
  ],

  "occupancyBlocks": [
    {
      "id": "T100",
      "address": 500,
      "location": "Track 1 station"
    }
  ],

  "trainRoutes": [
    {
      "from": 21,
      "to": 31,
      "via": [],
      "points": {
        "1": "straight",
        "3": "straight",
        "7": "straight"
      },
      "occupancyMustBeFree": ["T100"],
      "entryAspect": "Kör80",
      "divergingPoints": [],
      "conflictingRoutes": ["21-33", "51-31"],
      "notes": "Main route to track 1"
    },
    {
      "from": 21,
      "to": 33,
      "via": [],
      "points": {
        "1": "straight",
        "3": "diverging",
        "11": "diverging"
      },
      "occupancyMustBeFree": ["T101"],
      "entryAspect": "Kör40",
      "divergingPoints": [3],
      "conflictingRoutes": ["21-31", "51-33"],
      "notes": "Route to track 2, slow due to point 3"
    }
  ],

  "signalGoRules": [
    {
      "signal": 21,
      "rules": [
        {
          "route": "21-31",
          "conditions": [
            { "type": "switch", "address": 840, "state": "closed" },
            { "type": "switch", "address": 842, "state": "closed" },
            { "type": "switch", "address": 835, "state": "closed" },
            { "type": "occupancy", "address": 500, "state": "free" }
          ]
        }
      ]
    }
  ]
}
```

---

## Next Steps

1. **Review this plan** - does this capture your needs?
2. **Start with signals** - list all signals with their types and positions
3. **Map occupancy blocks** - what detection exists and where?
4. **Derive GO rules** - analyze which routes affect which signals
5. **Build tool incrementally** - start with validation, then generation

---

## Questions to Resolve

1. **What occupancy detection exists?** Need addresses and locations
2. **Signal decoder assignment** - which signals on which decoder?
3. **Conflict rules** - are there conflicts beyond point sharing?
4. **Försignalering** - which signals försignalerar which?
5. **Dwarf signals** - locations and control addresses?
