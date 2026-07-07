// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using OTD.TrainDriving.RouteControl.Domain;

namespace OTD.TrainDriving.RouteControl.Services;

public interface IRouteDefinitionService
{
    event Action? DefinitionsChanged;


    bool TryGetSegment(string fromWaypointId, string toWaypointId, out RouteSegment segment);

    IReadOnlyCollection<RouteSegment> GetAllSegments();
}

