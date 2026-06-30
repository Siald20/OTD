// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.Trajectory;

/// <summary>
/// Start-oriented acceleration trajectory.
///
/// The acceleration distance is derived from an explicit acceleration value in m/s².
/// The actual speed curve over that distance is then shaped by an
/// <see cref="AccelerationTrajectoryPreset"/>.
///
/// Behaviour:
/// <list type="bullet">
///   <item>The acceleration phase starts at the current position and is not tied to the end of the route segment.</item>
///   <item>The target speed may be reached before the segment end, or may remain unreachable on short segments.</item>
///   <item>Curve smoothness near the target speed is determined by the selected acceleration preset, not by a separate blend parameter.</item>
/// </list>
/// </summary>
public sealed class StartOrientedAccelerationTrajectory : ISpeedTrajectory
{
    private readonly ISpeedTrajectory _presetTrajectory;

    /// <summary>
    /// Creates a start-oriented acceleration trajectory.
    /// </summary>
    /// <param name="request">
    ///   Driving request supplying start/target speeds, segment distance and model scale.
    ///   <see cref="DrivingTrajectoryRequest.TargetSpeedKmhPrototype"/> must be greater than
    ///   <see cref="DrivingTrajectoryRequest.CurrentSpeedKmhPrototype"/>.
    /// </param>
    /// <param name="preset">
    ///   Acceleration preset that shapes how the start-oriented acceleration approaches the target speed.
    /// </param>
    /// <param name="accelerationMs2">
    ///   Effective acceleration in m/s² used for start-oriented acceleration distance estimation.
    /// </param>
    public StartOrientedAccelerationTrajectory(
        DrivingTrajectoryRequest request,
        AccelerationTrajectoryPreset preset,
        double accelerationMs2)
    {
        if (request.Scale <= 0)
            throw new ArgumentException("Scale must be > 0.", nameof(request));
        if (accelerationMs2 <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(accelerationMs2), accelerationMs2, "Acceleration must be greater than zero.");

        TotalDistanceCmModel = request.DistanceCmModel;

        var v0Ms = request.CurrentSpeedKmhPrototype / 3.6;
        var vTargetMs = request.TargetSpeedKmhPrototype / 3.6;

        if (vTargetMs <= v0Ms)
        {
            var nonAcceleratingRequest = request with { DistanceCmModel = Math.Max(0, request.DistanceCmModel) };
            _presetTrajectory = new Trajectory(nonAcceleratingRequest);
            return;
        }

        var accelerationDistancePrototypeM = ((vTargetMs * vTargetMs) - (v0Ms * v0Ms)) / (2.0 * accelerationMs2);
        var accelerationDistanceCmModel = Math.Max(1, (int)Math.Ceiling(accelerationDistancePrototypeM * 100.0 / request.Scale));

        var accelerationRequest = request with { DistanceCmModel = accelerationDistanceCmModel };
        var presetRequest = TrajectoryPresetFactory.Apply(accelerationRequest, preset);
        _presetTrajectory = new Trajectory(presetRequest);
    }

    /// <inheritdoc />
    public double TotalDistanceCmModel { get; }

    /// <inheritdoc />
    public double GetSpeedKmhAtModelDistanceCm(double traveledModelCm)
    {
        return _presetTrajectory.GetSpeedKmhAtModelDistanceCm(traveledModelCm);
    }
}
