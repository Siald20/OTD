// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using OTD.TrainDriving.RouteControl.Domain;

namespace OTD.TrainDriving.RouteControl.Services;

public interface IRailwayLayoutService
{
    event Action? DefinitionsChanged;

    bool TryGetGeneratedLeg(string fromWaypointId, string toWaypointId, out GeneratedTrackLeg leg);

    bool TryGetSection(string sectionId, out TrackFeedbackSection section);

    bool TryGetPoint(string pointId, out TrackFeedbackPoint point);

    bool TryGetWaypoint(string waypointId, out TrackWaypoint waypoint);

    IReadOnlyCollection<GeneratedTrackLeg> GetAllGeneratedLegs();

    IReadOnlyCollection<TrackFeedbackSection> GetAllSections();

    IReadOnlyCollection<TrackFeedbackPoint> GetAllPoints();

    IReadOnlyCollection<TrackWaypoint> GetAllWaypoints();
}

