// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OTD.TrainDriving.RouteModel;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Minimal demo: trajectory request is limited by route permission state and corrected by sensors.
/// </summary>
public static class RouteRuntimeDemo
{
    public static void RunDemo()
    {
        var route = RouteModelPrototype.CreateRouteTable();
        var runtime = new RouteRuntime(route);

        var tick1 = runtime.ApplyStep(deltaCm: 6, trajectorySpeedKmh: 60);
        Console.WriteLine(
            $"Tick1 pos={tick1.EstimatedPositionCm:F1}cm cmd={tick1.EffectiveSpeedKmh} km/h " +
            $"cycleAllowed={tick1.ActiveCycle?.AllowedSpeedKmh} brake={tick1.ActiveCycle?.DriveProfile?.BrakingPreset}");

        var tick2 = runtime.ApplyStep(deltaCm: 12, trajectorySpeedKmh: 60, activatedSensorId: 2);
        Console.WriteLine(
            $"Tick2 pos={tick2.EstimatedPositionCm:F1}cm cmd={tick2.EffectiveSpeedKmh} km/h " +
            $"recal={tick2.PositionRecalibrated} cycleAllowed={tick2.ActiveCycle?.AllowedSpeedKmh}");
    }
}



