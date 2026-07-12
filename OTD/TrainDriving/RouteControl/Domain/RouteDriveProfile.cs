// SPDX-License-Identifier: GPL-3.0-or-later

using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record RouteDriveProfile(
    AccelerationTrajectoryPresets? AccelerationPreset = null,
    BrakingTrajectoryPreset? BrakingPreset = null);

