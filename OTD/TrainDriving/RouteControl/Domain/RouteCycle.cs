// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

/// <summary>
/// Represents one driving segment between two waypoints.
/// </summary>
/// <param name="FromWaypointId">Start waypoint of this segment.</param>
/// <param name="ToWaypointId">End waypoint of this segment.</param>
/// <param name="DistanceCm">Physical length of this segment in model cm.</param>
/// <param name="AllowedSpeedKmh">
///   Target speed at the END of this segment (and the permitted maximum when no intermediate speed applies).
/// </param>
/// <param name="DriveProfile">Optional preset overrides for acceleration/braking curves.</param>
/// <param name="MaxIntermediateSpeedKmh">
///   Optional peak speed the train may reach DURING the segment before braking to <see cref="AllowedSpeedKmh"/>.
///   When set and greater than <see cref="AllowedSpeedKmh"/>, the segment uses a hybrid profile:
///   the train accelerates freely (time-based) toward this intermediate cap, then the distance-based
///   braking loop is triggered automatically once the brake point is reached.
///   Example: AllowedSpeedKmh=30, MaxIntermediateSpeedKmh=60 → accelerate toward 60, brake to 30 at end.
/// </param>
public sealed record RouteCycle(
    string FromWaypointId,
    string ToWaypointId,
    double DistanceCm,
    int AllowedSpeedKmh,
    RouteDriveProfile? DriveProfile,
    int? MaxIntermediateSpeedKmh = null);

