// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.Trajectory;

/// <summary>
/// Defines a distance-based speed trajectory for train control.
/// The trajectory is master-side logic; Train acts as slave and executes speed commands only.
/// </summary>
public interface ISpeedTrajectory
{
    /// <summary>
    /// Total distance over which the trajectory is defined (model scale, cm).
    /// </summary>
    double TotalDistanceCmModel { get; }

    /// <summary>
    /// Evaluates the target speed at a given traveled distance.
    /// </summary>
    /// <param name="traveledModelCm">Distance traveled on the model (cm). Automatically clamped to [0, TotalDistanceCmModel].</param>
    /// <returns>Target speed in km/h (prototype scale).</returns>
    double GetSpeedKmhAtModelDistanceCm(double traveledModelCm);
}


