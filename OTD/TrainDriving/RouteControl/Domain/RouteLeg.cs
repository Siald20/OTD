// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record RouteLeg
{
    // User-facing constructor: topology-derived fields are expanded into segment entries by RouteLegResolver.
    public RouteLeg(
        string FromWaypointId,
        string ToWaypointId,
        int? MaxSpeedKmh = null,
        RouteDriveProfile? DriveProfile = null,
        int? StopPointToTargetCm = null,
        RouteMetadata? Metadata = null)
    {
        this.FromWaypointId = FromWaypointId;
        this.ToWaypointId = ToWaypointId;
        // No explicit cap => let segment limits from topology resolve the effective speed.
        this.MaxSpeedKmh = MaxSpeedKmh ?? int.MaxValue;
        this.DriveProfile = DriveProfile;
        this.StopPointToTargetCm = StopPointToTargetCm;
        this.Metadata = Metadata;
    }


    public string FromWaypointId { get; init; }
    public string ToWaypointId { get; init; }
    public int DistanceCm { get; init; }
    public int MaxSpeedKmh { get; init; }
    public RouteDriveProfile? DriveProfile { get; init; }
    public AccelerationStartPolicy AccelerationStartPolicy { get; init; } = AccelerationStartPolicy.AfterTrainClearsWaypoint;
    public RouteMetadata? Metadata { get; init; }
    public int? StopPointToTargetCm { get; init; }

    // Runtime fields (resolved/expanded):
    public StopPoint? StopPoint { get; init; }
    public IReadOnlyList<SensorMarker>? SensorMarkers { get; init; }
    public IReadOnlyList<FeedbackReference>? FeedbackReferences { get; init; }
    public string? GroupId { get; init; }
    public int GroupTotalDistanceCm { get; init; }
    public int GroupOffsetStartCm { get; init; }

    public bool IsTopologyResolved => DistanceCm > 0;
}

