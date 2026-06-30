// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record DynamicRouteRequest(
    string FromWaypointId,
    string ToWaypointId,
    double? MaxSpeedKmh = null,
    RouteDriveProfile? DriveProfile = null,
    AccelerationStartPolicy? AccelerationStartPolicy = null,
    StopPointOverride? StopPoint = null,
    RouteMetadata? Metadata = null);

