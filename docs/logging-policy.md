# Logging Policy

Diese Richtlinie definiert die Zuordnung von Ereignissen zu `LogLevel` in OTD.
Debug-Logging wird namespace-basiert aktiviert.

## Grundsatz

- `Info`: nur zusammengefasste, fachlich relevante Aktionen auf API-/Use-Case-Ebene.
- `Debug`: technische Detailmeldungen unterhalb der API-Ebene (Decoder, Protokoll, Snapshot-Details).
- `Debug+`: zusaetzliche, hochfrequente oder besonders tiefe Entwicklerdiagnose. Diese Meldungen ergaenzen `Debug`, ersetzen es aber nicht.
- `Warning`: tolerierbare Auffaelligkeiten (z.B. NACK, unvollstaendige Antworten, Warm-up-Probleme).
- `Error`: Fehler mit Exception-Context oder fehlgeschlagene Operationen.

## Aktivierung

- Debug wird nicht mehr ueber feste Kategorien, sondern ueber Namespace-Praefixe aktiviert.
- Aktivierung fuer einen uebergeordneten Namespace gilt auch fuer alle Unter-Namespaces.

Beispiele:

```csharp
Logging.EnableDebugForNamespace("OTD.TrainDriving");
Logging.EnableDebugForNamespace("OTD.TrainDriving.RouteModel", extended: true);

Logging.EnableDebugFor<TrainDriving>(extended: true);
Logging.EnableDebugFor(typeof(RouteTable));
```

- `Logging.Debug(...)` und `Logging.DebugExtended(...)` koennen den Namespace automatisch vom Aufrufer ableiten.
- Bevorzugt werden im Produktivcode die direkten Aufrufe `Logging.Debug(...)`, `Logging.Info(...)` usw. aus der jeweiligen Klasse heraus.
- Fuer statische Klassen ist `Logging.Debug(typeof(StaticType), ...)` bzw. `Logging.Info(typeof(StaticType), ...)` die bevorzugte Form.

## Train-Steuerung

| Ebene | Beispiel | LogLevel |
|---|---|---|
| Train (aggregiert) | `Zug <id>: Fahrbefehl <v> km/h an <n> angetriebene Fahrzeug(e) gesendet.` | `Info` |
| Loco (pro Fahrzeug) | `Fahrbefehl: <v> km/h (SpeedStep <s>) ...` | `Debug` |
| LocoDecoder (pro Adresse) | `Fahrbefehl <dir> mit SpeedStep <s> an Adresse <a> gesendet.` | `Debug` |

## CommandStation / LoDi

| Ereignis | LogLevel |
|---|---|
| Connect warm-up erfolgreich (optional) | `Info` |
| TX/RX Diagnosepakete, Query-Details, Snapshot-Details | `Debug` |
| NACK oder recoverbare Protokollprobleme | `Warning` |
| Query-/Kommunikationsfehler mit Exception | `Error` |

## Feedback (LoDi S88)

| Ereignis | LogLevel |
|---|---|
| Modul-/Sensor-Diagnoseausgaben | `Debug` |
| Recoverbare Abweichungen im Snapshot-Warmup | `Debug` oder `Warning` (je nach Auswirkung) |
| Harte Verbindungs-/Abfragefehler | `Error` |

## Accessory

| Ebene | Beispiel | LogLevel |
|---|---|---|
| Accessory (aggregiert) | `Zubehör Turnout W2: Setze Zustand 'crossing-straight' (...)` | `Info` |
| Accessory (Bindung) | `Zubehör Turnout W2: Zentrale 'CommandStation' gebunden (...)` | `Info` |
| AccessoryDecoder (pro Adresse) | `Zubehördecoder 9: OutputValue=0 FunctionState=On gesendet (...)` | `Debug` |
| Accessory ReadBack/Auto-Off-Details | `ReadBack-State-ID ...`, `Auto-Off ...` | `Debug` |
| Accessory recoverbare Probleme | `Keine Zentrale abonniert, Befehl ignoriert.` | `Warning` |
| Accessory Konfigurations-/Ladefehler | `Fehler beim Laden ...` | `Error` |

## TrainDriving

| Ebene | Beispiel | LogLevel |
|---|---|---|
| TrainDriving Ablaufstatus | `Adaptive Modell aktiv: ...`, `Zug fährt.` | `Info` |
| Diagnostik/Intervallmodell | `Adaptive interval updated: ...` | `Debug` |
| Recoverbare Handler-Fehler | `ProgressTick handler failed: ...` | `Warning` |

## RouteModel / RouteController

| Ebene | Beispiel | LogLevel |
|---|---|---|
| `RouteTableService` API-nahe Mutationen | `ReplaceRoutes: count=...`, `ValidateAndNormalize: ...` | `Info` |
| `RouteTable` Struktur-/Payload-Aufbau | `registered A->B ...`, `Payload.Sensor ...` | `Debug` |
| `RouteController` Zyklusstart / erfolgreich verarbeitete Sensor-Ticks | `Drive cycle start ...`, `Sensor ... route tick applied ...` | `Debug` |
| `RouteRuntime` fachlich relevante Laufzeitereignisse | Permission-Eingriff, akzeptierter Sensor, ausgelöste Actions | `Debug` |
| `PositionTracker` akzeptierte/abgewiesene Rekalibrierung | `recalibration accepted/rejected ...` | `Debug` |
| Hochfrequente Tick-/Integrationsdetails | `ApplyStep ...`, `IntegrateDelta ...`, Runtime-Rebuild, ignorierte Sensoren, Payload-Reinit | `Debug+` |

## Hinweise

- Keine neuen `Console.WriteLine` im Produktivpfad verwenden; stattdessen `OTD.Common.Logging`.
- Diagnoseausgaben immer hinter Debug-Guard (`Logging.Debug(...)`) oder `Debug+` (`Logging.DebugExtended(...)`) halten.
- Nachrichten auf `Info` sollten fuer normalen Betrieb lesbar und knapp bleiben.
- `Debug+` fuer hochfrequente Schleifen, Tick-Details, Positionsintegration und aehnliche Diagnose verwenden, damit normales `Debug` lesbar bleibt.
- Fuer LoDi-Komponenten den gemeinsamen Helper `OTD/HardwareControl/CommandStation/LoDi/LoDiLog.cs` verwenden, damit Praefixe (`[LoDi TX]`, `[LoDi RX]`, `[LoDi INFO]`, `[LoDi WARN]`, `[LoDi ERROR]`, `[LoDiFeedback DEBUG]`) konsistent bleiben.
- LoDi-Warnungen/-Fehler im Schluessel-Wert-Schema formulieren: `Operation=<...> Address=<...> Reason=<...>` (optional z. B. `Transport`, `Type`, `Seq`, `Cmd`).
- Die aktuell gueltigen `Operation`-Werte und deren Bedeutung sind in `docs/lodi-log-operations.md` dokumentiert.





