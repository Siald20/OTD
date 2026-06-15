# TrainControl Dokumentation

Diese Datei beschreibt den aktuellen Stand des Namespaces `OTD.HardwareControl.Train`.

## Ziel und Scope

`OTD.HardwareControl.Train` kapselt die Zuglogik fuer:

- Aufbau und Verwaltung einer Zugkomposition (`Train`, `TrainComposition`, `TrainCompositionBuilder`)
- Einheitliche Ansteuerung von Lokomotiven und Wagen ueber `IVehicle`
- Decodernahe Funktionen (Richtung, Geschwindigkeit, Funktionen, Rueckmeldungen) ueber `ILocoDecoder` und `LocoDecoder`
- Headlight-/Kupplungslogik auf Train-Ebene

## Architekturueberblick

```
Train
 └─ TrainComposition  (immutable, 1..n TrainVehicle)
     └─ IVehicle      (Loco | Car)
         └─ LocoDecoder   (Command-Station-Bindung, Decoderbefehle)
```

- `Train`
  - Oberste Orchestrierung eines Zuges
  - Laedt Konfiguration, baut Komposition, setzt Betriebsmodus, Richtung, Geschwindigkeit, Funktionen
- `IVehicle`
  - Gemeinsamer Vertrag fuer Lok und Wagen
  - Fuer Richtungssetzung: `SetDirectionAsync(TrainDirection, VehicleOrientation, ...)`
  - Physische/skalierte Werte: `HasDecoder`, `Length`, `VMin`, `VMax`, `Weight`, `Direction`
- `ILocoDecoder`
  - Decoderfaehigkeiten fuer Train-Logik (Zugriff auf `LocoDecoder`-Instanz)
- `Loco`
  - Decoder ist Pflicht
  - Verarbeitet Geschwindigkeit via SpeedTable und VMax-Guards
  - Zusaetzlich: `SetSpeedVAsync(int speed, ...)` — nur auf `Loco`, nicht Teil von `IVehicle`
- `Car`
  - Decoder ist optional
  - Empfaengt nur Richtungsbefehle (SpeedStep immer 0)
  - Kein `SetSpeedVAsync`
- `LocoDecoder`
  - Niedrige Ebene fuer Command-Station-Bindung und Decoderbefehle
- Utilities
  - `TrainUtils`, `LocoDecoderUtils`, `HeadlightUtils`, `AutoCouplingUtils`, `TrainCompositionUtils`

## Trennung von Richtungs- und Geschwindigkeitssteuerung

Ein zentrales Designprinzip ist die klare Trennung zwischen Richtungs- und Geschwindigkeitsbefehlen:

1. **Richtung setzen** (`SetDirectionAsync`) — setzt die Decoder-Fahrtrichtung und haelt die Lok an (SpeedStep 0).
   Wird ueber `IVehicle` an alle Fahrzeuge (Loko und Wagen) geschickt.

2. **Geschwindigkeit setzen** (`SetSpeedVAsync`) — setzt die Fahrstufe anhand der konfigurierten SpeedTable.
   Nur auf `Loco`, da Wagen keine Eigentraktion haben.

`Train` garantiert, dass `SetDirectionAsync` immer vor dem ersten Fahrbefehl gesendet wird
(beim Einstellen der Betriebsmodi `Shunting` und `Travelling` sowie bei Richtungswechseln).

## Kerninterfaces

### `ILocoDecoder`

Datei: `OTD/HardwareControl/Train/ILocoDecoder.cs`

- `LocoDecoder? LocoDecoder { get; }`
  - Direkter Zugriff auf die konkrete Decoder-Instanz
  - Kann `null` sein bei Fahrzeugen ohne Decoder

### `IVehicle`

Datei: `OTD/HardwareControl/Train/IVehicle.cs`

Erweitert `ILocoDecoder` und fuegt fahrzeugbezogene Aspekte hinzu:

- Identitaet und Konfiguration:
   - `Guid VehicleId`
   - `XElement? VehicleConfig`
- Decoder-Zustand:
   - `bool HasDecoder`
   - `VehicleDirection Direction`
- Physische/skalierte Werte:
   - `Length`, `VMin`, `VMax`, `Weight`
- Richtungssteuerung (alle Fahrzeuge):
   - `SetDirectionAsync(TrainDirection, VehicleOrientation, bool forceSend, CancellationToken)`
     Setzt die Decoder-Fahrtrichtung und SpeedStep 0. Muss vor Fahrbefehlen aufgerufen werden.

## Fahrzeuge

### `Loco`

Datei: `OTD/HardwareControl/Train/Loco.cs`

- Decoder ist Pflicht (`_locoDecoder` nicht-null)
- `SetDirectionAsync(...)`: Setzt Decoder-Richtung, haelt an (SpeedStep 0)
- `SetSpeedVAsync(int speed, ...)`: Setzt Geschwindigkeit anhand SpeedTable; nutzt bereits gesetzte `_locoDecoder.Direction`
  - VMax-Guard: Befehle ueber VMax werden unterdrueckt
  - Duplikat-Unterdrueckung: Gleiches Speed/Richtungs-Paar wird nicht erneut gesendet
- `LocoDecoder`-Property liefert immer eine Instanz

### `Car`

Datei: `OTD/HardwareControl/Train/Car.cs`

- Decoder ist optional (`LocoDecoder? _locoDecoder`)
- `SetDirectionAsync(...)`: Setzt Decoder-Richtung mit SpeedStep 0; No-Op ohne Decoder
- Kein `SetSpeedVAsync` — Wagen sind nicht angetrieben

