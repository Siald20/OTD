// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record StopPointOverride(
    bool? Enabled = null,
    int? OffsetCm = null,
    string? StopReason = null,
    RouteMetadata? Metadata = null);

