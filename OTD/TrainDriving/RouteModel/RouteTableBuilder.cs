// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OTD.TrainDriving.Presets;

namespace OTD.TrainDriving.RouteModel;

/// <summary>
/// Fluent builder for waypoint-to-waypoint route definitions.
/// </summary>
public sealed class RouteTableBuilder
{
    private readonly List<RouteEntry> _routes = new();
    private readonly List<SensorMarker> _sensorMarkers = new();
    private readonly List<RouteActionEvent> _actionEvents = new();

    /// <summary>
    /// Gets the route entries currently collected by the builder.
    /// </summary>
    public IReadOnlyList<RouteEntry> Routes => new ReadOnlyCollection<RouteEntry>(_routes);

    /// <summary>
    /// Gets the sensor markers currently collected by the builder.
    /// </summary>
    public IReadOnlyList<SensorMarker> SensorMarkers => new ReadOnlyCollection<SensorMarker>(_sensorMarkers);

    /// <summary>
    /// Gets the action events currently collected by the builder.
    /// </summary>
    public IReadOnlyList<RouteActionEvent> ActionEvents => new ReadOnlyCollection<RouteActionEvent>(_actionEvents);

    /// <summary>
    /// Adds a single route entry from one waypoint to the next waypoint.
    /// </summary>
    /// <param name="id">Unique identifier of the route entry.</param>
    /// <param name="fromWaypointId">Identifier of the starting waypoint.</param>
    /// <param name="toWaypointId">Identifier of the destination waypoint.</param>
    /// <param name="distanceCm">Distance between the two waypoints in model centimeters.</param>
    /// <param name="startRoutePermission">Permission state that applies at the start waypoint of this route entry.</param>
    /// <param name="accelerationPreset">Optional acceleration preset to use for the resulting drive profile.</param>
    /// <param name="brakingPreset">Optional braking preset to use for the resulting drive profile.</param>
    /// <param name="block">Optional block identifier associated with the route entry.</param>
    /// <returns>The current builder instance.</returns>
    public RouteTableBuilder AddRoute(
        int id,
        string fromWaypointId,
        string toWaypointId,
        int distanceCm,
        RoutePermission startRoutePermission,
        AccelerationTrajectoryPreset? accelerationPreset = null,
        BrakingTrajectoryPreset? brakingPreset = null,
        string? block = null)
    {
        RouteDriveProfile? profile = null;
        if (accelerationPreset is not null || brakingPreset is not null)
        {
            profile = new RouteDriveProfile(
                AccelerationPreset: accelerationPreset,
                BrakingPreset: brakingPreset);
        }

        _routes.Add(new RouteEntry(
            Id: id,
            FromWaypointId: fromWaypointId,
            ToWaypointId: toWaypointId,
            DistanceCm: distanceCm,
            StartRoutePermission: startRoutePermission,
            DriveProfile: profile,
            Block: block));

        return this;
    }

    /// <summary>
    /// Adds a sensor marker positioned within a route entry.
    /// </summary>
    /// <param name="routeId">Identifier of the route entry that owns the marker.</param>
    /// <param name="sensorId">Identifier of the sensor marker.</param>
    /// <param name="offsetCm">Offset from the start of the route entry in model centimeters.</param>
    /// <returns>The current builder instance.</returns>
    public RouteTableBuilder AddSensorMarker(int routeId, int sensorId, int offsetCm)
    {
        _sensorMarkers.Add(new SensorMarker(
            RouteId: routeId,
            OffsetCm: offsetCm,
            SensorId: sensorId));

        return this;
    }

    /// <summary>
    /// Adds a free position-based action event to the route.
    /// </summary>
    /// <param name="eventId">Identifier of the action event.</param>
    /// <param name="type">Action type that describes the intended behavior.</param>
    /// <param name="positionCm">Absolute route position of the action event in model centimeters.</param>
    /// <param name="payload">Optional action payload.</param>
    /// <param name="triggerOnce"><c>true</c> to trigger the action only once; otherwise <c>false</c>.</param>
    /// <returns>The current builder instance.</returns>
    public RouteTableBuilder AddActionEvent(
        string eventId,
        RouteActionType type,
        double positionCm,
        string? payload = null,
        bool triggerOnce = true)
    {
        _actionEvents.Add(new RouteActionEvent(
            EventId: eventId,
            Type: type,
            PositionCm: positionCm,
            Payload: payload,
            TriggerOnce: triggerOnce));

        return this;
    }

    /// <summary>
    /// Appends multiple route entries to the builder state.
    /// </summary>
    /// <param name="routeEntries">Ordered route entries to append.</param>
    /// <returns>The current builder instance.</returns>
    public RouteTableBuilder AddRoute(IReadOnlyList<RouteEntry> routeEntries)
    {
        ArgumentNullException.ThrowIfNull(routeEntries);
        _routes.AddRange(routeEntries);
        return this;
    }

    /// <summary>
    /// Appends multiple sensor markers to the builder state.
    /// </summary>
    /// <param name="sensorMarkers">Sensor markers to append.</param>
    /// <returns>The current builder instance.</returns>
    public RouteTableBuilder AddSensorMarkers(IReadOnlyList<SensorMarker> sensorMarkers)
    {
        ArgumentNullException.ThrowIfNull(sensorMarkers);
        _sensorMarkers.AddRange(sensorMarkers);
        return this;
    }

    /// <summary>
    /// Appends multiple action events to the builder state.
    /// </summary>
    /// <param name="actionEvents">Action events to append.</param>
    /// <returns>The current builder instance.</returns>
    public RouteTableBuilder AddActionEvents(IReadOnlyList<RouteActionEvent> actionEvents)
    {
        ArgumentNullException.ThrowIfNull(actionEvents);
        _actionEvents.AddRange(actionEvents);
        return this;
    }

    /// <summary>
    /// Builds a fully initialized route table from the collected route entries, markers, and action events.
    /// </summary>
    /// <returns>A new route table instance.</returns>
    public RouteTable Build()
    {
        var table = new RouteTable();
        table.AddRoute(_routes);
        table.AddSensorMarkers(_sensorMarkers);
        table.AddActionEvents(_actionEvents);
        return table;
    }
    }




