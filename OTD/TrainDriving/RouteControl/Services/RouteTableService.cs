// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Exceptions;
using OTD.TrainDriving.RouteControl.Runtime;

namespace OTD.TrainDriving.RouteControl.Services;

public sealed class RouteTableService
{
    private const double ConsumeEpsilonCm = 0.1;

    private readonly object _sync = new();
    private readonly List<RouteLeg> _routeLegs = new();

    private Train? _boundTrain;
    private int _version;
    private int _consumedRouteCount;
    private double _headPositionCm;
    private bool _activeStopPoint;
    private string? _activeStopPointFromWaypointId;
    private bool _releasePendingForActiveStopPoint;
    private bool _safetyStopInjected;
    private bool _sensorRecoveryMode;

    public readonly record struct SensorActivationResult(
        bool Accepted,
        double? PreviousHeadPositionCm,
        double? AnchorPositionCm,
        double? CorrectionErrorCm,
        string? ActiveFromWaypointId,
        bool ForcedForwardLegSync);

    public event Action<int>? RouteChanged;

    public void BindTrain(Train train)
    {
        ArgumentNullException.ThrowIfNull(train);
        lock (_sync)
        {
            _boundTrain = train;
            NormalizeRuntimeUnsafe();
            Logging.Info<RouteTableService>("RouteTableService: train binding updated.");
        }
    }

