// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Collections.ObjectModel;
using OTD.TrainDriving.RouteControl.Domain;

namespace OTD.TrainDriving.RouteControl.Runtime;

public sealed record RouteSnapshot(
    IReadOnlyList<RouteLeg> RouteLegs,
    RouteRuntimeState RuntimeState,
    int Version)
{
    public static RouteSnapshot From(
        IReadOnlyList<RouteLeg> routeLegs,
        RouteRuntimeState runtimeState,
        int version)
    {
        return new RouteSnapshot(
            RouteLegs: new ReadOnlyCollection<RouteLeg>(new List<RouteLeg>(routeLegs)),
            RuntimeState: runtimeState,
            Version: version);
    }
}

