// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record DynamicRouteRequest(
    RouteTravelDirection TravelDirection,
    string FromWaypointId,
    string ToWaypointId,
    int? MaxSpeedKmh = null,
    RouteDriveProfile? DriveProfile = null,
    AccelerationStartPolicy? AccelerationStartPolicy = null,
    int? StopPointToTargetCm = null,
    RouteMetadata? Metadata = null);

