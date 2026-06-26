// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving;

/// <summary>
/// Simulates decoder-side speed ramping between commanded target speeds.
/// </summary>
internal sealed class DecoderSpeedResponseModel
{
    /// <summary>
    /// Initializes the decoder response model with the current speed and an optional response rate.
    /// </summary>
    /// <param name="initialSpeedKmh">Initial decoder speed in km/h.</param>
    /// <param name="decoderAverageBias">
    /// Bias for the step-average speed in the range -0.5..+0.5.
    /// - Negative values move the average towards the start speed (more inertia).
    /// - Positive values move the average towards the target speed (more aggressive response).
    /// - 0 keeps the classic arithmetic mean between start and end speed.
    /// </param>
    private readonly double _decoderAverageBias;

    public DecoderSpeedResponseModel(double initialSpeedKmh, double decoderAverageBias = 0.0)
    {
        CurrentSpeedKmh = Math.Max(0.0, initialSpeedKmh);
        _decoderAverageBias = Math.Clamp(decoderAverageBias, -0.5, 0.5);
    }

    /// <summary>
    /// Gets the current decoder-side speed state in km/h.
    /// </summary>
    public double CurrentSpeedKmh { get; private set; }

    /// <summary>
    /// Advances the decoder model towards a commanded speed for one simulation step.
    /// </summary>
    /// <param name="commandedSpeedKmh">Commanded target speed in km/h.</param>
    /// <param name="deltaTimeSeconds">Elapsed simulation time for this step in seconds.</param>
    /// <returns>
    /// Tuple of average speed over the step and resulting end speed, both in km/h.
    /// </returns>
    public (double AverageSpeedKmh, double EndSpeedKmh) AdvanceTowards(double commandedSpeedKmh, double deltaTimeSeconds)
    {
        var targetSpeedKmh = Math.Max(0.0, commandedSpeedKmh);
        if (deltaTimeSeconds <= 0.0)
            return (CurrentSpeedKmh, CurrentSpeedKmh);

        var startSpeedKmh = CurrentSpeedKmh;
        var endSpeedKmh = targetSpeedKmh;

        CurrentSpeedKmh = endSpeedKmh;
        var blend = Math.Clamp(0.5 + _decoderAverageBias, 0.0, 1.0);
        var averageSpeedKmh = startSpeedKmh + (endSpeedKmh - startSpeedKmh) * blend;
        return (averageSpeedKmh, endSpeedKmh);
    }


}

