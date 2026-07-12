// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using OTD.TrainDriving.RouteControl.Domain;

namespace OTD.TrainDriving.RouteControl.Services;

public interface IRailwayLayoutService
{
    event Action? DefinitionsChanged;

    bool TryGetGeneratedLeg(string fromWaypointId, string toWaypointId, out GeneratedTrackLeg leg);

    bool TryGetOccupancyFeedback(string occupancyFeedbackId, out OccupancyFeedback occupancyFeedback);

    bool TryGetContactFeedback(string contactFeedbackId, out ContactFeedback contactFeedback);

    bool TryGetWaypoint(string waypointId, out TrackWaypoint waypoint);

    IReadOnlyCollection<GeneratedTrackLeg> GetAllGeneratedLegs();

    IReadOnlyCollection<OccupancyFeedback> GetAllOccupancyFeedbacks();

    IReadOnlyCollection<ContactFeedback> GetAllContactFeedbacks();

    IReadOnlyCollection<TrackWaypoint> GetAllWaypoints();
}