## LocoDecoder-Bindung an CommandStation

Datei: `OTD/HardwareControl/Train/LocoDecoder.cs`

- `SubscribeCommandStationAsync(CommandStation)`
- `UnsubscribeCommandStationAsync(CommandStation?)`
- Sync-Wrapper: `UnsubscribeCommandStation(CommandStation?)` (ruft intern async auf)

Verwendung in `Train`/`TrainCompositionBuilder` wird mit `.GetAwaiter().GetResult()` synchronisiert,
um die bestehende synchrone Aufbau-/Abbau-Logik kompatibel zu halten.

## Konfigurationsregeln (Decoder)

Im Konstruktor sind diese XML-Werte Pflicht:

- `<protocol>`
- `<speedsteps>`
- `<address>`

Fehlen sie oder sind sie leer, wird `InvalidOperationException` geworfen.

Optional:

- `<functiontable>` — fehlt die Tabelle, wird mit leerer Liste gearbeitet

## `Train` Lebenszyklus

Datei: `OTD/HardwareControl/Train/Train.cs`

### Konstruktion

- `Train(Guid trainId, CommandStation commandStation)`

### `LoadComposition()`

- Nur in `TrainOperatingMode.ShutDown` erlaubt
- Liest `trains.xml`, erzeugt Fahrzeuginstanzen
- Bindet deren Decoder an die CommandStation
- Berechnet `Length`, `VMin`, `VMax`, `Weight`

### Betriebssteuerung

| Methode | Beschreibung |
|---|---|
| `SetOperatingModeAsync(mode)` | Wechselt Betriebsmodus; bei `Shunting`/`Travelling` wird zuerst `SetDirectionAsync` an alle Fahrzeuge gesendet |
| `SetTrainDirectionAsync(direction)` | Setzt Fahrtrichtung; sendet `SetDirectionAsync` an alle Fahrzeuge |
| `SetSpeedVAsync(speed)` | Sendet `SetSpeedVAsync` an alle Lokomotiven der Komposition |
| `EmergencyStopAsync()` | Notbremsung aller Lokomotiven |
| `SetFunctionStateAsync(...)` | Funktionssteuerung |
| `ActivateFunctionAsync(...)` | Zeitgesteuerte Funktionsaktivierung |

### Interner Ablauf bei Fahrtbefehl

```
SetOperatingModeAsync(Shunting/Travelling)
  └─ SendDirectionAsync()           → IVehicle.SetDirectionAsync (alle Fahrzeuge)

SetSpeedVAsync(speed)
  └─ SendSpeedCommandToAllAsync()   → Loco.SetSpeedVAsync (nur Lokomotiven)
```

## Wichtige Enums

Datei: `OTD/HardwareControl/Train/TrainEnums.cs`

- `TrainOperatingMode`: `ShutDown`, `Parking`, `Shunting`, `Travelling`
- `TrainDirection`: `A`, `B`
- `VehicleOrientation`: `Normal`, `Reverse`
- `VehicleDirection`: `Undefined`, `Forward`, `Backward`
- `FunctionState`: `Undefined`, `Off`, `On`
- `HeadlightMode`: `Off`, `On`, `Auto`

## Threading und Async-Konventionen

- Decoder-seitige Sendeoperationen sind asynchron und werden ueber `_commandLock` serialisiert
- Train-Ebene verwendet teils synchrone Wrapper auf async (`GetAwaiter().GetResult()`), um bestehende API konsistent zu halten
- Rueckmeldungen der Zentrale laufen ueber `CommandStation.RegisterDecoder(...)` und `LocoDecoder.StateChanged`

## Designentscheidungen

- `Subscribe/Unsubscribe` sind nicht Teil von `ILocoDecoder`/`IVehicle`; die konkrete `LocoDecoder`-Instanz ist ueber `ILocoDecoder.LocoDecoder` erreichbar
- `SetSpeedVAsync` ist bewusst nicht Teil von `IVehicle`, da nur Lokomotiven eine SpeedTable besitzen
- `VehicleOrientation` ist Kompositions-Kontext (`TrainVehicle`), kein intrinsisches Fahrzeug-Attribut
- `Train` garantiert immer: Richtung setzen vor Fahrbefehl

## Bekannte offene Punkte (aus Code-TODOs)

- Bewertung, ob Betrieb ohne konfigurierte Funktionen in allen Faellen sinnvoll ist
- Pruefen, ob bestimmte sync Wrapper weiter auf async umgestellt werden sollen

## Schnellreferenz: Typische Nutzung

```csharp
var train = new Train(trainId, commandStation);

// Betriebsmodus setzen (sendet intern SetDirectionAsync an alle Fahrzeuge)
await train.SetOperatingModeAsync(TrainOperatingMode.Shunting);

// Fahrtrichtung wechseln
await train.SetTrainDirectionAsync(TrainDirection.A);

// Geschwindigkeit setzen (nur Lokomotiven)
await train.SetSpeedVAsync(20);

// Funktionssteuerung
await train.SetFunctionStateAsync(0, FunctionState.On);
await train.ActivateFunctionAsync(5, 500);
```

Direkter LocoDecoder-Zugriff (z.B. fuer Diagnose):

```csharp
var vehicle = train.TrainComposition[0].VehicleInstance;
if (vehicle?.LocoDecoder is { } decoder)
{
    await decoder.SubscribeCommandStationAsync(commandStation);
    await decoder.UnsubscribeCommandStationAsync(commandStation);
}
```
