// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record GeneratedTrackLeg(
    string FromWaypointId,
    string ToWaypointId,
    int DistanceCm,
    IReadOnlyList<SensorMarker>? SensorMarkers = null);

