// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving;

/// <summary>
/// Executes a trajectory on a train by:
/// 1. Querying the trajectory for target speed at current distance
/// 2. Sending speed commands to the Train (which acts as slave)
/// 
/// The executor itself is stateless; it simply reads distance and applies trajectory.
/// </summary>
public sealed class TrajectoryExecutor
{
    private readonly Train _train;
    private readonly ISpeedTrajectory _trajectory;
    private readonly int _endSpeedKmh;

    public TrajectoryExecutor(Train train, ISpeedTrajectory trajectory)
    {
        _train = train ?? throw new ArgumentNullException(nameof(train));
        _trajectory = trajectory ?? throw new ArgumentNullException(nameof(trajectory));
        _endSpeedKmh = (int)Math.Round(_trajectory.GetSpeedKmhAtModelDistanceCm(_trajectory.TotalDistanceCmModel));
    }

    /// <summary>
    /// Evaluates the trajectory at a given model distance and applies the target speed to the train.
    /// Safe to call repeatedly (e.g., in a cycle).
    /// </summary>
    /// <param name="traveledModelCm">Distance traveled on the model (cm). Will be clamped to trajectory bounds.</param>
    /// <param name="cancellationToken">Cancellation token for the async train command.</param>
    public async Task ExecuteAtDistanceAsync(double traveledModelCm, CancellationToken cancellationToken = default)
    {
        var targetSpeedKmh = _trajectory.GetSpeedKmhAtModelDistanceCm(traveledModelCm);
        var speedInt = (int)Math.Round(targetSpeedKmh);

        if (traveledModelCm >= _trajectory.TotalDistanceCmModel)
            speedInt = _endSpeedKmh;

        // Clamp to VMin to prevent sub-minimum speeds that would be rejected by Train.
        // Exception: 0 km/h is allowed as a valid halt command.
        if (speedInt > 0 && speedInt < _train.VMin)
            speedInt = _train.VMin;

        await _train.SetSpeedVAsync(speedInt, cancellationToken);
    }
}
