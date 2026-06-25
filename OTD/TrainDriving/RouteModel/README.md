# RouteModel

Schlankes Routenmodell fuer `TrainDriving` mit **Start-Waypoint-Permission** pro Route.

## Ziel

Jede Route ist ein Abschnitt von `FromWaypointId` nach `ToWaypointId` mit Distanz und
Fahrerlaubnis am Start. Es gibt keine separate route-inherente Cruise-Geschwindigkeit.

## Kernobjekte

- `RouteEntry`: Abschnitt mit `DistanceCm` und `StartRoutePermission`
- `RoutePermission`: `ProceedAllowed` + optionale `MaxSpeedKmh`
- `RouteCycle`: aktueller Abschnitt zwischen zwei Waypoints inkl. `AllowedSpeedKmh`
- `RouteRuntime`: Positionsintegration, Sensor-Rekalibrierung, Effective-Speed

Wichtig: Der letzte Waypoint wird automatisch als **Halt** verankert (`RoutePermission.Stop()`),
damit ohne Folgeroute fail-safe `0 km/h` gilt.

## Schnellstart

```csharp
using OTD.TrainDriving.RouteModel;

var table = new RouteTableBuilder()
    .AddRoute(
        id: 1,
        fromWaypointId: "S1",
        toWaypointId: "S2",
        distanceCm: 120,
        startRoutePermission: RoutePermission.Proceed(maxSpeedKmh: 40, aspect: "Fahrt"))
    .AddRoute(
        id: 2,
        fromWaypointId: "S2",
        toWaypointId: "S3",
        distanceCm: 180,
        startRoutePermission: RoutePermission.Stop())
    .AddSensorMarker(routeId: 1, sensorId: 1, offsetCm: 30)
    .AddActionEvent("HX_BUE_01", RouteActionType.WarnHorn, positionCm: 150)
    .Build();

var runtime = new RouteRuntime(table);
var tick = runtime.ApplyStep(deltaCm: 80, trajectorySpeedKmh: 70, activatedSensorId: 1);
var effectiveSpeed = tick.EffectiveSpeedKmh;
```

