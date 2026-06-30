# Route Definition Service

## Ziel

`RouteController` kann weiterhin ueber `AddRoute(...)`, `AddRoutes(...)` und `ReplaceRoutes(...)` gesteuert werden.
Neu ist ein dynamischer API-Pfad, bei dem nur veraenderliche Parameter vom Interlocking uebergeben werden.
Statische Streckendaten (Distanz, Sensorpositionen) kommen ausschliesslich aus `routelegs.xml` ueber den Route-Definition-Service.

## Neue Typen

- `OTD/TrainDriving/RouteControl/Domain/DynamicRouteRequest.cs`
- `OTD/TrainDriving/RouteControl/Domain/StopPointOverride.cs`
- `OTD/TrainDriving/RouteControl/Services/IRouteDefinitionService.cs`
- `OTD/TrainDriving/RouteControl/Services/StaticRouteLegData.cs`
- `OTD/TrainDriving/RouteControl/Services/RouteLegResolver.cs`
- `OTD/TrainDriving/RouteControl/Services/XmlRouteDefinitionService.cs`

## Integration

```csharp
var routeDefinitions = new XmlRouteDefinitionService();
var controller = new RouteController(train, routeDefinitions, initialHold: true);

controller.AddRoute(new DynamicRouteRequest(
    FromWaypointId: "B2",
    ToWaypointId: "C3",
    MaxSpeedKmh: 45));
```

## Aufloesungsregeln

- `DistanceCm`: nur statisch aus Service
- `SensorMarkers`: nur statisch aus Service
- `MaxSpeedKmh`: DynamicRouteRequest > statischer Default > Fehler
- `DriveProfile`: DynamicRouteRequest > statischer Default
- `StopPoint`: Dynamic Override auf statischen Default

## Hinweis

Die bestehende API mit `RouteLeg` bleibt unveraendert nutzbar.
Der neue dynamische Pfad ist optional und kann schrittweise im Interlocking eingefuehrt werden.

