// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Services;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.RouteControl.Builder;

public sealed class RouteTableBuilder
{
    private readonly List<RouteLeg> _routeLegs = new();
    private readonly RouteLegResolver? _routeLegResolver;
    private readonly string _speedClass;

    public RouteTableBuilder()
    {
        _speedClass = "default";
    }

    public RouteTableBuilder(IRouteDefinitionService routeDefinitionService, IRailwayLayoutService trackLayoutService, string speedClass = "default")
    {
        ArgumentNullException.ThrowIfNull(routeDefinitionService);
        ArgumentNullException.ThrowIfNull(trackLayoutService);
        ArgumentException.ThrowIfNullOrWhiteSpace(speedClass);
        _routeLegResolver = new RouteLegResolver(routeDefinitionService, trackLayoutService);
        _speedClass = speedClass.Trim();
    }

    public IReadOnlyList<RouteLeg> RouteLegs => new ReadOnlyCollection<RouteLeg>(_routeLegs);

    public RouteTableBuilder AddRoute(
        string fromWaypointId,
        string toWaypointId,
        int? maxSpeedKmh = null,
        AccelerationStartPolicy accelerationStartPolicy = AccelerationStartPolicy.AtWaypointCrossing,
        AccelerationTrajectoryPreset? accelerationPreset = null,
        BrakingTrajectoryPreset? brakingPreset = null)
    {
        RouteDriveProfile? driveProfile = null;
        if (accelerationPreset is not null || brakingPreset is not null)
        {
            driveProfile = new RouteDriveProfile(
                AccelerationPreset: accelerationPreset,
                BrakingPreset: brakingPreset);
        }

        var leg = new RouteLeg(
            FromWaypointId: fromWaypointId,
            ToWaypointId: toWaypointId,
            MaxSpeedKmh: maxSpeedKmh,
            DriveProfile: driveProfile,
            StopPointToTargetCm: null,
            Metadata: null)
        {
            AccelerationStartPolicy = accelerationStartPolicy
        };

        if (_routeLegResolver is null)
        {
            throw new Exceptions.RouteValidationException(
                "RouteTableBuilder benötigt für AddRoute den Konstruktor mit IRouteDefinitionService und IRailwayLayoutService, damit RouteLegs in Segmenteinträge expandiert werden.");
        }

        var expanded = _routeLegResolver.Expand(leg, _speedClass);
        foreach (var segmentLeg in expanded)
        {
            ValidateDraftLeg(segmentLeg);
            _routeLegs.Add(segmentLeg);
        }

        return this;
    }

    public RouteTableBuilder AddStopPointToTarget(string fromWaypointId, int stopPointToTargetCm, string? stopReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);
        if (stopPointToTargetCm < 0)
            throw new Exceptions.RouteValidationException("StopPointToTargetCm must be >= 0.");

        var index = IndexOfFromWaypoint(fromWaypointId);
        var leg = _routeLegs[index] with
        {
            StopPointToTargetCm = stopPointToTargetCm
        };

        if (_routeLegResolver is null)
        {
            throw new Exceptions.RouteValidationException(
                "AddStopPointToTarget benötigt den Builder-Konstruktor mit Resolver, damit die Segmentexpansion aktualisiert werden kann.");
        }

        var expanded = _routeLegResolver.Expand(leg, _speedClass);
        _routeLegs.RemoveAt(index);
        _routeLegs.InsertRange(index, expanded);
        return this;
    }

    public IReadOnlyList<RouteLeg> Build()
    {
        RouteValidator.ValidateChain(_routeLegs);
        return new ReadOnlyCollection<RouteLeg>(new List<RouteLeg>(_routeLegs));
    }

    private static void ValidateDraftLeg(RouteLeg leg)
    {
        if (string.IsNullOrWhiteSpace(leg.FromWaypointId))
            throw new Exceptions.RouteValidationException("RouteLeg requires FromWaypointId.");

        if (string.IsNullOrWhiteSpace(leg.ToWaypointId))
            throw new Exceptions.RouteValidationException("RouteLeg requires ToWaypointId.");

        if (string.Equals(leg.FromWaypointId.Trim(), leg.ToWaypointId.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new Exceptions.RouteValidationException("RouteLeg requires different FromWaypointId and ToWaypointId.");

        if (leg.MaxSpeedKmh <= 0)
            throw new Exceptions.RouteValidationException("RouteLeg requires MaxSpeedKmh > 0.");

        if (leg.IsTopologyResolved)
            RouteValidator.ValidateLeg(leg);
    }

    private int IndexOfFromWaypoint(string fromWaypointId)
    {
        var normalized = fromWaypointId.Trim();
        var index = -1;

        for (var i = 0; i < _routeLegs.Count; i++)
        {
            if (!string.Equals(_routeLegs[i].FromWaypointId, normalized, StringComparison.OrdinalIgnoreCase))
                continue;

            if (index >= 0)
                throw new Exceptions.RouteValidationException($"fromWaypointId '{fromWaypointId}' is not unique in builder state.");

            index = i;
        }

        if (index < 0)
            throw new Exceptions.RouteValidationException($"Unknown fromWaypointId '{fromWaypointId}' in builder state.");

        return index;
    }
}

