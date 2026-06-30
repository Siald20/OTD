// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record SensorMarker(
    int SensorId,
    int OffsetCm,
    int? ActivationTimeoutMs = null);

