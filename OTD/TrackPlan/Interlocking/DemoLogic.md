# Demo Interlocking Logic

Die Demo-Logik ist absichtlich ueber `TrackSymbol.Properties` steuerbar. Ohne gesetzte Demo-Properties bleibt das normale Verhalten erhalten.

In der UI sind diese Werte im Editor ueber Rechtsklick auf ein Element und `Einstellungen` erreichbar.

Gemeinsam fuer alle Elemente:

- `Demo.Blocked=true`: sperrt das Element fuer das Stellen einer Fahrstrasse.

Element-spezifische Beispiele:

- Track: `Demo.Maintenance=true`
- TrackBlock: `Demo.ReserveOnly=true`
- Signal: `Demo.HoldRed=true`
- Switch / DoubleSlipSwitch: `Demo.LockedPosition=Straight|Diverging|Left|Right`
- Crossing: `Demo.ConflictingCrossing=true`
- Sensor: `Demo.SensorClear=false`
- Platform: `Demo.PassengerStopOnly=true`
- LevelCrossing: `Demo.Closed=false`
- TunnelPortal: `Demo.TunnelClear=false`
- Bridge: `Demo.BridgeReleased=false`
- Uncoupler: `Demo.Lowered=false`
- Depot: `Demo.DepotExitReleased=false`
- Turntable: `Demo.Aligned=false`
- TextLabel: `Demo.OperationalLabel=true`

Die Klassen liegen einzeln unter `Elements/` und koennen pro Elementtyp erweitert oder durch ein Stellwerksprofil wie `Domino67InterlockingProfile` ersetzt werden.

## Domino 67

Das Profil `Domino67InterlockingProfile` ersetzt aktuell die Logik fuer Signal, Block, Weiche, DKW und Bahnuebergang.

UI: Rechtsklick auf Element -> `Einstellungen` -> `Domino 67`.

Unterstuetzte Properties:

- `Domino67.Blocked=true`: Element sperrt die Fahrstrasse.
- `Domino67.HoldRed=true`: Startsignal bleibt auf Halt.
- `Domino67.FlankProtectionSymbols=B1,W2`: Symbol-IDs fuer Flankenschutz; wenn eines belegt ist, wird nicht gestellt.
- `Domino67.OverlapSymbols=B3,B4`: Symbol-IDs fuer Durchrutschweg; wenn eines belegt ist, wird nicht gestellt.
- `Domino67.LocalControl=true`: Weiche/DKW ist im Ortsbetrieb und kann nicht gestellt werden.
- `Domino67.Locked=true`: Weiche/DKW ist verschlossen; Umstellen in eine andere Lage wird abgelehnt.
- `Domino67.FlankProtectionEnabled=true`: Weiche/DKW darf automatisch als Schutzweiche verwendet werden.
- `Domino67.RequiredPosition=Straight|Diverging|Left|Right`: Weiche muss diese Lage behalten.
- `Domino67.Closed=true`: Bahnuebergang ist geschlossen.
- `Domino67.AutoClose=true`: Bahnuebergang darf durch die Fahrstrasse automatisch geschlossen werden.

Automatischer Flankenschutz:

- Beim Stellen sucht `Domino67InterlockingProfile` seitlich an die Fahrstrasse angeschlossene Weichen/DKW.
- Beruecksichtigt werden nur Weichen/DKW mit `Domino67.FlankProtectionEnabled=true`.
- Wenn eine eindeutige Schutzlage aus den vorhandenen `SwitchRouteOptions` ableitbar ist, wird diese Weiche automatisch in Schutzlage gestellt und verriegelt.
- Wenn an einem Anschluss keine Schutzlage moeglich ist, wird keine Fantasie-Lage gesetzt; die Weiche bleibt unveraendert.
