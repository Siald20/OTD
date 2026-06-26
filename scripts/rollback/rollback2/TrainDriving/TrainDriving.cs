// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl;
using OTD.TrainDriving.Presets;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving;

/// <summary>
/// Master-side train driving orchestration.
/// 
/// Responsibilities:
/// - Create speed trajectories (distance-based, with scale support)
/// - Wrap trajectories in executors for cyclic application to Train
/// 
/// Train acts as slave: receives only speed commands, no trajectory logic.
/// </summary>
public class TrainDriving
{
    // Vorläufig fix; wird später aus globaler App-Konfiguration gelesen.
    private const int DefaultScale = 87;
    private static readonly TimeSpan DefaultSpeedStepInterval = TimeSpan.FromMilliseconds(500);
    // Vorläufige Decoder-Rampenzeit in Sekunden (0 deaktiviert das jeweilige Limit).
    private const double DefaultAccelerationRampTimeSeconds = 0.5;
    private const double DefaultBrakingRampTimeSeconds = 0.5;

    private readonly Train? _train;

    /// <summary>
    /// Preset fuer Beschleunigungsphasen (currentSpeed &lt; targetSpeed).
    /// Kann zur Laufzeit geaendert werden.
    /// </summary>
    public AccelerationTrajectoryPreset AccelerationPreset { get; set; } = AccelerationTrajectoryPreset.Linear;

    /// <summary>
    /// Preset fuer Bremsenphasen (currentSpeed &gt; targetSpeed).
    /// Kann zur Laufzeit geaendert werden.
    /// </summary>
    public BrakingTrajectoryPreset BrakingPreset { get; set; } = BrakingTrajectoryPreset.Linear;

    public TrainDriving()
    {
    }

    public TrainDriving(Train train)
    {
        _train = train ?? throw new ArgumentNullException(nameof(train));
    }

    /// <summary>
    /// Convenience-API: fährt eine lineare Trajektorie über die angegebene Distanz.
    /// Train muss dafür über den Konstruktor gebunden sein.
    /// </summary>
    public void Drive(int currentSpeed, int targetSpeed, int distance)
    {
        DriveAsync(currentSpeed, targetSpeed, distance).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Convenience-API: fährt eine lineare Trajektorie über die angegebene Distanz.
    /// Train muss dafür über den Konstruktor gebunden sein.
    /// </summary>
    public async Task DriveAsync(int currentSpeed, int targetSpeed, int distance,
        CancellationToken cancellationToken = default)
    {
        if (_train is null)
            throw new InvalidOperationException(
                "DriveAsync requires a bound train. Create TrainDriving with new TrainDriving(train).");

        var request = new DrivingTrajectoryRequest(
            CurrentSpeedKmhPrototype: currentSpeed,
            TargetSpeedKmhPrototype: targetSpeed,
            DistanceCmModel: distance,
            Scale: DefaultScale,
            VMaxKmhPrototype: _train.VMax);

        ISpeedTrajectory trajectory;
        if (targetSpeed > currentSpeed)
        {
            var presetRequest = TrajectoryPresetFactory.Apply(request, AccelerationPreset);
            trajectory = new ParametricTrajectory(presetRequest);
        }
        else
        {
            var presetRequest = TrajectoryPresetFactory.Apply(request, BrakingPreset);
            trajectory = new ParametricTrajectory(presetRequest);
        }
        var executor = CreateExecutor(_train, trajectory);
        // Modelliert die Nachlaufdynamik des Decoders zwischen Soll- und Ist-Geschwindigkeit.
        var decoderResponse = new DecoderSpeedResponseModel(
            initialSpeedKmh: request.CurrentSpeedKmhPrototype,
            vmaxKmhPrototype: request.VMaxKmhPrototype.GetValueOrDefault(),
            accelerationRampTimeSeconds: DefaultAccelerationRampTimeSeconds,
            brakingRampTimeSeconds: DefaultBrakingRampTimeSeconds);
        // Initiales Solltempo am Startpunkt der Trajektorie.
        var commandedSpeedKmh = trajectory.GetSpeedKmhAtModelDistanceCm(0.0);

        var targetDistanceCm = request.DistanceCmModel;
        var traveledCm = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastTick = stopwatch.Elapsed;

        while (traveledCm < targetDistanceCm)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(DefaultSpeedStepInterval, cancellationToken).ConfigureAwait(false);

            var now = stopwatch.Elapsed;
            var dtSeconds = (now - lastTick).TotalSeconds;
            lastTick = now;

            // Der Decoder erreicht Sollwerte nur mit endlicher Rampe (CV3/CV4).
            var speedResponse = decoderResponse.AdvanceTowards(commandedSpeedKmh, dtSeconds);
            var speedForIntegrationKmh = speedResponse.AverageSpeedKmh;

            var deltaCmExact = ModelCmPerSecondFromPrototypeKmh(speedForIntegrationKmh, request.Scale) * dtSeconds;
            var deltaCm = (int)Math.Round(deltaCmExact, MidpointRounding.AwayFromZero);

            // Bei 0->x Beschleunigung darf der Integrator nicht im Stillstand festhaengen.
            var acceleratingFromStandstill =
                request.CurrentSpeedKmhPrototype == 0 && request.TargetSpeedKmhPrototype > 0;

            if (deltaCm == 0 && (speedForIntegrationKmh > 0 || acceleratingFromStandstill) && traveledCm < targetDistanceCm)
                deltaCm = 1;

            traveledCm = Math.Min(traveledCm + deltaCm, targetDistanceCm);
            // Neues Solltempo am aktualisierten Wegpunkt.
            commandedSpeedKmh = trajectory.GetSpeedKmhAtModelDistanceCm(traveledCm);

            await executor.ExecuteAtDistanceAsync(traveledCm, cancellationToken).ConfigureAwait(false);
        }
    }

