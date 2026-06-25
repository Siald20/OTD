// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.Trajectory;

/// <summary>
/// Optional curve control point in distance-speed coordinates.
/// X is normalized to [0..1] over total distance.
/// </summary>
public sealed record TrajectoryControlPoint(double XModelRatio, double SpeedKmhPrototype)
{
    public void Validate()
    {
        if (XModelRatio <= 0.0 || XModelRatio >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(XModelRatio), XModelRatio,
                "Control point ratio must be between 0 and 1 (exclusive).");
        }

        if (SpeedKmhPrototype < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedKmhPrototype), SpeedKmhPrototype,
                "Control point speed must be greater than or equal to zero.");
        }
    }
}