    public void AddRoute(RouteLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);
        Logging.Info<RouteTableService>($"AddRoute: {leg.FromWaypointId}->{leg.ToWaypointId}, dist={leg.DistanceCm}cm, vmax={leg.MaxSpeedKmh:F0}.");
        AddRoutes([leg]);
    }

    public void AddRoutes(IReadOnlyList<RouteLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);
        if (legs.Count == 0)
            return;

        Action<int>? handlers;
        int version;

        lock (_sync)
        {
            Logging.Info<RouteTableService>($"AddRoutes: count={legs.Count}.");
            var merged = new List<RouteLeg>(_routeLegs.Count + legs.Count);
            merged.AddRange(_routeLegs);
            merged.AddRange(NormalizeLegs(legs));
            RouteValidator.ValidateChain(merged);

            _routeLegs.Clear();
            _routeLegs.AddRange(merged);
            NormalizeRuntimeUnsafe();

            version = MarkRouteChangedUnsafe();
            handlers = RouteChanged;
            Logging.Debug<RouteTableService>($"AddRoutes: tableSize={_routeLegs.Count}, version={version}.");
        }

        handlers?.Invoke(version);
    }

    public void ReplaceRoutes(IReadOnlyList<RouteLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);
        if (legs.Count == 0)
            throw new RouteValidationException("ReplaceRoutes requires at least one RouteLeg.");

        Action<int>? handlers;
        int version;

        lock (_sync)
        {
            Logging.Info<RouteTableService>($"ReplaceRoutes: count={legs.Count}.");
            var normalized = NormalizeLegs(legs);
            var startFromWaypointId = normalized[0].FromWaypointId;
            var startIndex = ResolveUniqueIndexUnsafe(startFromWaypointId);
            EnsureRangeMutableUnsafe(startIndex, _routeLegs.Count - 1, "ReplaceRoutes");

            var next = new List<RouteLeg>(_routeLegs.GetRange(0, startIndex));
            next.AddRange(normalized);
            RouteValidator.ValidateChain(next);

            _routeLegs.Clear();
            _routeLegs.AddRange(next);
            NormalizeRuntimeUnsafe();

            version = MarkRouteChangedUnsafe();
            handlers = RouteChanged;
            Logging.Debug<RouteTableService>($"ReplaceRoutes: tableSize={_routeLegs.Count}, version={version}.");
        }

        handlers?.Invoke(version);
    }

    public void ReplaceRouteAtEnd(RouteLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);

        Action<int>? handlers;
        int version;

        lock (_sync)
        {
            Logging.Info<RouteTableService>($"ReplaceRouteAtEnd: {leg.FromWaypointId}->{leg.ToWaypointId}, dist={leg.DistanceCm}cm.");
            if (_routeLegs.Count == 0)
                throw new RouteValidationException("ReplaceRouteAtEnd requires at least one RouteLeg in the table.");

            var index = _routeLegs.Count - 1;
            EnsureRangeMutableUnsafe(index, index, "ReplaceRouteAtEnd");

            var normalized = NormalizeLeg(leg);
            var next = new List<RouteLeg>(_routeLegs);
            next[index] = normalized;
            RouteValidator.ValidateChain(next);

            _routeLegs.Clear();
            _routeLegs.AddRange(next);
            NormalizeRuntimeUnsafe();

            version = MarkRouteChangedUnsafe();
            handlers = RouteChanged;
            Logging.Debug<RouteTableService>($"ReplaceRouteAtEnd: tableSize={_routeLegs.Count}, version={version}.");
        }

        handlers?.Invoke(version);
    }

    public void RemoveRouteAtEnd()
    {
        Action<int>? handlers;
        int version;

        lock (_sync)
        {
            Logging.Info<RouteTableService>("RemoveRouteAtEnd requested.");
            if (_routeLegs.Count == 0)
                throw new RouteValidationException("RemoveRouteAtEnd requires at least one RouteLeg in the table.");

            var index = _routeLegs.Count - 1;
            EnsureRangeMutableUnsafe(index, index, "RemoveRouteAtEnd");
            _routeLegs.RemoveAt(index);
            NormalizeRuntimeUnsafe();

            version = MarkRouteChangedUnsafe();
            handlers = RouteChanged;
            Logging.Debug<RouteTableService>($"RemoveRouteAtEnd: tableSize={_routeLegs.Count}, version={version}.");
        }

        handlers?.Invoke(version);
    }

    public void RemoveRoutesFromWaypoint(string fromWaypointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);

        Action<int>? handlers;
        int version;

        lock (_sync)
        {
            Logging.Info<RouteTableService>($"RemoveRoutesFromWaypoint: from={fromWaypointId}.");
            var startIndex = ResolveUniqueIndexUnsafe(fromWaypointId);
            EnsureRangeMutableUnsafe(startIndex, _routeLegs.Count - 1, "RemoveRoutesFromWaypoint");

            _routeLegs.RemoveRange(startIndex, _routeLegs.Count - startIndex);
            NormalizeRuntimeUnsafe();

            version = MarkRouteChangedUnsafe();
            handlers = RouteChanged;
            Logging.Debug<RouteTableService>($"RemoveRoutesFromWaypoint: tableSize={_routeLegs.Count}, version={version}.");
        }

        handlers?.Invoke(version);
    }

    public void UpdateActiveRouteLeg(string fromWaypointId, int? newDistanceCm = null, double? newMaxSpeedKmh = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);

        Action<int>? handlers;
        int version;

        lock (_sync)
        {
            Logging.Info<RouteTableService>($"UpdateActiveRouteLeg: from={fromWaypointId}, dist={newDistanceCm?.ToString() ?? "-"}, vmax={newMaxSpeedKmh?.ToString("F0") ?? "-"}.");
            var activeIndex = GetActiveIndexUnsafe();
            if (activeIndex is null)
                throw new RouteStateException("No active RouteLeg available for UpdateActiveRouteLeg.");

            var activeLeg = _routeLegs[activeIndex.Value];
            if (!string.Equals(activeLeg.FromWaypointId, fromWaypointId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new RouteUpdateConflictException(
                    $"UpdateActiveRouteLeg can only target active RouteLeg '{activeLeg.FromWaypointId}'.");
            }

            var effectiveDistance = newDistanceCm ?? activeLeg.DistanceCm;
            var effectiveMaxSpeed = newMaxSpeedKmh ?? activeLeg.MaxSpeedKmh;

            if (effectiveDistance <= 0 || effectiveMaxSpeed <= 0)
                throw new RouteValidationException("Updated RouteLeg values must remain > 0.");

            if (effectiveDistance < activeLeg.DistanceCm)
                throw new RouteUpdateConflictException("DistanceCm reduction is not allowed for active RouteLeg updates.");

            if (effectiveMaxSpeed < activeLeg.MaxSpeedKmh)
                throw new RouteUpdateConflictException("MaxSpeedKmh reduction is not allowed for active RouteLeg updates.");

            if (activeLeg.StopPoint is not null && activeLeg.StopPoint.OffsetCm > effectiveDistance)
            {
                throw new RouteValidationException(
                    $"StopPoint offset {activeLeg.StopPoint.OffsetCm} must stay <= RouteLeg distance {effectiveDistance}.");
            }

            _routeLegs[activeIndex.Value] = activeLeg with
            {
                DistanceCm = effectiveDistance,
                MaxSpeedKmh = effectiveMaxSpeed
            };

            RouteValidator.ValidateChain(_routeLegs);
            NormalizeRuntimeUnsafe();

            version = MarkRouteChangedUnsafe();
            handlers = RouteChanged;
            Logging.Debug<RouteTableService>($"UpdateActiveRouteLeg: applied, version={version}.");
        }

        handlers?.Invoke(version);
    }

    public void AdvancePosition(double headPositionCm)
    {
        Action<int>? handlers = null;
        int version = 0;
        var changed = false;

        lock (_sync)
        {
            var previous = _headPositionCm;
            _headPositionCm = Math.Max(0.0, headPositionCm);
            changed = NormalizeRuntimeUnsafe();
            Logging.DebugExtended<RouteTableService>($"AdvancePosition: head={previous:F1}->{_headPositionCm:F1} cm, changed={(changed ? "yes" : "no")}." );
            if (changed)
            {
                version = MarkRouteChangedUnsafe();
                handlers = RouteChanged;
            }
        }

        if (changed)
            handlers?.Invoke(version);
    }

    public void AdvanceByDelta(double deltaCm)
    {
        AdvancePosition(GetRuntimeState().HeadPositionCm + deltaCm);
    }

    public void ReleaseGo()
    {
        Action<int>? handlers = null;
        int version = 0;
        var changed = false;

        lock (_sync)
        {
            if (_activeStopPoint)
            {
                _releasePendingForActiveStopPoint = true;
                _activeStopPoint = false;
                changed = true;
                Logging.Info<RouteTableService>("ReleaseGo: active StopPoint released.");
            }
            else
            {
                Logging.DebugExtended<RouteTableService>("ReleaseGo: ignored (no active StopPoint).");
            }

            if (changed)
            {
                version = MarkRouteChangedUnsafe();
                handlers = RouteChanged;
            }
        }

        if (changed)
            handlers?.Invoke(version);
    }

    public void OnEmergencyStop()
    {
        lock (_sync)
        {
            _safetyStopInjected = true;
            _sensorRecoveryMode = false;
            Logging.Warning<RouteTableService>("EmergencyStop received: SafetyStopInjected=true, SensorRecoveryMode=false.");
        }
    }

    public void OnEmergencyRelease()
    {
        lock (_sync)
        {
            _safetyStopInjected = false;
            _sensorRecoveryMode = true;
            Logging.Warning<RouteTableService>("EmergencyRelease received: SafetyStopInjected=false, SensorRecoveryMode=true.");
        }
    }

    public SensorActivationResult OnSensorActivated(int sensorId)
    {
        Action<int>? handlers = null;
        int version = 0;
        var changed = false;
        SensorActivationResult result;

        lock (_sync)
        {
            var activeIndexBefore = GetActiveIndexUnsafe();
            if (!TryResolveSensorAnchorUnsafe(sensorId, out var anchorCm, out var sensorLegIndex))
            {
                Logging.DebugExtended<RouteTableService>($"Sensor {sensorId}: ignored, no SensorMarker found in route table.");
                return new SensorActivationResult(false, null, null, null, null, false);
            }

            var previousHead = _headPositionCm;
            var forcedForwardLegSync = false;
            var correctedAnchorCm = anchorCm;

            // Sensoren am Beginn des Folge-RouteLeg koennen sonst auf der Leg-Grenze verbleiben.
            if (activeIndexBefore is not null && sensorLegIndex > activeIndexBefore.Value)
            {
                var sensorLegStartCm = GetLegStartPositionUnsafe(sensorLegIndex);
                correctedAnchorCm = Math.Max(correctedAnchorCm, sensorLegStartCm + ConsumeEpsilonCm);
                forcedForwardLegSync = true;
            }

            var correctionError = correctedAnchorCm - previousHead;
            _headPositionCm = Math.Max(0.0, correctedAnchorCm);
            changed = NormalizeRuntimeUnsafe();
            if (!changed)
            {
                // Auch ohne Zustandswechsel soll der Re-Plan unmittelbar greifen.
                changed = true;
            }

            var state = GetRuntimeStateUnsafe();
            Logging.Debug<RouteTableService>(
                $"Sensor {sensorId}: recalibration accepted, pos={previousHead:F1}->{state.HeadPositionCm:F1} cm, " +
                $"error={correctionError:F1} cm, active={state.ActiveFromWaypointId ?? "-"}, forcedForwardLegSync={(forcedForwardLegSync ? "yes" : "no")}.");

            result = new SensorActivationResult(true, previousHead, correctedAnchorCm, correctionError, state.ActiveFromWaypointId, forcedForwardLegSync);

            if (changed)
            {
                version = MarkRouteChangedUnsafe();
                handlers = RouteChanged;
            }
        }

        if (changed)
            handlers?.Invoke(version);

        return result;
    }

    public RouteSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            NormalizeRuntimeUnsafe();
            return RouteSnapshot.From(_routeLegs, GetRuntimeStateUnsafe(), _version);
        }
    }

    public RouteRuntimeState GetRuntimeState()
    {
        lock (_sync)
        {
            NormalizeRuntimeUnsafe();
            return GetRuntimeStateUnsafe();
        }
    }

    private bool NormalizeRuntimeUnsafe()
    {
        if (_routeLegs.Count == 0)
        {
            _headPositionCm = 0;
            _activeStopPoint = false;
            _activeStopPointFromWaypointId = null;
            _releasePendingForActiveStopPoint = false;
            return false;
        }

        var trainLength = ResolveTrainLengthCmUnsafe();
        var changed = ConsumePrefixByTailUnsafe(trainLength);

        var activeIndex = GetActiveIndexUnsafe();
        if (activeIndex is null)
        {
            _activeStopPoint = false;
            _activeStopPointFromWaypointId = null;
            _releasePendingForActiveStopPoint = false;
            return changed;
        }

        var activeLeg = _routeLegs[activeIndex.Value];
        var activeLegStartCm = GetLegStartPositionUnsafe(activeIndex.Value);

        if (!string.Equals(_activeStopPointFromWaypointId, activeLeg.FromWaypointId, StringComparison.OrdinalIgnoreCase))
        {
            _activeStopPoint = false;
            _activeStopPointFromWaypointId = null;
            _releasePendingForActiveStopPoint = false;
        }

        if (activeLeg.StopPoint is null)
            return changed;

        var stopPositionCm = activeLegStartCm + activeLeg.StopPoint.OffsetCm;
        if (_headPositionCm + ConsumeEpsilonCm < stopPositionCm)
            return changed;

        if (_releasePendingForActiveStopPoint)
            return changed;

        _headPositionCm = stopPositionCm;
        _activeStopPoint = true;
        _activeStopPointFromWaypointId = activeLeg.FromWaypointId;
        return true;
    }

    private bool ConsumePrefixByTailUnsafe(double trainLengthCm)
    {
        var changed = false;
        while (_routeLegs.Count > 0)
        {
            var tailPositionCm = _headPositionCm - trainLengthCm;
            if (tailPositionCm + ConsumeEpsilonCm < _routeLegs[0].DistanceCm)
                break;

            var removedDistanceCm = _routeLegs[0].DistanceCm;
            _routeLegs.RemoveAt(0);
            _headPositionCm = Math.Max(0.0, _headPositionCm - removedDistanceCm);
            _consumedRouteCount++;
            _activeStopPoint = false;
            _activeStopPointFromWaypointId = null;
            _releasePendingForActiveStopPoint = false;
            changed = true;
        }

        return changed;
    }

    private int? GetActiveIndexUnsafe()
    {
        if (_routeLegs.Count == 0)
            return null;

        var position = Math.Max(0.0, _headPositionCm);
        var cumulative = 0.0;

        for (var i = 0; i < _routeLegs.Count; i++)
        {
            cumulative += _routeLegs[i].DistanceCm;
            if (position <= cumulative + ConsumeEpsilonCm)
                return i;
        }

        return null;
    }

    private double GetLegStartPositionUnsafe(int routeIndex)
    {
        var cumulative = 0.0;
        for (var i = 0; i < routeIndex; i++)
            cumulative += _routeLegs[i].DistanceCm;

        return cumulative;
    }

    private RouteRuntimeState GetRuntimeStateUnsafe()
    {
        var activeIndex = GetActiveIndexUnsafe();
        var activeFromWaypointId = activeIndex is null ? null : _routeLegs[activeIndex.Value].FromWaypointId;

        return new RouteRuntimeState(
            ActiveFromWaypointId: activeFromWaypointId,
            ActiveRouteIndex: activeIndex,
            HeadPositionCm: _headPositionCm,
            TrainLengthCm: ResolveTrainLengthCmUnsafe(),
            ActiveStopPoint: _activeStopPoint,
            SafetyStopInjected: _safetyStopInjected,
            SensorRecoveryMode: _sensorRecoveryMode,
            ConsumedRouteCount: _consumedRouteCount);
    }

    private static List<RouteLeg> NormalizeLegs(IReadOnlyList<RouteLeg> legs)
    {
        var normalized = new List<RouteLeg>(legs.Count);
        foreach (var leg in legs)
            normalized.Add(NormalizeLeg(leg));

        return normalized;
    }

    private static RouteLeg NormalizeLeg(RouteLeg leg)
    {
        RouteValidator.ValidateLeg(leg);
        return leg with
        {
            FromWaypointId = leg.FromWaypointId.Trim(),
            ToWaypointId = leg.ToWaypointId.Trim()
        };
    }

    private int ResolveUniqueIndexUnsafe(string fromWaypointId)
    {
        var normalized = fromWaypointId.Trim();
        var firstIndex = -1;
        var hits = 0;

        for (var i = 0; i < _routeLegs.Count; i++)
        {
            if (!string.Equals(_routeLegs[i].FromWaypointId, normalized, StringComparison.OrdinalIgnoreCase))
                continue;

            hits++;
            if (firstIndex < 0)
                firstIndex = i;
        }

        if (hits == 0)
            throw new RouteValidationException($"Unknown fromWaypointId '{fromWaypointId}'.");

        if (hits > 1)
            throw new RouteValidationException($"fromWaypointId '{fromWaypointId}' is not unique in RouteTable.");

        return firstIndex;
    }

    private void EnsureRangeMutableUnsafe(int startInclusive, int endInclusive, string operationName)
    {
        var activeIndex = GetActiveIndexUnsafe();
        if (activeIndex is not null && activeIndex.Value >= startInclusive && activeIndex.Value <= endInclusive)
        {
            throw new RouteUpdateConflictException(
                $"{operationName} target contains active RouteLeg '{_routeLegs[activeIndex.Value].FromWaypointId}'.");
        }

        // Consumed RouteLegs are physically removed from table and therefore not addressable.
        if (startInclusive < 0 || endInclusive >= _routeLegs.Count || startInclusive > endInclusive)
            throw new RouteStateException($"Invalid mutable range [{startInclusive}, {endInclusive}] for {operationName}.");
    }

    private double ResolveTrainLengthCmUnsafe()
    {
        if (_boundTrain is null || _boundTrain.Length <= 0)
            return 0.0;

        return _boundTrain.Length / 10.0;
    }

    private bool TryResolveSensorAnchorUnsafe(int sensorId, out double anchorCm, out int sensorLegIndex)
    {
        anchorCm = 0.0;
        sensorLegIndex = -1;
        var cumulative = 0.0;

        for (var legIndex = 0; legIndex < _routeLegs.Count; legIndex++)
        {
            var leg = _routeLegs[legIndex];
            if (leg.SensorMarkers is not null)
            {
                foreach (var marker in leg.SensorMarkers)
                {
                    if (marker.SensorId != sensorId)
                        continue;

                    anchorCm = cumulative + marker.OffsetCm;
                    sensorLegIndex = legIndex;
                    return true;
                }
            }

            cumulative += leg.DistanceCm;
        }

        return false;
    }

    private int MarkRouteChangedUnsafe()
    {
        _version++;
        return _version;
    }
}