    private static double ModelCmPerSecondFromPrototypeKmh(double speedKmhPrototype, double scale)
    {
        var prototypeMetersPerSecond = speedKmhPrototype / 3.6;
        return prototypeMetersPerSecond * 100.0 / scale;
    }

    /// <summary>
    /// Creates a linear (constant acceleration/deceleration) trajectory.
    /// </summary>
    public ISpeedTrajectory CreateLinearTrajectory(DrivingTrajectoryRequest request)
    {
        return CreatePresetTrajectory(request, TrajectoryPreset.Linear);
    }

    /// <summary>
    /// Creates a parametric trajectory that can be bent via curve type and optional control point.
    /// </summary>
    public ISpeedTrajectory CreateParametricTrajectory(DrivingTrajectoryRequest request)
    {
        return new ParametricTrajectory(request);
    }

    /// <summary>
    /// Creates a parametric trajectory from a named preset.
    /// </summary>
    public ISpeedTrajectory CreatePresetTrajectory(DrivingTrajectoryRequest request, TrajectoryPreset preset)
    {
        var presetRequest = TrajectoryPresetFactory.Apply(request, preset);
        return new ParametricTrajectory(presetRequest);
    }

    /// <summary>
    /// Creates an aggressive braking trajectory (early deceleration).
    /// </summary>
    public ISpeedTrajectory CreateAggressiveBrakeTrajectory(DrivingTrajectoryRequest request)
    {
        return CreatePresetTrajectory(request, TrajectoryPreset.AggressiveBrake);
    }

    /// <summary>
    /// Creates a late-brake trajectory (hold speed, then brake sharply).
    /// </summary>
    public ISpeedTrajectory CreateLateBrakeTrajectory(DrivingTrajectoryRequest request)
    {
        return CreatePresetTrajectory(request, TrajectoryPreset.LateBrake);
    }

    /// <summary>
    /// Creates a comfort-curve trajectory (smooth ease-in/ease-out).
    /// </summary>
    public ISpeedTrajectory CreateComfortCurveTrajectory(DrivingTrajectoryRequest request)
    {
        return CreatePresetTrajectory(request, TrajectoryPreset.Comfort);
    }

    /// <summary>
    /// Creates an executor that applies a trajectory to a train.
    /// 
    /// Usage (in a cycle):
    ///   await executor.ExecuteAtDistanceAsync(traveledCm);
    /// </summary>
    public TrajectoryExecutor CreateExecutor(Train train, ISpeedTrajectory trajectory)
    {
        return new TrajectoryExecutor(train, trajectory);
    }

    /// <summary>
    /// Convenience: creates and wraps a linear trajectory in one call.
    /// </summary>
    public TrajectoryExecutor CreateLinearExecutor(Train train, DrivingTrajectoryRequest request)
    {
        var trajectory = CreateLinearTrajectory(request);
        return CreateExecutor(train, trajectory);
    }
}
