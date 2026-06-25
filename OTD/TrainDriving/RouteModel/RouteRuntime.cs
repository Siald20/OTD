// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OTD.TrainDriving.RouteModel;

/// <summary>
/// Runtime helper combining position tracking, route permissions, and free route actions.
/// </summary>
public sealed class RouteRuntime
{
    private readonly RouteTable _routeTable;
    private readonly HashSet<string> _triggeredOneShotEventIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a route runtime with an optional externally managed position tracker.
    /// </summary>
    /// <param name="routeTable">The route table used for permission, waypoint, and sensor lookups.</param>
    /// <param name="positionTracker">Optional externally created position tracker. A new tracker is created when omitted.</param>
    public RouteRuntime(RouteTable routeTable, PositionTracker? positionTracker = null)
    {
        _routeTable = routeTable ?? throw new ArgumentNullException(nameof(routeTable));
        PositionTracker = positionTracker ?? new PositionTracker(routeTable);
    }

    /// <summary>
    /// Gets the position tracker used by this runtime.
    /// </summary>
    public PositionTracker PositionTracker { get; }

    /// <summary>
    /// Applies one runtime step by integrating traveled distance, optionally recalibrating from a sensor, and resolving the resulting route state.
    /// </summary>
    /// <param name="deltaCm">Distance traveled since the previous update in model centimeters.</param>
    /// <param name="trajectorySpeedKmh">Requested trajectory speed in km/h.</param>
    /// <param name="activatedSensorId">Optional sensor identifier that became active during this step.</param>
    /// <returns>A tick result containing the updated position, effective speed, active route cycle, and triggered actions.</returns>
    public RouteRuntimeTickResult ApplyStep(double deltaCm, int trajectorySpeedKmh, int? activatedSensorId = null)
    {
        var previousPosition = PositionTracker.EstimatedPositionCm;
        PositionTracker.IntegrateDelta(deltaCm);
        var estimatedPositionBeforeRecalibration = PositionTracker.EstimatedPositionCm;

        var recalibrated = false;
        if (activatedSensorId is not null)
            recalibrated = PositionTracker.TryRecalibrateFromSensor(activatedSensorId.Value);

        var currentPosition = PositionTracker.EstimatedPositionCm;

        var permissionState = _routeTable.GetRoutePermissionAt(currentPosition);
        _routeTable.TryGetRouteCycleAt(currentPosition, out var activeCycle);

        var triggered = CollectTriggeredEvents(previousPosition, currentPosition);

        return new RouteRuntimeTickResult(
            EstimatedPositionCm: currentPosition,
            EstimatedPositionBeforeRecalibrationCm: estimatedPositionBeforeRecalibration,
            TrajectorySpeedKmh: Math.Max(0, trajectorySpeedKmh),
            EffectiveSpeedKmh: ApplyRoutePermission(trajectorySpeedKmh, permissionState),
            PermissionState: permissionState,
            ActiveCycle: activeCycle,
            TriggeredActions: triggered,
            PositionRecalibrated: recalibrated,
            CalibrationSensorId: recalibrated ? PositionTracker.LastCalibrationSensorId : null,
            CorrectionErrorCm: PositionTracker.LastCorrectionErrorCm);
    }

    private IReadOnlyList<TriggeredRouteAction> CollectTriggeredEvents(double previousPosition, double currentPosition)
    {
        var hits = _routeTable.GetActionEventsBetween(previousPosition, currentPosition);
        if (hits.Count == 0)
            return Array.Empty<TriggeredRouteAction>();

        var effective = new List<TriggeredRouteAction>(hits.Count);

        foreach (var hit in hits)
        {
            var eventId = hit.Event.EventId;

            if (hit.Event.TriggerOnce)
            {
                if (_triggeredOneShotEventIds.Contains(eventId))
                    continue;

                _triggeredOneShotEventIds.Add(eventId);
            }

            effective.Add(hit);
        }

        return new ReadOnlyCollection<TriggeredRouteAction>(effective);
    }

    private static int ApplyRoutePermission(int trajectorySpeedKmh, RoutePermissionState permissionState)
    {
        var requested = Math.Max(0, trajectorySpeedKmh);
        if (!permissionState.ProceedAllowed)
            return 0;

        if (permissionState.MaxSpeedKmh is not null)
        {
            var vmax = permissionState.MaxSpeedKmh.Value;
            return Math.Min(requested, Math.Max(0, vmax));
        }

        return requested;
    }
}

/// <summary>
/// Result of a single route runtime update step.
/// </summary>
/// <param name="EstimatedPositionCm">Estimated absolute route position in model centimeters after the step.</param>
/// <param name="EstimatedPositionBeforeRecalibrationCm">Estimated absolute route position after delta integration and before sensor recalibration.</param>
/// <param name="TrajectorySpeedKmh">Requested trajectory speed in km/h.</param>
/// <param name="EffectiveSpeedKmh">Effective speed after applying route permission constraints.</param>
/// <param name="PermissionState">Resolved route permission state at the updated position.</param>
/// <param name="ActiveCycle">Resolved active route cycle at the updated position, if available.</param>
/// <param name="TriggeredActions">Action events triggered during the step.</param>
/// <param name="PositionRecalibrated"><c>true</c> if a sensor recalibration was applied during the step.</param>
/// <param name="CalibrationSensorId">Identifier of the sensor used for recalibration, if any.</param>
/// <param name="CorrectionErrorCm">Difference between estimated and sensed position before recalibration, if available.</param>
public sealed record RouteRuntimeTickResult(
    double EstimatedPositionCm,
    double EstimatedPositionBeforeRecalibrationCm,
    int TrajectorySpeedKmh,
    int EffectiveSpeedKmh,
    RoutePermissionState PermissionState,
    RouteCycle? ActiveCycle,
    IReadOnlyList<TriggeredRouteAction> TriggeredActions,
    bool PositionRecalibrated,
    int? CalibrationSensorId,
    double? CorrectionErrorCm
);



