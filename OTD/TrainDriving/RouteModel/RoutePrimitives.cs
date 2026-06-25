// SPDX-License-Identifier: GPL-3.0-or-later

using OTD.TrainDriving.Presets;

namespace OTD.TrainDriving.RouteModel;

/// <summary>
/// Permission state associated with a waypoint (for example a signal aspect).
/// </summary>
public sealed record RoutePermission(bool ProceedAllowed, int? MaxSpeedKmh = null, string? Aspect = null)
{
    /// <summary>
    /// Creates a permissive route permission with an optional speed limit and aspect label.
    /// </summary>
    /// <param name="maxSpeedKmh">Maximum permitted speed in km/h.</param>
    /// <param name="aspect">Optional aspect or display label associated with the permission.</param>
    /// <returns>A route permission that allows proceeding.</returns>
    public static RoutePermission Proceed(int maxSpeedKmh, string? aspect = null)
        => new(true, maxSpeedKmh, aspect);

    /// <summary>
    /// Creates a restrictive route permission that represents stop.
    /// </summary>
    /// <param name="aspect">Optional aspect or display label associated with the stop state.</param>
    /// <returns>A route permission that forbids proceeding.</returns>
    public static RoutePermission Stop(string? aspect = "Halt")
        => new(false, 0, aspect);
}

/// <summary>
/// Optional drive profile applied while traversing a route entry.
/// </summary>
/// <param name="AccelerationPreset">Optional acceleration preset override.</param>
/// <param name="BrakingPreset">Optional braking preset override.</param>
public sealed record RouteDriveProfile(
    AccelerationTrajectoryPreset? AccelerationPreset = null,
    BrakingTrajectoryPreset? BrakingPreset = null);

/// <summary>
/// A single route entry connecting one waypoint to the next waypoint.
/// </summary>
/// <param name="Id">Unique identifier of the route entry.</param>
/// <param name="FromWaypointId">Identifier of the starting waypoint.</param>
/// <param name="ToWaypointId">Identifier of the destination waypoint.</param>
/// <param name="DistanceCm">Distance between the two waypoints in model centimeters.</param>
/// <param name="StartRoutePermission">Permission state that applies from the start waypoint for this route entry.</param>
/// <param name="DriveProfile">Optional drive profile associated with the route entry.</param>
/// <param name="Block">Optional block identifier associated with the route entry.</param>
public sealed record RouteEntry(
    int Id,
    string FromWaypointId,
    string ToWaypointId,
    int DistanceCm,
    RoutePermission StartRoutePermission,
    RouteDriveProfile? DriveProfile = null,
    string? Block = null);

/// <summary>
/// Sensor marker positioned inside a route entry.
/// </summary>
/// <param name="RouteId">Identifier of the owning route entry.</param>
/// <param name="SensorId">Identifier of the sensor marker.</param>
/// <param name="OffsetCm">Offset from the start of the route entry in model centimeters.</param>
public sealed record SensorMarker(
    int RouteId,
    int OffsetCm,
    int SensorId);

/// <summary>
/// Supported action kinds for position-based route actions.
/// </summary>
public enum RouteActionType
{
    Generic = 0,
    WarnHorn,
    CustomSound,
    LogMarker
}

/// <summary>
/// Position-based action event that can be triggered independently of sensors.
/// </summary>
/// <param name="EventId">Identifier of the route action event.</param>
/// <param name="Type">Action type that describes the intended behavior.</param>
/// <param name="PositionCm">Absolute route position of the action event in model centimeters.</param>
/// <param name="Payload">Optional payload associated with the action event.</param>
/// <param name="TriggerOnce"><c>true</c> if the action should only trigger once.</param>
public sealed record RouteActionEvent(
    string EventId,
    RouteActionType Type,
    double PositionCm,
    string? Payload = null,
    bool TriggerOnce = true);

