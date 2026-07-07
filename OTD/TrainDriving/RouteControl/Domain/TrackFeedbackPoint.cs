// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record TrackFeedbackPoint(
    string Id,
    int DetectorId,
    string HostTrackId,
    int OffsetCm,
    SensorType Type = SensorType.TrackContact,
    string? Description = null);

