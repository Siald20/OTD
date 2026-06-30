// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OTD.TrainDriving.RouteControl.Services;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Minimal demo: RouteControl table mutation and runtime state progression.
/// </summary>
public static class RouteRuntimeDemo
{
    public static void RunDemo()
    {
        var service = new RouteTableService();
        service.AddRoutes(RouteControlPrototype.CreateRouteTable());

        service.AdvancePosition(30);
        var s1 = service.GetRuntimeState();
        Console.WriteLine($"Tick1 pos={s1.HeadPositionCm:F1} active={s1.ActiveFromWaypointId} stop={s1.ActiveStopPoint}");

        service.AdvancePosition(130);
        var s2 = service.GetRuntimeState();
        Console.WriteLine($"Tick2 pos={s2.HeadPositionCm:F1} active={s2.ActiveFromWaypointId} stop={s2.ActiveStopPoint}");

        service.ReleaseGo();
        service.AdvancePosition(140);
        var s3 = service.GetRuntimeState();
        Console.WriteLine($"Tick3 pos={s3.HeadPositionCm:F1} active={s3.ActiveFromWaypointId} stop={s3.ActiveStopPoint}");
    }
}
