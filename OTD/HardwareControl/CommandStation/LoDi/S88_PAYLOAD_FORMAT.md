# S88 EVT Payload-Format

Diese Notiz dokumentiert das implementierte LoDi-S88-EVT-Parsing in `S88EventPayloadParser`.

## Heartbeat

- Format: `[0x00]`
- Bedeutung: Keine Zustandsaenderung
- Verhalten: Kann ueber `SuppressHeartbeatDiagnostics` unterdrueckt werden

## Aenderungsereignis (EVT)

- Format: `[Count][Moduladresse][Kontakt][State]...`
- Pro Aenderung genau 3 Bytes
- Erwartete Laenge: `1 + Count * 3`

### Feldbedeutung

- `Count`: Anzahl Aenderungen
- `Moduladresse`: S88-Moduladresse
- `Kontakt`: Kontakt-/Inputnummer
- `State`: `0x00 = frei`, `!= 0x00 = besetzt`

## Parse-Reihenfolge (wichtig!)

1. **Heartbeat**: `count == 0 && length == 1` → kein Event
2. **ContactChanges**: `length >= 1 + count * 3 && count > 0` → Sensor-Events
3. **Fehler (zu kurz)**: `length < 1 + count * 3` → `TryParse` gibt `false` zurück, Fehler im Diagnose-Log
4. **ModuleOverview**: `length >= 5` (nur wenn nicht zu kurz!) → Diagnose, kein Event

> ⚠️ **Achtung**: Der ModuleOverview-Fallback darf NICHT vor dem Längenfehler-Check stehen.
> Andernfalls werden EVT-Pakete mit `count > 1` und unerwarteter Länge lautlos als ModuleOverview
> klassifiziert – alle betroffenen Sensoren bleiben stumm (Bug: "nur erste paar Sensoren melden zurück").

## Diagnoselog (LoDiS88Commander)

Bei echten Aenderungen (`Payload > 1`) wird strukturiert ausgegeben:

- Anzahl (`Count`)
- betroffene Moduladressen (`Module=[...]`)
- Typen aggregiert (`Typen=[BESETZT:n, FREI:m]`)
- optionale Restbytes (`TrailingBytes`)
- Einzelzeilen pro Aenderung

