// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OTD.TrainDriving.RouteModel;

/// <summary>
/// In-memory route table based on waypoint-to-waypoint route entries.
/// </summary>
public sealed class RouteTable
{
    private readonly List<RouteEntry> _routes = new();
    private readonly Dictionary<int, double> _routeEntryStartById = new();
    private readonly Dictionary<int, RouteEntry> _routesById = new();

    private readonly List<RouteAnchor> _waypointAnchors = new();

    private readonly List<SensorMarker> _sensorMarkers = new();
    private readonly Dictionary<int, SensorAnchor> _sensorAnchors = new();

    private readonly List<RouteActionEvent> _actionEvents = new();

    /// <summary>
    /// Gets the ordered route entries that define this route table.
    /// </summary>
    public IReadOnlyList<RouteEntry> Routes => new ReadOnlyCollection<RouteEntry>(_routes);

    /// <summary>
    /// Gets the normalized route anchors derived from the route entries.
    /// </summary>
    public IReadOnlyList<RouteAnchor> RouteAnchors => new ReadOnlyCollection<RouteAnchor>(_waypointAnchors);

    /// <summary>
    /// Gets the registered sensor markers.
    /// </summary>
    public IReadOnlyList<SensorMarker> SensorMarkers => new ReadOnlyCollection<SensorMarker>(_sensorMarkers);

    /// <summary>
    /// Gets the sensor anchors keyed by sensor identifier.
    /// </summary>
    public IReadOnlyDictionary<int, SensorAnchor> SensorAnchors => _sensorAnchors;

    /// <summary>
    /// Gets the registered free position-based route actions.
    /// </summary>
    public IReadOnlyList<RouteActionEvent> ActionEvents => new ReadOnlyCollection<RouteActionEvent>(_actionEvents);

    /// <summary>
    /// Gets the total route length in model centimeters.
    /// </summary>
    public double TotalDistanceCm { get; private set; }

