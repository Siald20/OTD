// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Runtime;

public sealed record RouteRuntimeState(
    string? ActiveFromWaypointId,
    int? ActiveRouteIndex,
    double HeadPositionCm,
    double TrainLengthCm,
    bool ActiveStopPoint,
    bool SafetyStopInjected,
    bool SensorRecoveryMode,
    int ConsumedRouteCount);

