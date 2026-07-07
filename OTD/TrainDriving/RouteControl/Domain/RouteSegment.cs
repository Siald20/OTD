// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record RouteSegment(
    string FromWaypointId,
    string ToWaypointId,
    int LengthCm,
    IReadOnlyDictionary<string, int> MaxSpeedByClassKmh)
{
    public int ResolveSpeedLimitKmh(string speedClass = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speedClass);

        if (MaxSpeedByClassKmh.TryGetValue(speedClass.Trim(), out var byClass) && byClass > 0)
            return byClass;

        if (MaxSpeedByClassKmh.TryGetValue("default", out var byDefault) && byDefault > 0)
            return byDefault;

        var firstPositive = MaxSpeedByClassKmh.Values.FirstOrDefault(value => value > 0);
        if (firstPositive > 0)
            return firstPositive;

        throw new InvalidOperationException(
            $"RouteSegment '{FromWaypointId}<->{ToWaypointId}' does not provide a positive speed limit.");
    }
}

