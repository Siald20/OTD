// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Exceptions;

namespace OTD.TrainDriving.RouteControl.Services;

public sealed class RouteLegResolver
{
    private readonly IRouteDefinitionService _definitionService;

    public RouteLegResolver(IRouteDefinitionService definitionService)
    {
        _definitionService = definitionService ?? throw new ArgumentNullException(nameof(definitionService));
    }

    public RouteLeg Resolve(DynamicRouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_definitionService.TryGetLeg(request.FromWaypointId, request.ToWaypointId, out var staticLeg))
        {
            throw new RouteValidationException(
                $"No static route definition found for '{request.FromWaypointId}->{request.ToWaypointId}'.");
        }

        var effectiveMaxSpeed = request.MaxSpeedKmh ?? staticLeg.DefaultMaxSpeedKmh;
        if (effectiveMaxSpeed is null || effectiveMaxSpeed <= 0)
        {
            throw new RouteValidationException(
                $"RouteLeg '{request.FromWaypointId}->{request.ToWaypointId}' requires MaxSpeedKmh via dynamic request or static defaults.");
        }

        var effectiveDriveProfile = request.DriveProfile ?? staticLeg.DefaultDriveProfile;
        var effectiveAccelerationPolicy = request.AccelerationStartPolicy
            ?? staticLeg.DefaultAccelerationStartPolicy
            ?? Domain.AccelerationStartPolicy.AfterTrainClearsWaypoint;

        var effectiveStopPoint = ResolveStopPoint(staticLeg.DefaultStopPoint, request.StopPoint);
        var effectiveMetadata = request.Metadata ?? staticLeg.Metadata;

        var leg = new RouteLeg(
            FromWaypointId: staticLeg.FromWaypointId,
            ToWaypointId: staticLeg.ToWaypointId,
            DistanceCm: staticLeg.DistanceCm,
            MaxSpeedKmh: effectiveMaxSpeed.Value,
            DriveProfile: effectiveDriveProfile,
            AccelerationStartPolicy: effectiveAccelerationPolicy,
            Metadata: effectiveMetadata,
            StopPoint: effectiveStopPoint,
            SensorMarkers: staticLeg.SensorMarkers);

        RouteValidator.ValidateLeg(leg);
        return leg;
    }

    private static StopPoint? ResolveStopPoint(StopPoint? staticStopPoint, StopPointOverride? overrideStopPoint)
    {
        if (overrideStopPoint is null)
            return staticStopPoint;

        if (overrideStopPoint.Enabled is false)
            return null;

        var baseStopPoint = staticStopPoint;
        if (baseStopPoint is null && overrideStopPoint.OffsetCm is null)
        {
            return null;
        }

        var offset = overrideStopPoint.OffsetCm ?? baseStopPoint?.OffsetCm;
        if (offset is null)
            return null;

        var stopReason = overrideStopPoint.StopReason ?? baseStopPoint?.StopReason;
        var metadata = overrideStopPoint.Metadata ?? baseStopPoint?.Metadata;

        return new StopPoint(OffsetCm: offset.Value, StopReason: stopReason, Metadata: metadata);
    }
}

