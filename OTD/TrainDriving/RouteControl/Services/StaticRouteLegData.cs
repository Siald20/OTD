// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using OTD.TrainDriving.RouteControl.Domain;

namespace OTD.TrainDriving.RouteControl.Services;

public sealed record StaticRouteLegData(
    string FromWaypointId,
    string ToWaypointId,
    int DistanceCm,
    IReadOnlyList<SensorMarker>? SensorMarkers = null,
    double? DefaultMaxSpeedKmh = null,
    RouteDriveProfile? DefaultDriveProfile = null,
    AccelerationStartPolicy? DefaultAccelerationStartPolicy = null,
    StopPoint? DefaultStopPoint = null,
    RouteMetadata? Metadata = null);

