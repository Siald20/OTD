// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record TrackFeedbackSection(
    string Id,
    int DetectorId,
    string HostTrackId,
    SensorType Type = SensorType.OccupancyDetection,
    string? Description = null);

