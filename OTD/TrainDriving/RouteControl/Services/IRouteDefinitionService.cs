// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace OTD.TrainDriving.RouteControl.Services;

public interface IRouteDefinitionService
{
    event Action? DefinitionsChanged;

    bool TryGetLeg(string fromWaypointId, string toWaypointId, out StaticRouteLegData legData);

    IReadOnlyCollection<StaticRouteLegData> GetAllLegs();
}

