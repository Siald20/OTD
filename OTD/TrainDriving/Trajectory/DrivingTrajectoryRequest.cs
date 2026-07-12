// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.Trajectory;

/// <summary>
/// Configuration for building a speed trajectory.
/// All parameters are validated during construction.
/// </summary>
public sealed record DrivingTrajectoryRequest(
    int CurrentSpeedKmhPrototype,
    int TargetSpeedKmhPrototype,
    int DistanceCmModel,
    int Scale,
    int? VMaxKmhPrototype = null,
    TrajectoryCurveType CurveType = TrajectoryCurveType.Linear,
    TrajectoryControlPoint? ControlPoint = null,
    TrajectoryControlPoint? SecondaryControlPoint = null,
    double CurveShapePercent = 50.0
)
{
    public void Validate()
    {
        if (Scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Scale), Scale, "Scale must be greater than zero.");
        }

        if (DistanceCmModel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DistanceCmModel), DistanceCmModel, "Distance must be greater than or equal to zero.");
        }

        if (CurrentSpeedKmhPrototype < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CurrentSpeedKmhPrototype), CurrentSpeedKmhPrototype, "Current speed must be greater than or equal to zero.");
        }

        if (TargetSpeedKmhPrototype < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetSpeedKmhPrototype), TargetSpeedKmhPrototype, "Target speed must be greater than or equal to zero.");
        }

        if (VMaxKmhPrototype is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VMaxKmhPrototype), VMaxKmhPrototype, "VMax must be greater than or equal to zero when specified.");
        }


        if (CurveShapePercent is < 0.0 or > 100.0)
        {
            throw new ArgumentOutOfRangeException(nameof(CurveShapePercent), CurveShapePercent,
                "CurveShapePercent must be in range 0..100.");
        }

        if (CurveType == TrajectoryCurveType.ControlPoint && ControlPoint is null)
        {
            throw new ArgumentException("ControlPoint must be set when CurveType is ControlPoint.", nameof(ControlPoint));
        }

        if (SecondaryControlPoint is not null && ControlPoint is null)
        {
            throw new ArgumentException("ControlPoint must be set when SecondaryControlPoint is used.", nameof(ControlPoint));
        }

        ControlPoint?.Validate();
        SecondaryControlPoint?.Validate();

        if (ControlPoint is not null && SecondaryControlPoint is not null &&
            SecondaryControlPoint.XModelRatio <= ControlPoint.XModelRatio)
        {
            throw new ArgumentException(
                "SecondaryControlPoint must be located after ControlPoint on the X axis.",
                nameof(SecondaryControlPoint));
        }
    }
}


