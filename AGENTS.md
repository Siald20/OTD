# AGENTS.md - AI Coding Agent Guidelines for OTD

OTD is a **model train (DCC) control system** built with C# (.NET 10.0) and Avalonia UI. It manages complex train compositions, hardware command stations, and interlocking logic.

## Architecture Overview

```
OTD (Avalonia Desktop App)
├── HardwareControl/          # Core domain - train control & hardware abstraction
│   ├── Train/               # Train compositions, vehicles (Loco/Car), decoder control
│   ├── CommandStation/      # Abstract interface (ICommandStation) for DCC centers
│   ├── Accessory/           # Zubehör decoder management
│   └── Feedback/            # Sensor feedback handling
├── Interlocking/            # Relay logic (Integra/Relais) with cycle-based execution
├── Controlls/               # Avalonia UI controls (InterlockingElements: signals, tracks, buttons)
└── AppData/                 # XML configs (train.xml, loco.xml, accessory.xml, etc.)
```

## Critical Design Patterns

### 1. Train/Vehicle Architecture (see `HardwareControl/Train/README.md`)

**Key distinction**: Trains contain **immutable compositions** of vehicles with **explicit separation** between direction and speed control.

```csharp
Train
  └─ TrainComposition (immutable list of TrainVehicle)
      └─ IVehicle (Loco or Car)
          └─ LocoDecoder (Command-Station binding, low-level decoder commands)
```

**Critical rule**: Direction MUST be set before sending speed commands
- `IVehicle.SetDirectionAsync()` → sets decoder direction + SpeedStep 0
- `Loco.SetSpeedVAsync()` → only on Loco, uses SpeedTable + VMax guards, duplicate suppression
- `Car` has optional decoder, receives only direction commands

**Example pattern** (from Train.cs):
```csharp
await train.SetOperatingModeAsync(TrainOperatingMode.Shunting);  // SendDirectionAsync internally
await train.SetSpeedVAsync(20);  // Then speed; duplex guards automatically applied
```

### 2. ICommandStation Abstraction

All hardware control goes through `ICommandStation` (manufacturer-independent):
- Current implementations: `LoDiRektor` (UDP-based DCC), `MockCommandStation` (testing)
- All async methods support `CancellationToken`
- Core methods: `ConnectAsync`, `SetLocoSpeedAsync`, `SetLocoFunctionAsync`, `EmergencyStopAsync`

**Adding a new command station**: Create a class implementing `ICommandStation` in `HardwareControl/CommandStation/` (no existing code modification needed).

### 3. XML Configuration as Single Source of Truth

Train/Vehicle/Decoder configs live in `OTD/AppData/` as XML:
- `train.xml`: Train compositions, vehicle references
- `loco.xml`: Decoder protocol, speedsteps, address, function tables
- `accessory.xml`: Zubehör configuration
- **Required decoder fields** (throws `InvalidOperationException` if missing):
  - `<protocol>`, `<speedsteps>`, `<address>`, `<functiontable>` (optional)

Configuration is loaded in `Train.LoadComposition()` and validated during construction.

### 4. Async/Threading Model

- All decoder operations serialize via internal `_commandLock`
- High-level Train API uses `async/await` cleanly
- Some internal layers use `GetAwaiter().GetResult()` for sync compatibility (legacy integration)
- Always pass `CancellationToken` to public async methods

## Development Workflows

### Build & Run

```bash
# Normal UI mode (starts with RelayPlanWindow or WsrTestWindow)
dotnet build
dotnet run

# Test mode via environment variable
OTD_ENTRYPOINT=TEST_HARDWARECONTROL dotnet run
```

### Key Entry Points

- **App.axaml → RelayPlanWindow**: Default UI (interlocking relay visualizer)
- **Program.cs**: Avalonia initialization + test mode router
- **Examples/TrainTest.cs**: Reference implementation for Train/Decoder setup

### Testing & Mocking

- Use `MockCommandStation` in `HardwareControl/CommandStation/Mock/` for unit tests
- All domain logic can be tested without hardware via mocks
- See `HardwareControl/Examples/` for integration examples

## Interlocking & Relay Logic

