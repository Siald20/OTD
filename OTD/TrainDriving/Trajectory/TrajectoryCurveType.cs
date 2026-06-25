// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.Trajectory;

/// <summary>
/// Selects how speed is distributed over distance.
/// </summary>
public enum TrajectoryCurveType
{
    Linear,
    ControlPoint,
    EaseInOut
}


