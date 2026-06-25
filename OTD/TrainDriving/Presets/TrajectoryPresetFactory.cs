// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.Presets;

/// <summary>
/// Factory for mapping named trajectory presets to concrete <see cref="DrivingTrajectoryRequest"/> configurations.
/// 
/// Separation of concerns:
/// - <see cref="Apply(DrivingTrajectoryRequest, AccelerationTrajectoryPreset)"/> handles acceleration phase presets
/// - <see cref="Apply(DrivingTrajectoryRequest, BrakingTrajectoryPreset)"/> handles braking phase presets
/// 
/// Each preset defines a curve type and optional control point(s) that shape the speed profile over distance.
/// </summary>
public static class TrajectoryPresetFactory
{
    /// <summary>
    /// Applies an acceleration preset to a driving trajectory request.
    /// 
    /// Maps named acceleration profiles (Linear, Comfort, EarlyAcceleration, etc.) to specific
    /// curve shapes and control points that define how speed increases from current to target speed
    /// over a given distance.
    /// </summary>
    /// <param name="request">
    ///   The base driving trajectory request specifying current speed, target speed, distance, and scale.
    ///   The returned request will retain all original values and add/override curve configuration.
    /// </param>
    /// <param name="preset">
    ///   The named acceleration preset to apply. Determines curve type, control point placement, and shape factor.
    /// </param>
    /// <returns>
    ///   A new <see cref="DrivingTrajectoryRequest"/> with curve parameters configured according to the preset.
    ///   All other request properties remain unchanged.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if preset is not a recognized <see cref="AccelerationTrajectoryPreset"/> value.</exception>
    public static DrivingTrajectoryRequest Apply(DrivingTrajectoryRequest request, AccelerationTrajectoryPreset preset)
    {
        return preset switch
        {
            AccelerationTrajectoryPreset.Linear => request with
            {
                CurveType = TrajectoryCurveType.Linear,
                ControlPoint = null,
                CurveShapePercent = 50.0
            },
            AccelerationTrajectoryPreset.Comfort => request with
            {
                CurveType = TrajectoryCurveType.EaseInOut,
                ControlPoint = null,
                CurveShapePercent = 80.0
            },
            AccelerationTrajectoryPreset.EarlyAcceleration => request with
            {
                CurveType = TrajectoryCurveType.ControlPoint,
                ControlPoint = CreateControlPoint(request, xRatio: 0.35, speedRatio: 0.75),
                CurveShapePercent = 50.0
            },
            AccelerationTrajectoryPreset.LateAcceleration => request with
            {
                CurveType = TrajectoryCurveType.ControlPoint,
                ControlPoint = CreateControlPoint(request, xRatio: 0.75, speedRatio: 0.20),
                CurveShapePercent = 50.0
            },
            AccelerationTrajectoryPreset.BalancedControlPoint => request with
            {
                CurveType = TrajectoryCurveType.ControlPoint,
                ControlPoint = CreateControlPoint(request, xRatio: 0.50, speedRatio: 0.50),
                CurveShapePercent = 50.0
            },
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unsupported acceleration preset.")
        };
    }

    /// <summary>
    /// Applies a braking preset to a driving trajectory request.
    /// 
    /// Maps named braking profiles (Linear, Comfort, AggressiveBrake, EarlyBrake, LateBrake, etc.) to specific
    /// curve shapes and control points that define how speed decreases from current to target speed
    /// over a given distance, ensuring the train arrives at the target speed exactly at the segment end.
    /// </summary>
    /// <param name="request">
    ///   The base driving trajectory request specifying current speed, target speed, distance, and scale.
    ///   The returned request will retain all original values and add/override curve configuration.
    /// </param>
    /// <param name="preset">
    ///   The named braking preset to apply. Determines curve type, control point placement, and shape factor.
    /// </param>
    /// <returns>
    ///   A new <see cref="DrivingTrajectoryRequest"/> with curve parameters configured according to the preset.
    ///   All other request properties remain unchanged.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if preset is not a recognized <see cref="BrakingTrajectoryPreset"/> value.</exception>
    public static DrivingTrajectoryRequest Apply(DrivingTrajectoryRequest request, BrakingTrajectoryPreset preset)
    {
        return preset switch
        {
            BrakingTrajectoryPreset.Linear => request with
            {
                CurveType = TrajectoryCurveType.Linear,
                ControlPoint = null,
                CurveShapePercent = 50.0
            },
            BrakingTrajectoryPreset.Comfort => request with
            {
                CurveType = TrajectoryCurveType.EaseInOut,
                ControlPoint = null,
                CurveShapePercent = 80.0
            },
            BrakingTrajectoryPreset.AggressiveBrake => request with
            {
                CurveType = TrajectoryCurveType.ControlPoint,
                ControlPoint = CreateControlPoint(request, xRatio: 0.35, speedRatio: 0.75),
                CurveShapePercent = 50.0
            },
            BrakingTrajectoryPreset.EarlyBrake => request with
            {
                CurveType = TrajectoryCurveType.ControlPoint,
                // Ziel: bei ca. 50% Distanz ~1/3 der Startgeschwindigkeit, danach Ausrollen.
                ControlPoint = CreateControlPoint(request, xRatio: 0.50, speedRatio: 2.0 / 3.0),
                CurveShapePercent = 50.0
            },
            BrakingTrajectoryPreset.LateBrake => request with
            {
                CurveType = TrajectoryCurveType.ControlPoint,
                ControlPoint = CreateControlPoint(request, xRatio: 0.75, speedRatio: 0.20),
                CurveShapePercent = 50.0
            },
            BrakingTrajectoryPreset.BalancedControlPoint => request with
            {
                CurveType = TrajectoryCurveType.ControlPoint,
                ControlPoint = CreateControlPoint(request, xRatio: 0.50, speedRatio: 0.50),
                CurveShapePercent = 50.0
            },
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unsupported braking preset.")
        };
    }

    /// <summary>
    /// Creates a trajectory control point at a specific distance and speed position.
    /// </summary>
    /// <param name="request">
    ///   The driving trajectory request providing the speed range for interpolation.
    /// </param>
    /// <param name="xRatio">
    ///   Normalized position along the distance axis, in range (0..1).
    ///   0 represents the start, 1 represents the end of the segment.
    /// </param>
    /// <param name="speedRatio">
    ///   Normalized position along the speed range, in range [0..1].
    ///   0 corresponds to current speed, 1 corresponds to target speed.
    /// </param>
    /// <returns>
    ///   A new <see cref="TrajectoryControlPoint"/> positioned at the interpolated location.
    /// </returns>
    private static TrajectoryControlPoint CreateControlPoint(DrivingTrajectoryRequest request, double xRatio, double speedRatio)
    {
        var speed = request.CurrentSpeedKmhPrototype +
                    ((request.TargetSpeedKmhPrototype - request.CurrentSpeedKmhPrototype) * speedRatio);

        return new TrajectoryControlPoint(xRatio, speed);
    }
}
