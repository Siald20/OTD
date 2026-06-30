// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record RouteLeg(
    string FromWaypointId,
    string ToWaypointId,
    int DistanceCm,
    double MaxSpeedKmh,
    RouteDriveProfile? DriveProfile = null,
    AccelerationStartPolicy AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint,
    RouteMetadata? Metadata = null,
    StopPoint? StopPoint = null,
    IReadOnlyList<SensorMarker>? SensorMarkers = null);

