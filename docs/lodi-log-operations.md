# LoDi Log Operations

Diese Referenz beschreibt die standardisierten `Operation`-Werte fuer LoDi-Logs im Key-Value-Format.

## Format

- Basisformat fuer Warnungen/Fehler:
  - `Operation=<...> Reason=<...>`
- Optional zusaetzliche Felder:
  - `Address=<...>`
  - `Transport=<TCP|UDP>`
  - `Type=<...>`
  - `Seq=<0x..>`
  - `Cmd=<0x..>`

Beispiel:

`Operation=QueryLocoSpeed Address=8 Reason=No response received from the LoDi device.`

## Aktuelle Operation-Werte

| Operation | Ebene | Bedeutung | Typische Zusatzfelder |
|---|---|---|---|
| `QueryDecoderState` | CommandStation/LoDi | Fehler bei der Zustandsabfrage von Decoder-Funktionen | `Address`, `Reason` |
| `QueryLocoSpeed` | CommandStation/LoDi | Fehler bei der Geschwindigkeits-/Richtungsabfrage | `Address`, `Reason` |
| `ConnectWarmup` | CommandStation/LoDi | Connect-Warmup konnte nicht erfolgreich abgeschlossen werden | `Reason` |
| `PacketReceived` | CommandStation/LoDi | Empfangene NACK-Information oder Paketbezogener Warnfall | `Type`, `Seq`, `Cmd` |
| `PacketProcessing` | CommandStation/LoDiConnection | Fehler bei der Paketverarbeitung im Transportlayer | `Transport`, `Reason` |

## Konventionen fuer neue Operation-Werte

- `Operation` in PascalCase ohne Leerzeichen, z. B. `ReadCvPom`.
- Wert beschreibt den fachlichen Verarbeitungsschritt, nicht die Exception-Klasse.
- `Reason` enthaelt die konkrete Fehlursache (kurz und parsebar).
- Bei adressbezogenen Vorgangen immer `Address=<...>` mitgeben.
- Bei Transport-/Protokollthemen nach Bedarf `Transport`, `Seq`, `Cmd`, `Type` ergaenzen.

## Auswertungshinweise

- Alerting kann stabil auf `Operation` + `Type` filtern.
- Trendanalysen sollten `Reason` aggregieren (z. B. Timeout-Haeufungen).
- `Address` eignet sich fuer Fahrzeug-/Decoder-spezifische Korrelation.

## Do / Don't

- **Do:** `Operation=QueryLocoSpeed Address=8 Reason=Timeout while waiting for ACK`
- **Don't:** `Lokspeed-Abfrage ging schief (Adresse 8)`

- **Do:** `Operation=PacketProcessing Transport=UDP Reason=Checksum mismatch`
- **Don't:** `UDP kaputt`

- **Do:** `Operation=PacketReceived Type=NACK Seq=0x12 Cmd=0xC1 Reason=Device rejected command`
- **Don't:** `NACK bei C1`

- **Do:** `Operation=ConnectWarmup Reason=No response received from the LoDi device`
- **Don't:** `Warm-up fehlgeschlagen`

## Regex / Parsing Beispiele

Allgemeines Regex fuer Key-Value-Paare (mehrfach auf eine Zeile anwendbar):

```regex
(?<key>[A-Za-z][A-Za-z0-9]*)=(?<value>"[^"]*"|[^\s]+)
```

Beispiel-Logzeile:

```text
Operation=PacketReceived Type=NACK Seq=0x12 Cmd=0xC1 Reason=DeviceRejected
```

### Loki / LogQL (Beispiele)

Warnungen mit `Operation=PacketReceived` filtern und Felder extrahieren:

```logql
{app="otd"} |= "[LoDi WARN]" |= "Operation=PacketReceived"
| regexp "Operation=(?P<Operation>\\S+) Type=(?P<Type>\\S+) Seq=(?P<Seq>\\S+) Cmd=(?P<Cmd>\\S+)"
```

Fehler nach `Operation` zaehlen (z. B. fuer Dashboard/Alert):

```logql
sum by (Operation) (
  count_over_time(
    {app="otd"} |= "[LoDi ERROR]"
    | regexp "Operation=(?P<Operation>\\S+)"
    [5m]
  )
)
```