/// <summary>
/// Normalized route anchor describing a waypoint at a cumulative route position.
/// </summary>
/// <param name="WaypointId">Identifier of the waypoint represented by the anchor.</param>
/// <param name="S_Cm">Absolute route position in model centimeters.</param>
/// <param name="RouteId">Identifier of the route entry that leads to this anchor.</param>
/// <param name="Permission">Permission state that applies at the waypoint.</param>
/// <param name="DriveProfile">Optional drive profile that starts at the waypoint.</param>
/// <param name="Block">Optional block associated with the anchor.</param>
public sealed record RouteAnchor(
    string WaypointId,
    double S_Cm,
    int RouteId,
    RoutePermission Permission,
    RouteDriveProfile? DriveProfile,
    string? Block);

/// <summary>
/// Mapping from a sensor identifier to an absolute route position.
/// </summary>
/// <param name="SensorId">Identifier of the sensor.</param>
/// <param name="S_Cm">Absolute route position in model centimeters.</param>
/// <param name="RouteId">Identifier of the owning route entry.</param>
/// <param name="Block">Optional block associated with the sensor anchor.</param>
public sealed record SensorAnchor(
    int SensorId,
    double S_Cm,
    int RouteId,
    string? Block);

/// <summary>
/// Active route cycle spanning from one route anchor to the next.
/// </summary>
/// <param name="FromWaypoint">Start anchor of the cycle.</param>
/// <param name="ToWaypoint">End anchor of the cycle.</param>
/// <param name="DistanceCm">Distance between the anchors in model centimeters.</param>
/// <param name="AllowedSpeedKmh">Allowed speed for the cycle in km/h resolved from the start permission.</param>
/// <param name="DriveProfile">Optional drive profile associated with the cycle.</param>
public sealed record RouteCycle(
    RouteAnchor FromWaypoint,
    RouteAnchor ToWaypoint,
    double DistanceCm,
    int AllowedSpeedKmh,
    RouteDriveProfile? DriveProfile);

/// <summary>
/// Effective permission state at a specific route position.
/// </summary>
/// <param name="ProceedAllowed"><c>true</c> if proceeding is permitted.</param>
/// <param name="MaxSpeedKmh">Optional maximum permitted speed in km/h.</param>
/// <param name="SourceWaypointId">Identifier of the waypoint that contributed the state.</param>
/// <param name="SourceAspect">Optional aspect text that contributed the state.</param>
/// <param name="SourceRouteId">Identifier of the route entry that contributed the state.</param>
/// <param name="SourceRoutePositionCm">Absolute route position of the contributing waypoint in model centimeters.</param>
public sealed record RoutePermissionState(
    bool ProceedAllowed,
    int? MaxSpeedKmh,
    string? SourceWaypointId,
    string? SourceAspect,
    int? SourceRouteId,
    double? SourceRoutePositionCm)
{
    /// <summary>
    /// Gets a fail-safe default permission state that forbids proceeding.
    /// </summary>
    public static RoutePermissionState FailSafeStop { get; } =
        new(false, 0, null, null, null, null);
}

/// <summary>
/// Describes an upcoming waypoint relative to the current route position.
/// </summary>
/// <param name="WaypointId">Identifier of the upcoming waypoint.</param>
/// <param name="DistanceAheadCm">Distance ahead of the current position in model centimeters.</param>
/// <param name="Permission">Permission state that applies at the upcoming waypoint.</param>
/// <param name="DriveProfile">Optional drive profile that starts at the upcoming waypoint.</param>
/// <param name="RouteId">Identifier of the route entry leading to the waypoint.</param>
/// <param name="RoutePositionCm">Absolute route position of the waypoint in model centimeters.</param>
public sealed record UpcomingRoutePoint(
    string WaypointId,
    double DistanceAheadCm,
    RoutePermission Permission,
    RouteDriveProfile? DriveProfile,
    int RouteId,
    double RoutePositionCm);

/// <summary>
/// Action event that was crossed between two route positions.
/// </summary>
/// <param name="Event">The triggered route action event.</param>
/// <param name="TriggerPositionCm">Absolute route position where the event was triggered.</param>
/// <param name="ForwardDirection"><c>true</c> if the route was traversed in the forward direction.</param>
public sealed record TriggeredRouteAction(
    RouteActionEvent Event,
    double TriggerPositionCm,
    bool ForwardDirection);



