// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OTD.TrainDriving.Profiles;

namespace OTD.TrainDriving;

/// <summary>
/// Simulates decoder-side speed ramping between commanded target speeds.
/// </summary>
internal sealed class DecoderSpeedResponseModel
{
    private readonly double? _maxAccelerationKmhPerSecond;
    private readonly double? _maxBrakingKmhPerSecond;

    public DecoderSpeedResponseModel(double initialSpeedKmh, TrajectoryDelayConfig? delayConfig, int vmaxKmhPrototype)
    {
        CurrentSpeedKmh = Math.Max(0.0, initialSpeedKmh);

        // Ohne Delay-Konfiguration oder ohne gültiges VMax wirkt kein dynamisches Limit.
        if (delayConfig is null || vmaxKmhPrototype <= 0)
        {
            _maxAccelerationKmhPerSecond = null;
            _maxBrakingKmhPerSecond = null;
            return;
        }

        var accelerationLimitMs2 = delayConfig.GetAccelerationLimitMps2(vmaxKmhPrototype);
        var brakingLimitMs2 = delayConfig.GetBrakingLimitMps2(vmaxKmhPrototype);

        _maxAccelerationKmhPerSecond = accelerationLimitMs2.HasValue ? accelerationLimitMs2.Value * 3.6 : null;
        _maxBrakingKmhPerSecond = brakingLimitMs2.HasValue ? brakingLimitMs2.Value * 3.6 : null;
    }

    public double CurrentSpeedKmh { get; private set; }

    public (double AverageSpeedKmh, double EndSpeedKmh) AdvanceTowards(double commandedSpeedKmh, double deltaTimeSeconds)
    {
        var targetSpeedKmh = Math.Max(0.0, commandedSpeedKmh);
        if (deltaTimeSeconds <= 0.0)
            return (CurrentSpeedKmh, CurrentSpeedKmh);

        var startSpeedKmh = CurrentSpeedKmh;
        double endSpeedKmh;

        if (targetSpeedKmh > startSpeedKmh)
        {
            // Beschleunigungsrampe anwenden.
            endSpeedKmh = ApplyPositiveRamp(startSpeedKmh, targetSpeedKmh, deltaTimeSeconds, _maxAccelerationKmhPerSecond);
        }
        else if (targetSpeedKmh < startSpeedKmh)
        {
            // Bremsrampe anwenden.
            endSpeedKmh = ApplyNegativeRamp(startSpeedKmh, targetSpeedKmh, deltaTimeSeconds, _maxBrakingKmhPerSecond);
        }
        else
        {
            endSpeedKmh = targetSpeedKmh;
        }

        CurrentSpeedKmh = endSpeedKmh;
        return ((startSpeedKmh + endSpeedKmh) / 2.0, endSpeedKmh);
    }

    private static double ApplyPositiveRamp(double start, double target, double dtSeconds, double? limitKmhPerSecond)
    {
        if (!limitKmhPerSecond.HasValue)
            return target;

        var maxDelta = limitKmhPerSecond.Value * dtSeconds;
        return Math.Min(target, start + maxDelta);
    }

    private static double ApplyNegativeRamp(double start, double target, double dtSeconds, double? limitKmhPerSecond)
    {
        if (!limitKmhPerSecond.HasValue)
            return target;

        var maxDelta = limitKmhPerSecond.Value * dtSeconds;
        return Math.Max(target, start - maxDelta);
    }
}

