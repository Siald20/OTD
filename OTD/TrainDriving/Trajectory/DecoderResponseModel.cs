// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.Trajectory;

/// <summary>
/// Simulates decoder-side speed ramping between commanded target speeds.
/// </summary>
internal sealed class DecoderResponseModel
{
    public DecoderResponseModel(double initialSpeedKmh)
    {
        CurrentSpeedKmh = Math.Max(0.0, initialSpeedKmh);
    }

    /// <summary>
    /// Gets the current decoder-side speed state in km/h.
    /// </summary>
    public double CurrentSpeedKmh { get; private set; }

    public (double AverageSpeedKmh, double EndSpeedKmh) AdvanceTowards(double commandedSpeedKmh, double deltaTimeSeconds)
    {
        var targetSpeedKmh = Math.Max(0.0, commandedSpeedKmh);
        if (deltaTimeSeconds <= 0.0)
            return (CurrentSpeedKmh, CurrentSpeedKmh);

        var startSpeedKmh = CurrentSpeedKmh;
        var endSpeedKmh = targetSpeedKmh;

        CurrentSpeedKmh = endSpeedKmh;
        var averageSpeedKmh = (startSpeedKmh + endSpeedKmh) / 2.0;
        return (averageSpeedKmh, endSpeedKmh);
    }
}