    /// <summary>
    /// Replaces all route entries and resets all subordinate markers and action events.
    /// </summary>
    /// <param name="routeEntries">Ordered route entries that define the route topology.</param>
    public void AddRoute(IReadOnlyList<RouteEntry> routeEntries)
    {
        ArgumentNullException.ThrowIfNull(routeEntries);

        _routes.Clear();
        _routeEntryStartById.Clear();
        _routesById.Clear();
        _waypointAnchors.Clear();

        _sensorMarkers.Clear();
        _sensorAnchors.Clear();

        _actionEvents.Clear();
        TotalDistanceCm = 0.0;

        if (routeEntries.Count == 0)
            return;

        var seenRouteIds = new HashSet<int>();
        var running = 0.0;

        for (var i = 0; i < routeEntries.Count; i++)
        {
            var routeEntry = routeEntries[i];

            ValidateRouteEntry(routeEntry);
            if (!seenRouteIds.Add(routeEntry.Id))
                throw new InvalidOperationException($"Duplicate route entry id '{routeEntry.Id}'.");

            if (i > 0)
            {
                var previous = routeEntries[i - 1];
                if (!string.Equals(previous.ToWaypointId, routeEntry.FromWaypointId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Route entry '{routeEntry.Id}' starts at '{routeEntry.FromWaypointId}' but previous entry ends at '{previous.ToWaypointId}'.");
                }
            }

            _routeEntryStartById[routeEntry.Id] = running;
            _routesById[routeEntry.Id] = routeEntry;
            _routes.Add(routeEntry with
            {
                FromWaypointId = routeEntry.FromWaypointId.Trim(),
                ToWaypointId = routeEntry.ToWaypointId.Trim()
            });

            _waypointAnchors.Add(new RouteAnchor(
                WaypointId: routeEntry.FromWaypointId.Trim(),
                S_Cm: running,
                RouteId: routeEntry.Id,
                Permission: routeEntry.StartRoutePermission,
                DriveProfile: routeEntry.DriveProfile,
                Block: routeEntry.Block));

            running += routeEntry.DistanceCm;
        }

        var lastEntry = routeEntries[^1];
        _waypointAnchors.Add(new RouteAnchor(
            WaypointId: lastEntry.ToWaypointId.Trim(),
            S_Cm: running,
            RouteId: lastEntry.Id,
            Permission: RoutePermission.Stop(),
            DriveProfile: null,
            Block: lastEntry.Block));

        TotalDistanceCm = running;
    }

    /// <summary>
    /// Adds a single sensor marker to the current route.
    /// </summary>
    /// <param name="sensorMarker">Sensor marker to validate and register.</param>
    public void AddSensorMarker(SensorMarker sensorMarker)
    {
        ArgumentNullException.ThrowIfNull(sensorMarker);

        if (!_routesById.TryGetValue(sensorMarker.RouteId, out var routeEntry))
            throw new InvalidOperationException($"Unknown RouteId '{sensorMarker.RouteId}' in sensor marker.");

        if (sensorMarker.OffsetCm < 0 || sensorMarker.OffsetCm > routeEntry.DistanceCm)
        {
            throw new InvalidOperationException(
                $"Sensor marker '{sensorMarker.SensorId}' offset {sensorMarker.OffsetCm} is outside route entry '{sensorMarker.RouteId}' length {routeEntry.DistanceCm}.");
        }

        var sensorId = sensorMarker.SensorId;
        if (_sensorAnchors.ContainsKey(sensorId))
            throw new InvalidOperationException($"Duplicate sensor marker '{sensorId}'.");

        _sensorMarkers.Add(sensorMarker);

        var s = _routeEntryStartById[sensorMarker.RouteId] + sensorMarker.OffsetCm;
        _sensorAnchors[sensorId] = new SensorAnchor(
            SensorId: sensorId,
            S_Cm: s,
            RouteId: sensorMarker.RouteId,
            Block: routeEntry.Block);
    }

    /// <summary>
    /// Adds multiple sensor markers to the current route.
    /// </summary>
    /// <param name="sensorMarkers">Sensor markers to validate and register.</param>
    public void AddSensorMarkers(IReadOnlyList<SensorMarker> sensorMarkers)
    {
        ArgumentNullException.ThrowIfNull(sensorMarkers);
        foreach (var marker in sensorMarkers)
            AddSensorMarker(marker);
    }

    /// <summary>
    /// Adds a single position-based action event to the route.
    /// </summary>
    /// <param name="actionEvent">Action event to validate and register.</param>
    public void AddActionEvent(RouteActionEvent actionEvent)
    {
        ArgumentNullException.ThrowIfNull(actionEvent);

        if (string.IsNullOrWhiteSpace(actionEvent.EventId))
            throw new InvalidOperationException("Route action event requires EventId.");

        if (actionEvent.PositionCm < 0)
            throw new InvalidOperationException($"Route action event '{actionEvent.EventId}' has negative position.");

        if (actionEvent.PositionCm > TotalDistanceCm)
        {
            throw new InvalidOperationException(
                $"Route action event '{actionEvent.EventId}' at {actionEvent.PositionCm} exceeds route length {TotalDistanceCm}.");
        }

        _actionEvents.Add(actionEvent with { EventId = actionEvent.EventId.Trim() });
        _actionEvents.Sort((a, b) => a.PositionCm.CompareTo(b.PositionCm));
    }

    /// <summary>
    /// Adds multiple position-based action events to the route.
    /// </summary>
    /// <param name="actionEvents">Action events to validate and register.</param>
    public void AddActionEvents(IReadOnlyList<RouteActionEvent> actionEvents)
    {
        ArgumentNullException.ThrowIfNull(actionEvents);
        foreach (var actionEvent in actionEvents)
            AddActionEvent(actionEvent);
    }

    /// <summary>
    /// Tries to resolve a sensor anchor by its identifier.
    /// </summary>
    /// <param name="sensorId">Sensor identifier to resolve.</param>
    /// <param name="anchor">Resolved sensor anchor when the lookup succeeds.</param>
    /// <returns><c>true</c> if a matching sensor anchor exists; otherwise <c>false</c>.</returns>
    public bool TryGetSensorAnchor(int sensorId, out SensorAnchor anchor)
    {
        return _sensorAnchors.TryGetValue(sensorId, out anchor!);
    }

    /// <summary>
    /// Returns the effective route permission at the specified absolute route position.
    /// </summary>
    /// <param name="routePositionCm">Absolute route position in model centimeters.</param>
    /// <returns>The permission state that applies at the specified position.</returns>
    public RoutePermissionState GetRoutePermissionAt(double routePositionCm)
    {
        var clamped = ClampRoutePosition(routePositionCm);
        var state = RoutePermissionState.FailSafeStop;

        foreach (var anchor in _waypointAnchors)
        {
            if (anchor.S_Cm > clamped)
                break;

            state = new RoutePermissionState(
                ProceedAllowed: anchor.Permission.ProceedAllowed,
                MaxSpeedKmh: anchor.Permission.MaxSpeedKmh,
                SourceWaypointId: anchor.WaypointId,
                SourceAspect: anchor.Permission.Aspect,
                SourceRouteId: anchor.RouteId,
                SourceRoutePositionCm: anchor.S_Cm);
        }

        return state;
    }

    /// <summary>
    /// Tries to resolve the active route cycle that spans the specified absolute route position.
    /// </summary>
    /// <param name="routePositionCm">Absolute route position in model centimeters.</param>
    /// <param name="cycle">Resolved route cycle when the lookup succeeds.</param>
    /// <returns><c>true</c> if a valid cycle could be resolved; otherwise <c>false</c>.</returns>
    public bool TryGetRouteCycleAt(double routePositionCm, out RouteCycle cycle)
    {
        cycle = default!;
        if (_waypointAnchors.Count < 2)
            return false;

        var clamped = ClampRoutePosition(routePositionCm);
        var from = _waypointAnchors[0];

        foreach (var anchor in _waypointAnchors)
        {
            if (anchor.S_Cm <= clamped)
                from = anchor;
            else
                break;
        }

        var fromIndex = _waypointAnchors.IndexOf(from);
        if (fromIndex < 0 || fromIndex >= _waypointAnchors.Count - 1)
            return false;

        var to = _waypointAnchors[fromIndex + 1];
        var distance = Math.Max(0.0, to.S_Cm - from.S_Cm);

        cycle = new RouteCycle(
            FromWaypoint: from,
            ToWaypoint: to,
            DistanceCm: distance,
            AllowedSpeedKmh: ResolveCycleAllowedSpeed(from),
            DriveProfile: from.DriveProfile);

        return true;
    }

    /// <summary>
    /// Returns upcoming route points at or ahead of the specified absolute route position.
    /// </summary>
    /// <param name="routePositionCm">Absolute route position in model centimeters.</param>
    /// <param name="maxCount">Maximum number of route points to return.</param>
    /// <returns>An ordered list of upcoming route points.</returns>
    public IReadOnlyList<UpcomingRoutePoint> GetUpcomingRoutePoints(double routePositionCm, int maxCount = 5)
    {
        if (maxCount <= 0)
            return Array.Empty<UpcomingRoutePoint>();

        var clamped = ClampRoutePosition(routePositionCm);
        var result = new List<UpcomingRoutePoint>(Math.Min(maxCount, _waypointAnchors.Count));

        foreach (var anchor in _waypointAnchors)
        {
            if (anchor.S_Cm < clamped)
                continue;

            result.Add(new UpcomingRoutePoint(
                WaypointId: anchor.WaypointId,
                DistanceAheadCm: anchor.S_Cm - clamped,
                Permission: anchor.Permission,
                DriveProfile: anchor.DriveProfile,
                RouteId: anchor.RouteId,
                RoutePositionCm: anchor.S_Cm));

            if (result.Count >= maxCount)
                break;
        }

        return result;
    }

    /// <summary>
    /// Returns all action events crossed between two absolute route positions.
    /// </summary>
    /// <param name="fromPositionCm">Start position of the evaluation interval in model centimeters.</param>
    /// <param name="toPositionCm">End position of the evaluation interval in model centimeters.</param>
    /// <returns>Triggered action events in traversal order.</returns>
    public IReadOnlyList<TriggeredRouteAction> GetActionEventsBetween(double fromPositionCm, double toPositionCm)
    {
        if (_actionEvents.Count == 0)
            return Array.Empty<TriggeredRouteAction>();

        var from = ClampRoutePosition(fromPositionCm);
        var to = ClampRoutePosition(toPositionCm);

        if (Math.Abs(from - to) < double.Epsilon)
            return Array.Empty<TriggeredRouteAction>();

        var forward = to > from;
        var result = new List<TriggeredRouteAction>();

        foreach (var actionEvent in _actionEvents)
        {
            var p = actionEvent.PositionCm;
            var isHit = forward
                ? p > from && p <= to
                : p < from && p >= to;

            if (!isHit)
                continue;

            result.Add(new TriggeredRouteAction(
                Event: actionEvent,
                TriggerPositionCm: p,
                ForwardDirection: forward));
        }

        return result;
    }

    private double ClampRoutePosition(double routePositionCm)
    {
        if (TotalDistanceCm <= 0)
            return Math.Max(0.0, routePositionCm);

        if (routePositionCm < 0)
            return 0.0;

        if (routePositionCm > TotalDistanceCm)
            return TotalDistanceCm;

        return routePositionCm;
    }

    private static int ResolveCycleAllowedSpeed(RouteAnchor fromWaypoint)
    {
        if (!fromWaypoint.Permission.ProceedAllowed)
            return 0;

        if (fromWaypoint.Permission.MaxSpeedKmh is not null)
        {
            var vmax = fromWaypoint.Permission.MaxSpeedKmh.Value;
            return Math.Max(0, vmax);
        }

        return 0;
    }

    private static void ValidateRouteEntry(RouteEntry routeEntry)
    {
        if (string.IsNullOrWhiteSpace(routeEntry.FromWaypointId))
            throw new InvalidOperationException($"Route entry id '{routeEntry.Id}' requires FromWaypointId.");

        if (string.IsNullOrWhiteSpace(routeEntry.ToWaypointId))
            throw new InvalidOperationException($"Route entry id '{routeEntry.Id}' requires ToWaypointId.");

        if (routeEntry.DistanceCm < 0)
            throw new InvalidOperationException($"Route entry id '{routeEntry.Id}' has negative DistanceCm.");

        if (routeEntry.StartRoutePermission is null)
            throw new InvalidOperationException($"Route entry id '{routeEntry.Id}' requires StartRoutePermission.");

        if (routeEntry.StartRoutePermission.MaxSpeedKmh is < 0)
            throw new InvalidOperationException($"Route entry id '{routeEntry.Id}' has negative MaxSpeedKmh.");

        if (routeEntry.StartRoutePermission.ProceedAllowed && routeEntry.StartRoutePermission.MaxSpeedKmh is null)
            throw new InvalidOperationException($"Route entry id '{routeEntry.Id}' requires MaxSpeedKmh when ProceedAllowed is true.");

        if (!routeEntry.StartRoutePermission.ProceedAllowed && routeEntry.StartRoutePermission.MaxSpeedKmh is > 0)
            throw new InvalidOperationException($"Route entry id '{routeEntry.Id}' defines stop waypoint with MaxSpeedKmh > 0.");
    }
}