**Global cycle-based execution** (see `Interlocking/Global.cs`):
- `g_cycle_time_ms = 20` (50 cycles/sec)
- `ENABLE_DUMMY_*` flags control simulation vs. real I/O
- `Integra/` and `Relais/` contain logic layer

**NOTE**: Interlocking operates independently from Train control; coordinate via `CommandStation` interface if needed.

## UI Architecture (Avalonia)

- **Engine**: Avalonia 12.0.4 + Fluent theme
- **Main windows**: `MainWindow`, `RelayPlanWindow` (interlocking), `WsrTestWindow`
- **Controls**: `InterlockingEnlements` (SignalTiles, TrackTiles, ButtonTiles)
- **XAML files**: Use `.axaml` extension with compiled bindings (`AvaloniaUseCompiledBindingsByDefault=true`)

## Common Tasks for Agents

### Adding a New Command Station Type

1. Create `OTD/HardwareControl/CommandStation/MyNewStation.cs`
2. Implement `ICommandStation` interface (8 methods)
3. Handle connection, power, speed/function commands
4. Use in App startup by replacing `MockCommandStation` instantiation

### Extending Train Functionality

1. Modify `Train.cs` public methods (e.g., new operating mode)
2. Ensure `SetDirectionAsync` is called before speed commands
3. Test with `TrainTest.cs` in test mode
4. Update `HardwareControl/Train/README.md` if behavioral changes

### Adding Interlocking Logic

1. Extend `Interlocking/Integra/` or `Integra/Relais/`
2. Respect `g_cycle_time_ms` cycle timing
3. Signal state changes via public events/properties for UI binding

### Modifying Vehicle/Decoder Config

1. Edit XML in `OTD/AppData/` (schema defined in loader)
2. Validate required fields: `<protocol>`, `<speedsteps>`, `<address>`
3. For new fields, update `LocoDecoder` or `IVehicle` parsing logic

## Key Files & Responsibilities

| File/Folder | Responsibility |
|---|---|
| `HardwareControl/Train/Train.cs` | High-level train orchestration, composition loading, direction/speed dispatch |
| `HardwareControl/Train/IVehicle.cs`, `Loco.cs`, `Car.cs` | Vehicle abstraction, direction/speed handling per type |
| `HardwareControl/Train/LocoDecoder.cs` | Low-level decoder commands + Command-Station binding |
| `HardwareControl/CommandStation/ICommandStation.cs` | Hardware abstraction contract |
| `HardwareControl/CommandStation/LoDi/` | LoDi protocol implementatio (UDP, DCC126) |
| `Controlls/InterlockingEnlements/` | Avalonia UI tiles for signals/tracks/buttons |
| `OTD/AppData/` | XML configuration files (train, loco, accessory) |
| `Interlocking/Global.cs` | Global cycle config & simulation flags |

## Conventions & Gotchas

1. **Namespace structures**: `OTD.HardwareControl.{Domain}`, `OTD.Controlls.InterlockingEnlements.*`, top-level `OTD` for UI windows
2. **Async patterns**: Always await CommandStation and decoder operations; use `CancellationToken`
3. **Configuration immutability**: `TrainComposition` is immutable after creation; rebuild if changes needed
4. **Direction/Speed contract**: Never call `SetSpeedVAsync` without prior `SetDirectionAsync`; Train class enforces this
5. **Speed guards**: `Loco` applies VMax checks and duplicate-suppression automatically; don't bypass
6. **Test mode**: Set `OTD_ENTRYPOINT=TEST_HARDWARECONTROL` to run `TrainTest.cs` instead of UI

## Performance Considerations

- Decoder commands serialize via `_commandLock` to prevent race conditions
- SpeedTable lookup in `SetSpeedVAsync` is O(n); tables typically < 128 entries
- Cycle-based interlocking (20ms tick) is deterministic; avoid blocking in cycle loop
- XML parsing happens once at `Train.LoadComposition()`; cache configurations if reloading frequently

---

**For questions on architecture**, refer to:
- `HardwareControl/Train/README.md` - detailed Train/Vehicle/Decoder design
- `HardwareControl/CommandStation/ICommandStation_README.md` - hardware abstraction
- `HardwareControl/Examples/TrainTest.cs` - working reference code
- `Program.cs` + `Interlocking/Global.cs` - app lifecycle and global config

