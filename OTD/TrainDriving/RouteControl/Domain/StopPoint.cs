// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record StopPoint(
    int OffsetCm,
    string? StopReason = null,
    RouteMetadata? Metadata = null);

