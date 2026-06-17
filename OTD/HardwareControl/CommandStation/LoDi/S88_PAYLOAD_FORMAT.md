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

## Diagnoselog (LoDiS88Commander)

Bei echten Aenderungen (`Payload > 1`) wird strukturiert ausgegeben:

- Anzahl (`Count`)
- betroffene Moduladressen (`Module=[...]`)
- Typen aggregiert (`Typen=[BESETZT:n, FREI:m]`)
- optionale Restbytes (`TrailingBytes`)
- Einzelzeilen pro Aenderung

