// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OTD.TrainDriving.RouteControl.Builder;
using OTD.TrainDriving.RouteControl.Services;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Minimal demo for RouteControl stop-point behavior.
/// </summary>
public static class RouteRuntimeActionsDemo
{
    public static void RunDemo()
    {
        var legs = new RouteTableBuilder()
            .AddRoute(
                fromWaypointId: "S1",
                toWaypointId: "S2",
                distanceCm: 120,
                maxSpeedKmh: 40)
             .AddRoute(
                fromWaypointId: "S2",
                toWaypointId: "S3",
                distanceCm: 80,
                maxSpeedKmh: 25)
            .AddStopPoint(fromWaypointId: "S2", offsetCm: 15, stopReason: "Bahnsteig")
            .Build();
 
        var service = new RouteTableService();
        service.AddRoutes(legs);
 
        service.AdvancePosition(125);
        var s1 = service.GetRuntimeState();
        Console.WriteLine($"Tick1 pos={s1.HeadPositionCm:F1} active={s1.ActiveFromWaypointId} stop={s1.ActiveStopPoint}");
 
        service.ReleaseGo();
        service.AdvancePosition(140);
        var s2 = service.GetRuntimeState();
        Console.WriteLine($"Tick2 pos={s2.HeadPositionCm:F1} active={s2.ActiveFromWaypointId} stop={s2.ActiveStopPoint}");
    }
}
