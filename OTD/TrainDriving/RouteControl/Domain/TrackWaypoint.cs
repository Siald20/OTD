// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record TrackWaypoint(
    string Id,
    string? NodeId = null,
    string? HostTrackId = null,
    int? OffsetCm = null,
    string? Description = null);

