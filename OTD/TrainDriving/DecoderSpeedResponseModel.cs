// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving;

/// <summary>
/// Simulates decoder-side speed ramping between commanded target speeds.
/// </summary>
internal sealed class DecoderSpeedResponseModel
{
    public DecoderSpeedResponseModel(double initialSpeedKmh)
    {
        CurrentSpeedKmh = Math.Max(0.0, initialSpeedKmh);
    }

    public double CurrentSpeedKmh { get; private set; }

    public (double AverageSpeedKmh, double EndSpeedKmh) AdvanceTowards(double commandedSpeedKmh, double deltaTimeSeconds)
    {
        var targetSpeedKmh = Math.Max(0.0, commandedSpeedKmh);
        if (deltaTimeSeconds <= 0.0)
            return (CurrentSpeedKmh, CurrentSpeedKmh);

        var startSpeedKmh = CurrentSpeedKmh;
        var endSpeedKmh = targetSpeedKmh;

        CurrentSpeedKmh = endSpeedKmh;
        return ((startSpeedKmh + endSpeedKmh) / 2.0, endSpeedKmh);
    }


}

