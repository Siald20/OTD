// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record RouteCycle(
    string FromWaypointId,
    string ToWaypointId,
    double DistanceCm,
    int AllowedSpeedKmh,
    RouteDriveProfile? DriveProfile);

