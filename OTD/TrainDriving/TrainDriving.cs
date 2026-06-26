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
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.Presets;
using OTD.TrainDriving.RouteModel;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving;

public readonly record struct TrainDrivingProgressTick(
    int DeltaCmModel,
    int CommandedSpeedKmhPrototype);

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
    private static readonly TimeSpan DefaultMinSpeedStepInterval = TimeSpan.FromMilliseconds(100);
    private const double DefaultAccelerationMs2 = 0.55;
    private const double DefaultBrakePointCorrectionPercent = 0.0;
    private const double DefaultBrakePointCorrectionPercentPerVMax  = 0.0;
    private const double DefaultSpeedCurveFidelityPercent = 60.0;

    /// <summary>
    /// Optional diagnostics event emitted once per control tick.
    /// </summary>
    public event Action<TrainDrivingProgressTick>? ProgressTick;

    /// <summary>
    /// Preset for acceleration phases (currentSpeed &lt; targetSpeed). Can be changed at runtime.
    /// Shapes the start-oriented acceleration curve while <see cref="AccelerationMs2"/> controls the absolute acceleration strength.
    /// </summary>
    public AccelerationTrajectoryPreset AccelerationPreset { get; set; } = AccelerationTrajectoryPreset.Linear;

    /// <summary>
    /// Preset for braking phases (currentSpeed &gt; targetSpeed). Can be changed at runtime.
    /// </summary>
    public BrakingTrajectoryPreset BrakingPreset { get; set; } = BrakingTrajectoryPreset.Linear;

    /// <summary>
    /// Effective train acceleration in m/s² for start-oriented acceleration phases.
    /// This value is interpreted in prototype units and converted to model scale internally.
    /// REMINDER: In the long term this value should be derived automatically from <c>Train</c>/<c>TrainComposition</c>
    /// data such as operating mass and locomotive power instead of being set manually.
    /// </summary>
    public double AccelerationMs2 { get; set; } = DefaultAccelerationMs2;

    /// <summary>
    /// Enables adaptive speed-step timing. If enabled, small expected speed changes
    /// are sent with a shorter interval for smoother motion, while larger changes
    /// use longer intervals up to <see cref="MaxSpeedStepInterval"/>.
    /// </summary>
    public bool UseAdaptiveSpeedStepInterval { get; set; } = true;

    /// <summary>
    /// Fidelity of the speed curve in percent (0..100) for dynamic interval shortening.
    /// Applies to both acceleration and braking phases.
    /// 0 = no dynamic adjustment (always MaxSpeedStepInterval),
    /// 100 = maximum curve fidelity (down to MinSpeedStepInterval).
    /// </summary>
    public double SpeedCurveFidelityPercent { get; set; } = DefaultSpeedCurveFidelityPercent;

    /// <summary>
    /// Upper bound for command loop interval (network/DCC load cap).
    /// </summary>
    public TimeSpan MaxSpeedStepInterval { get; set; } = DefaultSpeedStepInterval;

    /// <summary>
    /// Lower bound for command loop interval (prevents excessive command rate).
    /// </summary>
    public TimeSpan MinSpeedStepInterval { get; set; } = DefaultMinSpeedStepInterval;


    /// <summary>
    /// Brake point correction in percent of the total braking distance.
    /// Negative values move the brake point earlier (shorter braking distance, stops sooner).
    /// Positive values move the brake point later (longer braking distance, stops later).
    /// 
    /// Example: -5 for a 200 cm brake distance means the effective target is 190 cm (5% earlier).
    /// </summary>
    public double BrakePointCorrectionPercent { get; set; } = DefaultBrakePointCorrectionPercent;

    /// <summary>
    /// Additional brake point correction (percent) scaled by current speed relative to VMax.
    /// At VMax, this value is fully applied; at lower speeds, it scales proportionally.
    /// 
    /// Example: -5 means at VMax the brake point is moved 5% earlier (additional to BrakePointCorrectionPercent).
    /// At 50% VMax, the additional correction is 2.5%.
    /// </summary>
    public double BrakePointCorrectionPercentPerVMax { get; set; } = DefaultBrakePointCorrectionPercentPerVMax;

    /// <summary>
    /// If enabled, emits focused diagnostics for braking runs (command drop start and per-tick progress).
    /// </summary>
    public bool BrakeDebugLogging { get; set; }

    private readonly Train? _train;

    private int? _lastLoggedAdaptiveIntervalMs;

    /// <summary>
    /// Creates an unbound instance without a train. Use factory methods to create trajectories manually.
    /// </summary>
    public TrainDriving()
    {
    }

    /// <summary>
    /// Creates an instance bound to the given train for convenience drive methods.
    /// </summary>
    /// <param name="train">The train that receives speed commands as a slave.</param>
    public TrainDriving(Train train)
    {
        _train = train ?? throw new ArgumentNullException(nameof(train));
    }

    public void Accelerate(int targetSpeed)
    {
        AccelerateAsync(targetSpeed).GetAwaiter().GetResult();
    }

    public void Brake(int targetSpeed, int distance)
    {
        BrakeAsync(targetSpeed, distance).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Synchronously executes one route cycle.
    /// Allowed speed and optional presets are taken from the cycle start waypoint.
    /// </summary>
    /// <param name="cycle">The active route cycle providing distance and start-waypoint permission.</param>
    public void DriveRouteCycle(RouteCycle cycle)
    {
        DriveRouteCycleAsync(cycle)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Accelerates from the train's current speed (<see cref="Train.SpeedV"/>) to <paramref name="targetSpeed"/>.
    /// The acceleration distance is derived from <see cref="AccelerationPreset"/> and <see cref="AccelerationMs2"/>.
    /// </summary>
    public async Task AccelerateAsync(
        int targetSpeed,
        CancellationToken cancellationToken = default)
    {
        var train = GetBoundTrain();
        var currentSpeed = Math.Max(0, train.SpeedV);
        targetSpeed = Math.Max(0, targetSpeed);

        if (targetSpeed < currentSpeed)
            throw new InvalidOperationException(
                $"AccelerateAsync requires targetSpeed >= current speed ({currentSpeed} km/h).");

        if (targetSpeed == currentSpeed)
            return;

        var distanceCm = EstimateAccelerationDistanceCm(
            currentSpeed,
            targetSpeed,
            DefaultScale,
            AccelerationMs2);

        await DriveDistanceAsync(currentSpeed, targetSpeed, distanceCm, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Brakes (or coasts for equal speed) from the train's current speed (<see cref="Train.SpeedV"/>)
    /// to <paramref name="targetSpeed"/> over <paramref name="distance"/> model cm.
    /// </summary>
    public async Task BrakeAsync(
        int targetSpeed,
        int distance,
        CancellationToken cancellationToken = default)
    {
        var train = GetBoundTrain();
        var currentSpeed = Math.Max(0, train.SpeedV);
        targetSpeed = Math.Max(0, targetSpeed);

        if (distance < 0)
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "Distance must be >= 0 model cm.");

        if (targetSpeed > currentSpeed)
            throw new InvalidOperationException(
                $"BrakeAsync requires targetSpeed <= current speed ({currentSpeed} km/h).");

        if (distance == 0)
        {
            if (targetSpeed != currentSpeed)
                await train.SetSpeedVAsync(targetSpeed, cancellationToken).ConfigureAwait(false);
            return;
        }

        Logging.Info(LogCategory.TrainDriving, $"Sending brake command: BrakeAsync(targetSpeed={targetSpeed}, distance={distance} cm)");
        await DriveDistanceAsync(currentSpeed, targetSpeed, distance, cancellationToken).ConfigureAwait(false);
    }

    private async Task DriveDistanceAsync(
        int currentSpeed,
        int targetSpeed,
        int distance,
        CancellationToken cancellationToken = default)
    {
        var train = GetBoundTrain();

        // Berechne die korrigierte Zielentfernung basierend auf Prozentuale Haltepunkt-Korrektur
        var brakePointCorrectionPercent = Math.Clamp(BrakePointCorrectionPercent, -100.0, 100.0);
        
        // Geschwindigkeitsabhängige zusätzliche Korrektur
        var speedRatio = Math.Clamp((double)currentSpeed / train.VMax, 0.0, 1.0);
        var speedDependentCorrectionPercent = BrakePointCorrectionPercentPerVMax * speedRatio;
        var totalCorrectionPercent = brakePointCorrectionPercent + speedDependentCorrectionPercent;
        
        var correctedTargetDistanceCm = (double)distance * (1.0 + totalCorrectionPercent / 100.0);
        correctedTargetDistanceCm = Math.Clamp(correctedTargetDistanceCm, 1.0, (double)distance + 100.0);
        var targetDistanceCm = (int)Math.Round(correctedTargetDistanceCm, MidpointRounding.AwayFromZero);

        // Use the corrected target distance for the trajectory itself so the generated speed profile
        // and the stopping point stay aligned and the final fallback command does not create a large jump.
        var request = new DrivingTrajectoryRequest(
            CurrentSpeedKmhPrototype: currentSpeed,
            TargetSpeedKmhPrototype: targetSpeed,
            DistanceCmModel: targetDistanceCm,
            Scale: DefaultScale,
            VMaxKmhPrototype: train.VMax);

        var trajectory = CreateSinglePhaseTrajectory(request);
        var executor = CreateExecutor(train, trajectory);

        _lastLoggedAdaptiveIntervalMs = null;
        Logging.Debug(LogCategory.TrainDriving,
            $"DriveDistance start: current={currentSpeed} km/h, target={targetSpeed} km/h, distance={distance} cm, " +
            $"correctedDistance={correctedTargetDistanceCm:F1} cm (base={brakePointCorrectionPercent:F1}% + speed-dependent={speedDependentCorrectionPercent:F2}% @ {speedRatio*100:F0}% VMax = total {totalCorrectionPercent:F1}%), " +
            $"adaptive={(UseAdaptiveSpeedStepInterval ? "on" : "off")}, min={MinSpeedStepInterval.TotalMilliseconds:F0} ms, " +
            $"max={MaxSpeedStepInterval.TotalMilliseconds:F0} ms");

        // Modelliert die Nachlaufdynamik des Decoders zwischen Soll- und Ist-Geschwindigkeit.
        var decoderResponse = new DecoderSpeedResponseModel(
            initialSpeedKmh: request.CurrentSpeedKmhPrototype);
        // Initiales Solltempo am Startpunkt der Trajektorie.
        var commandedSpeedKmh = trajectory.GetSpeedKmhAtModelDistanceCm(0.0);

        var traveledCm = 0.0;
        int? lastSentCommandedSpeedRoundedKmh = null;
        var lastIntegratedSpeedKmh = (double)request.CurrentSpeedKmhPrototype;
        var isBrakingManeuver = request.TargetSpeedKmhPrototype < request.CurrentSpeedKmhPrototype;

        int? lastBrakeCommandedSpeedRoundedKmh = null;
        var initialCommandedSpeedRoundedKmh = (int)Math.Round(commandedSpeedKmh, MidpointRounding.AwayFromZero);
        var brakeCommandDropLogged = false;
        var stopwatch = Stopwatch.StartNew();
        var lastTick = stopwatch.Elapsed;

        if (isBrakingManeuver)
        {
            // Send first reduced braking command immediately.
            var previewSpeedKmh = trajectory.GetSpeedKmhAtModelDistanceCm(traveledCm);
            var previewSpeedRoundedKmh = (int)Math.Round(previewSpeedKmh, MidpointRounding.AwayFromZero);

            if (previewSpeedRoundedKmh < initialCommandedSpeedRoundedKmh)
            {
                await executor.ExecuteAtDistanceAsync(traveledCm, cancellationToken).ConfigureAwait(false);
                lastSentCommandedSpeedRoundedKmh = previewSpeedRoundedKmh;
                commandedSpeedKmh = previewSpeedKmh;
                brakeCommandDropLogged = true;

                Logging.Debug(LogCategory.TrainDriving,
                    $"Brake immediate command: traveled={traveledCm}/{targetDistanceCm} cm, " +
                    $"cmd={initialCommandedSpeedRoundedKmh}->{previewSpeedRoundedKmh} km/h");
            }
        }

        while (traveledCm < targetDistanceCm - 0.001)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stepInterval = ComputeSpeedStepInterval(
                traveledCm,
                targetDistanceCm,
                request.Scale,
                trajectory,
                commandedSpeedKmh,
                lastIntegratedSpeedKmh,
                lastSentCommandedSpeedRoundedKmh);

                Logging.Debug(LogCategory.TrainDriving,
                    $"Loop tick: traveled={traveledCm}/{targetDistanceCm} cm, cmd={commandedSpeedKmh:F1} km/h, " +
                    $"vInt={lastIntegratedSpeedKmh:F1} km/h, delay={stepInterval.TotalMilliseconds:F0} ms");

            await Task.Delay(stepInterval, cancellationToken).ConfigureAwait(false);

            var now = stopwatch.Elapsed;
            var dtSeconds = (now - lastTick).TotalSeconds;
            lastTick = now;

            // Aktuell wird kein separater Decoder-Rampennachlauf modelliert.
            var speedResponse = decoderResponse.AdvanceTowards(commandedSpeedKmh, dtSeconds);
            var speedForIntegrationKmh = speedResponse.AverageSpeedKmh;

            lastIntegratedSpeedKmh = speedForIntegrationKmh;

            var deltaCmExact = ModelCmPerSecondFromPrototypeKmh(speedForIntegrationKmh, request.Scale) * dtSeconds;
            var deltaCm = deltaCmExact;

            // Bei 0->x Beschleunigung darf der Integrator nicht im Stillstand festhaengen.
            var acceleratingFromStandstill =
                request.CurrentSpeedKmhPrototype == 0 &&
                request.TargetSpeedKmhPrototype > 0;

            // Bei Bremsfahrt auf Halt darf die Schleife nicht im 0-km/h-Abschnitt stehenbleiben,
            // sonst kommt das Modell nie bis zum Zielpunkt.
            var brakingToStopAtStandstill =
                isBrakingManeuver &&
                request.TargetSpeedKmhPrototype == 0 &&
                lastBrakeCommandedSpeedRoundedKmh.HasValue &&
                lastBrakeCommandedSpeedRoundedKmh.Value == 0 &&
                traveledCm < targetDistanceCm - 0.001;

            if (Math.Abs(deltaCm) < 0.5 &&
                (speedForIntegrationKmh > 0 || acceleratingFromStandstill || brakingToStopAtStandstill) &&
                traveledCm < targetDistanceCm - 0.001)
                deltaCm = 1.0;

            traveledCm = Math.Min(traveledCm + deltaCm, (double)targetDistanceCm);
            // Neues Solltempo am aktualisierten Wegpunkt.
            var commandDistanceCm = traveledCm;

            commandedSpeedKmh = trajectory.GetSpeedKmhAtModelDistanceCm(commandDistanceCm);

            var commandedSpeedRoundedKmh = (int)Math.Round(commandedSpeedKmh, MidpointRounding.AwayFromZero);
            if (isBrakingManeuver)
            {
                if (lastBrakeCommandedSpeedRoundedKmh.HasValue)
                    commandedSpeedRoundedKmh =
                        Math.Min(commandedSpeedRoundedKmh, lastBrakeCommandedSpeedRoundedKmh.Value);

                lastBrakeCommandedSpeedRoundedKmh = commandedSpeedRoundedKmh;
                commandedSpeedKmh = commandedSpeedRoundedKmh;
            }

            if (isBrakingManeuver && !brakeCommandDropLogged &&
                commandedSpeedRoundedKmh < initialCommandedSpeedRoundedKmh)
            {
                brakeCommandDropLogged = true;
                Logging.Debug(LogCategory.TrainDriving,
                    $"Brake command drop begins: traveled={traveledCm}/{targetDistanceCm} cm, " +
                    $"cmd={initialCommandedSpeedRoundedKmh}->{commandedSpeedRoundedKmh} km/h, dt={dtSeconds * 1000.0:F0} ms");
            }

            RaiseProgressTick(new TrainDrivingProgressTick(
                DeltaCmModel: (int)Math.Round(deltaCm, MidpointRounding.AwayFromZero),
                CommandedSpeedKmhPrototype: Math.Max(0, commandedSpeedRoundedKmh)));

            // Keep processing every tick, but avoid sending duplicate speed commands.
            var shouldSendCommand = !lastSentCommandedSpeedRoundedKmh.HasValue ||
                                    lastSentCommandedSpeedRoundedKmh.Value != commandedSpeedRoundedKmh;

            if (shouldSendCommand)
            {
                var executeDistanceCm = isBrakingManeuver ? commandDistanceCm : traveledCm;
                await executor.ExecuteAtDistanceAsync(executeDistanceCm, cancellationToken).ConfigureAwait(false);
                lastSentCommandedSpeedRoundedKmh = commandedSpeedRoundedKmh;

                Logging.Debug(LogCategory.TrainDriving,
                    $"Speed command sent: speed={commandedSpeedRoundedKmh} km/h, traveled={traveledCm}/{targetDistanceCm} cm, " +
                    $"delay={stepInterval.TotalMilliseconds:F0} ms, dt={dtSeconds * 1000.0:F0} ms, vInt={speedForIntegrationKmh:F1} km/h");
            }

            if (isBrakingManeuver && BrakeDebugLogging)
            {
                var remainingCm = Math.Max(0, targetDistanceCm - traveledCm);
                Logging.Debug(LogCategory.TrainDriving,
                    $"Brake tick: traveled={traveledCm}/{targetDistanceCm} cm, remaining={remainingCm} cm, " +
                    $"cmd={commandedSpeedRoundedKmh} km/h, vInt={speedForIntegrationKmh:F1} km/h, " +
                    $"deltaCm={deltaCm}, send={(shouldSendCommand ? "yes" : "no")}, train.SpeedV={train.SpeedV} km/h");
            }
        }

        if (isBrakingManeuver && targetSpeed == 0)
        {
            if (lastSentCommandedSpeedRoundedKmh != targetSpeed)
            {
                await train.SetSpeedVAsync(targetSpeed, cancellationToken).ConfigureAwait(false);
                lastSentCommandedSpeedRoundedKmh = targetSpeed;

                Logging.Debug(LogCategory.TrainDriving,
                    $"Brake final command sent: speed={targetSpeed} km/h, traveled={traveledCm}/{targetDistanceCm} cm");
            }
            else if (BrakeDebugLogging)
            {
                Logging.Debug(LogCategory.TrainDriving,
                    $"Brake final command already active: speed={targetSpeed} km/h, traveled={traveledCm}/{targetDistanceCm} cm");
            }
        }

        if (isBrakingManeuver && targetSpeed == 0)
        {
            Logging.Info(LogCategory.TrainDriving,
                $"Braking completed: reached {targetSpeed} km/h over {traveledCm:F0}/{targetDistanceCm} cm.");
        }

        Logging.Debug(LogCategory.TrainDriving,
            $"DriveDistance finished: traveled={traveledCm}/{targetDistanceCm} cm, finalCommand={lastSentCommandedSpeedRoundedKmh ?? targetSpeed} km/h");
    }

    private TimeSpan ComputeSpeedStepInterval(
        double traveledCm,
        int targetDistanceCm,
        int scale,
        ISpeedTrajectory trajectory,
        double currentCommandedSpeedKmh,
        double currentIntegratedSpeedKmh,
        int? lastSentCommandedSpeedRoundedKmh)
    {
        _ = scale;
        _ = lastSentCommandedSpeedRoundedKmh;

        var maxInterval = MaxSpeedStepInterval;
        var minInterval = MinSpeedStepInterval;

        if (maxInterval <= TimeSpan.Zero)
            maxInterval = DefaultSpeedStepInterval;
        if (minInterval <= TimeSpan.Zero)
            minInterval = DefaultMinSpeedStepInterval;
        if (minInterval > maxInterval)
            minInterval = maxInterval;

        if (!UseAdaptiveSpeedStepInterval)
            return maxInterval;

        if (traveledCm >= targetDistanceCm - 0.001)
            return minInterval;

        // 0..100 => 0..1; 0 bedeutet statisches Intervall (Max).
        var fidelity = Math.Clamp(SpeedCurveFidelityPercent, 0.0, 100.0) / 100.0;
        if (fidelity <= 0.0)
            return maxInterval;

        // Lokale Kurvensteilheit abschaetzen: |dv/ds| in km/h pro cm.
        var sampleCm = Math.Max(1.0, targetDistanceCm * 0.01); // 1% der Strecke, min 1 cm
        var s0 = Math.Max(0.0, traveledCm - sampleCm);
        var s1 = Math.Min((double)targetDistanceCm, traveledCm + sampleCm);
        var v0 = trajectory.GetSpeedKmhAtModelDistanceCm(s0);
        var v1 = trajectory.GetSpeedKmhAtModelDistanceCm(s1);
        var slopeKmhPerCm = Math.Abs(v1 - v0) / Math.Max(0.001, s1 - s0);

        // Normierung auf praxisnahen Referenzwert und nichtlineare Kennlinie.
        const double referenceSlopeKmhPerCm = 0.25;
        var slopeNorm = Math.Clamp(slopeKmhPerCm / referenceSlopeKmhPerCm, 0.0, 1.0);
        var curveBlend = Math.Pow(slopeNorm, 2.0); // sanft bei kleiner Steilheit, aggressiver bei steiler Kurve

        // Bei hoher Treue + steiler Kurve Intervall verkuerzen (bis Min).
        var shortenBlend = fidelity * curveBlend;
        var intervalMsDouble = maxInterval.TotalMilliseconds -
                               (maxInterval.TotalMilliseconds - minInterval.TotalMilliseconds) * shortenBlend;
        var clampedInterval = TimeSpan.FromMilliseconds(intervalMsDouble);


        var intervalMs = (int)Math.Round(clampedInterval.TotalMilliseconds, MidpointRounding.AwayFromZero);
        if (!_lastLoggedAdaptiveIntervalMs.HasValue || _lastLoggedAdaptiveIntervalMs.Value != intervalMs)
        {
            _lastLoggedAdaptiveIntervalMs = intervalMs;
            Logging.Debug(LogCategory.TrainDriving,
                $"Adaptive interval updated: {intervalMs} ms (fidelity={fidelity * 100.0:F0}%, " +
                $"slope={slopeKmhPerCm:F3} km/h/cm, curveBlend={curveBlend:F2}, " +
                $"cmd={currentCommandedSpeedKmh:F1} km/h, vInt={currentIntegratedSpeedKmh:F1} km/h)");
        }

        return clampedInterval;
    }


    /// <summary>
    /// Executes one route cycle using the start-waypoint permission and cycle distance.
    /// If a drive profile exists on the cycle's start waypoint, it temporarily overrides
    /// AccelerationPreset/BrakingPreset for this call.
    /// AccelerationMs2 is global and is not overridden by route cycle drive profiles.
    /// </summary>
    /// <param name="cycle">The route cycle providing distance, allowed speed, and optional drive profile.</param>
    /// <param name="cancellationToken">Cancellation token to abort the drive.</param>
    public async Task DriveRouteCycleAsync(
        RouteCycle cycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        var distanceCm = (int)Math.Round(Math.Max(0.0, cycle.DistanceCm), MidpointRounding.AwayFromZero);
        var targetSpeedKmh = Math.Max(0, cycle.AllowedSpeedKmh);
        var currentSpeedKmh = Math.Max(0, GetBoundTrain().SpeedV);

        var originalAccelerationPreset = AccelerationPreset;
        var originalBrakingPreset = BrakingPreset;

        // AccelerationPreset override from route drive profile.
        if (cycle.DriveProfile?.AccelerationPreset is not null)
        {
            var accelerationPreset = cycle.DriveProfile.AccelerationPreset.Value;
            AccelerationPreset = accelerationPreset;
        }

        if (cycle.DriveProfile?.BrakingPreset is not null)
        {
            var brakingPreset = cycle.DriveProfile.BrakingPreset.Value;
            BrakingPreset = brakingPreset;
        }

        try
        {
            if (targetSpeedKmh > currentSpeedKmh)
            {
                await AccelerateAsync(
                    targetSpeed: targetSpeedKmh,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await BrakeAsync(
                    targetSpeed: targetSpeedKmh,
                    distance: distanceCm,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            AccelerationPreset = originalAccelerationPreset;
            BrakingPreset = originalBrakingPreset;
        }
    }

    private Train GetBoundTrain()
    {
        if (_train is null)
            throw new InvalidOperationException(
                "TrainDriving requires a bound train. Create TrainDriving with new TrainDriving(train).");
        return _train;
    }

    private static int EstimateAccelerationDistanceCm(
        int currentSpeedKmhPrototype,
        int targetSpeedKmhPrototype,
        int scale,
        double accelerationMs2)
    {
        if (targetSpeedKmhPrototype <= currentSpeedKmhPrototype)
            return 0;

        if (accelerationMs2 <= 0)
            return 1;

        var v0 = currentSpeedKmhPrototype / 3.6;
        var v1 = targetSpeedKmhPrototype / 3.6;
        var distanceMetersPrototype = (v1 * v1 - v0 * v0) / (2.0 * accelerationMs2);
        var distanceCmModel = distanceMetersPrototype * 100.0 / scale;
        return Math.Max(1, (int)Math.Round(distanceCmModel, MidpointRounding.AwayFromZero));
    }

    private void RaiseProgressTick(TrainDrivingProgressTick tick)
    {
        var handlers = ProgressTick;
        if (handlers is null)
            return;

        foreach (var subscribedDelegate in handlers.GetInvocationList())
        {
            if (subscribedDelegate is not Action<TrainDrivingProgressTick> handler)
                continue;

            try
            {
                handler(tick);
            }
            catch (Exception ex)
            {
                Logging.Error(LogCategory.TrainDriving,
                    $"ProgressTick handler failed: {ex.Message}");
            }
        }
    }

    private static double ModelCmPerSecondFromPrototypeKmh(double speedKmhPrototype, double scale)
    {
        var prototypeMetersPerSecond = speedKmhPrototype / 3.6;
        return prototypeMetersPerSecond * 100.0 / scale;
    }

    private ISpeedTrajectory CreateSinglePhaseTrajectory(DrivingTrajectoryRequest request)
    {
        if (request.TargetSpeedKmhPrototype > request.CurrentSpeedKmhPrototype)
        {
            return new StartOrientedAccelerationTrajectory(request, AccelerationPreset, AccelerationMs2);
        }

        var brakingRequest = TrajectoryPresetFactory.Apply(request, BrakingPreset);
        return new ParametricTrajectory(brakingRequest);
    }

    /// <summary>
    /// Creates a linear (constant acceleration/deceleration) trajectory.
    /// </summary>
    public ISpeedTrajectory CreateLinearTrajectory(DrivingTrajectoryRequest request)
    {
        var linearRequest = request with
        {
            CurveType = TrajectoryCurveType.Linear,
            ControlPoint = null,
            CurveShapePercent = 50.0
        };
        return new ParametricTrajectory(linearRequest);
    }

    /// <summary>
    /// Creates a parametric trajectory that can be bent via curve type and optional control point.
    /// </summary>
    public ISpeedTrajectory CreateParametricTrajectory(DrivingTrajectoryRequest request)
    {
        return new ParametricTrajectory(request);
    }

    /// <summary>
    /// Creates an aggressive braking trajectory (early deceleration).
    /// </summary>
    public ISpeedTrajectory CreateAggressiveBrakeTrajectory(DrivingTrajectoryRequest request)
    {
        var brakingRequest = TrajectoryPresetFactory.Apply(request, BrakingTrajectoryPreset.AggressiveBrake);
        return new ParametricTrajectory(brakingRequest);
    }

    /// <summary>
    /// Creates an early-brake trajectory (moderate early deceleration with rollout).
    /// </summary>
    public ISpeedTrajectory CreateEarlyBrakeTrajectory(DrivingTrajectoryRequest request)
    {
        var brakingRequest = TrajectoryPresetFactory.Apply(request, BrakingTrajectoryPreset.EarlyBrake);
        return new ParametricTrajectory(brakingRequest);
    }

    /// <summary>
    /// Creates a late-brake trajectory (hold speed, then brake sharply).
    /// </summary>
    public ISpeedTrajectory CreateLateBrakeTrajectory(DrivingTrajectoryRequest request)
    {
        var brakingRequest = TrajectoryPresetFactory.Apply(request, BrakingTrajectoryPreset.LateBrake);
        return new ParametricTrajectory(brakingRequest);
    }

    /// <summary>
    /// Creates a comfort-curve trajectory (smooth ease-in/ease-out).
    /// </summary>
    public ISpeedTrajectory CreateComfortCurveTrajectory(DrivingTrajectoryRequest request)
    {
        var brakingRequest = TrajectoryPresetFactory.Apply(request, BrakingTrajectoryPreset.Comfort);
        return new ParametricTrajectory(brakingRequest);
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


