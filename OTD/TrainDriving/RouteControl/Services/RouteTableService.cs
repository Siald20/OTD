// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Exceptions;
using OTD.TrainDriving.RouteControl.Runtime;

namespace OTD.TrainDriving.RouteControl.Services;


public sealed class RouteTableService
{
    private const double ConsumeEpsilonCm = 0.1;
    private const int DefaultScale = 87;

    private readonly object _sync = new();
    private readonly List<RouteLeg> _routeLegs = new();
    private readonly FeedbackTracking _feedbackTracking = new();

    private Train? _boundTrain;
    private int _version;
    private int _consumedRouteCount;
    private double _headPositionCm;
    private bool _activeStopPoint;
    private string? _activeStopPointFromWaypointId;
    private bool _releasePendingForActiveStopPoint;
    private bool _safetyStopInjected;
    private bool _feedbackInputRecoveryMode;
    private double? _stuckAlertBaseAnchorCm;
    private bool _stuckAlertTriggeredForCurrentBase;
    private bool _stuckAlertSuppressedForCurrentBase;
    private long? _stuckAlertMonitoringStartedTimestamp;

    public readonly record struct FeedbackInputActivationResult(
        bool Accepted,
        double? PreviousHeadPositionCm,
        double? AnchorPositionCm,
        double? CorrectionErrorCm,
        string? ActiveFromWaypointId,
        bool ForcedForwardLegSync,
        bool EmergencyStopRequested,
        int? ExpectedFeedbackInputId,
        double? ExpectedFeedbackInputAnchorCm,
        double? ActivatedFeedbackInputAnchorCm,
        double? UnexpectedDeltaToExpectedCm,
        bool IsExpectedFeedbackInputActivatedEarly);

    public readonly record struct StuckAlertResult(
        double BaseAnchorCm,
        int ExpectedFeedbackInputId,
        double ExpectedFeedbackInputAnchorCm,
        double HeadPositionCm,
        double SegmentDistanceCm,
        double AllowedOverrunCm,
        double OverrunCm,
        string? ActiveFromWaypointId);

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

    public void UpdateActiveRouteLeg(string fromWaypointId, int? newDistanceCm = null, int? newMaxSpeedKmh = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);

        Action<int>? handlers;
        int version;

        lock (_sync)
        {
            Logging.Info<RouteTableService>($"UpdateActiveRouteLeg: from={fromWaypointId}, dist={newDistanceCm?.ToString() ?? "-"}, vmax={newMaxSpeedKmh?.ToString() ?? "-"}.");
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

    public StuckAlertResult? AdvancePosition(double headPositionCm, bool suppressStuckAlert = false)
    {
        Action<int>? handlers = null;
        int version = 0;
        bool changed;
        StuckAlertResult? stuckAlert = null;

        lock (_sync)
        {
            var previous = _headPositionCm;
            _headPositionCm = Math.Max(0.0, headPositionCm);
            if (!suppressStuckAlert)
                EnsureStuckAlertMonitoringStartedUnsafe(previous);
            changed = NormalizeRuntimeUnsafe();
            if (!suppressStuckAlert)
                stuckAlert = TryEvaluateStuckAlertUnsafe();

            if (stuckAlert is not null)
                changed = true;

            Logging.DebugExtended<RouteTableService>($"AdvancePosition: head={previous:F1}->{_headPositionCm:F1} cm, changed={(changed ? "yes" : "no")}." );
            if (changed)
            {
                version = MarkRouteChangedUnsafe();
                handlers = RouteChanged;
            }
        }

        if (changed)
            handlers?.Invoke(version);

        return stuckAlert;
    }

    public StuckAlertResult? AdvanceByDelta(double deltaCm, bool suppressStuckAlert = false)
    {
        return AdvancePosition(GetRuntimeState().HeadPositionCm + deltaCm, suppressStuckAlert);
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
                _stuckAlertMonitoringStartedTimestamp = null;
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
            _feedbackInputRecoveryMode = false;
            _stuckAlertMonitoringStartedTimestamp = null;
            Logging.Warning<RouteTableService>("EmergencyStop received: SafetyStopInjected=true, FeedbackInputRecoveryMode=false.");
        }
    }

    public void OnEmergencyRelease()
    {
        lock (_sync)
        {
            _safetyStopInjected = false;
            _feedbackInputRecoveryMode = true;
            _stuckAlertMonitoringStartedTimestamp = null;
            Logging.Warning<RouteTableService>("EmergencyRelease received: SafetyStopInjected=false, FeedbackInputRecoveryMode=true.");
        }
    }

    public FeedbackInputActivationResult OnFeedbackInputActivated(int feedbackId, double? estimatedHeadPositionCm = null)
    {
        Action<int>? handlers = null;
        int version = 0;
        bool changed;
        FeedbackInputActivationResult result;

        lock (_sync)
        {
            // Nur Feedbacken des aktiven Legs akzeptieren. Zukünftige Feedbacken werden ignoriert.
            var activeIndexBefore = GetActiveIndexUnsafe(estimatedHeadPositionCm);
            if (activeIndexBefore is null)
            {
                Logging.DebugExtended<RouteTableService>($"Feedback {feedbackId}: ignored, no active leg.");
                return new FeedbackInputActivationResult(false, null, null, null, null, false, false, null, null, null, null, false);
            }

            if (!TryResolveFeedbackAnchorUnsafe(feedbackId, estimatedHeadPositionCm, out var anchorCm, out var feedbackLegIndex))
            {
                Logging.DebugExtended<RouteTableService>($"Feedback {feedbackId}: ignored, not in active leg.");
                return new FeedbackInputActivationResult(false, null, null, null, null, false, false, null, null, null, null, false);
            }

            var headPositionForUnexpectedCheck = Math.Max(0.0, estimatedHeadPositionCm ?? _headPositionCm);
            if (RouteControlSafetyOptions.EnableUnexpectedAheadFeedbackInputEmergencyStop)
            {
                var lookAhead = ResolveFeedbackLookAheadPositionUnsafe(headPositionForUnexpectedCheck);
                if (_feedbackTracking.TryMatchUnexpectedAheadInput(
                    feedbackId,
                    anchorCm,
                    headPositionForUnexpectedCheck,
                    lookAhead.SearchAnchorCm,
                    _routeLegs,
                    Math.Max(0.0, RouteControlSafetyOptions.UnexpectedAheadFeedbackInputToleranceCm),
                    lookAhead.TrainLengthCm,
                    out var unexpectedMatch))
                {
                    var state = GetRuntimeStateUnsafe();
                    Logging.Warning<RouteTableService>(
                        $"Feedback {feedbackId}: unexpected ahead feedback detected, active={state.ActiveFromWaypointId ?? "-"}, " +
                        $"head={headPositionForUnexpectedCheck:F1}cm, activated={unexpectedMatch.ActivatedAnchorCm:F1}cm, " +
                        $"expectedInput={unexpectedMatch.ExpectedInput.InputId}@{unexpectedMatch.ExpectedInput.AnchorCm:F1}cm, " +
                        (unexpectedMatch.IsExpectedInputActivatedEarly
                            ? $"headToFeedbackInput={unexpectedMatch.HeadToInputDistanceCm:F1}cm (expected feedback input activated too early)."
                            : $"delta={unexpectedMatch.DeltaToExpectedCm:F1}cm."));
                    return new FeedbackInputActivationResult(
                        false,
                        headPositionForUnexpectedCheck,
                        anchorCm,
                        null,
                        state.ActiveFromWaypointId,
                        false,
                        true,
                        unexpectedMatch.ExpectedInput.InputId,
                        unexpectedMatch.ExpectedInput.AnchorCm,
                        unexpectedMatch.ActivatedAnchorCm,
                        unexpectedMatch.DeltaToExpectedCm,
                        unexpectedMatch.IsExpectedInputActivatedEarly);
                }
            }

            // Normalfall: nur Feedbacken des aktuell aktiven Legs zulassen.
            // Startup-Recovery: Falls noch nie kalibriert wurde, akzeptiere einmalig auch
            // ein Feedback aus einem bereits vorgerueckten Leg, um die erste Synchronisation
            // trotz frueher Fremd-/Heck-Feedbacks zu ermoeglichen.
            if (feedbackLegIndex < activeIndexBefore.Value)
            {
                var allowInitialRecoverySync = !_stuckAlertBaseAnchorCm.HasValue;
                if (!allowInitialRecoverySync)
                {
                    var state = GetRuntimeStateUnsafe();
                    Logging.DebugExtended<RouteTableService>(
                        $"Feedback {feedbackId}: ignored, not in current active leg (feedbackLegIndex={feedbackLegIndex}, activeIndex={activeIndexBefore.Value}), active={state.ActiveFromWaypointId ?? "-"}.");
                    return new FeedbackInputActivationResult(false, null, null, null, null, false, false, null, null, null, null, false);
                }

                Logging.Debug<RouteTableService>(
                    $"Feedback {feedbackId}: startup recovery sync allowed (feedbackLegIndex={feedbackLegIndex}, activeIndex={activeIndexBefore.Value}, head={Math.Max(0.0, estimatedHeadPositionCm ?? _headPositionCm):F1}cm).");
            }

            // Duplikat-Schutz: Nur einmal pro aktivem Waypoint kalibrieren.
            if (_feedbackTracking.IsAlreadyCalibratedForCurrentWaypoint(feedbackId))
            {
                var state = GetRuntimeStateUnsafe();
                Logging.DebugExtended<RouteTableService>(
                    $"Feedback {feedbackId}: ignored, already calibrated for active waypoint {state.ActiveFromWaypointId ?? "-"}.");
                return new FeedbackInputActivationResult(false, null, null, null, null, false, false, null, null, null, null, false);
            }

            var previousHead = estimatedHeadPositionCm is null ? _headPositionCm : Math.Max(0.0, estimatedHeadPositionCm.Value);
            var forcedForwardLegSync = false;
            var correctedAnchorCm = anchorCm;

            // Feedbacken am Beginn des Folge-RouteLeg koennen sonst auf der Leg-Grenze verbleiben.
            if (feedbackLegIndex > activeIndexBefore.Value)
            {
                var feedbackLegStartCm = GetLegStartPositionUnsafe(feedbackLegIndex);
                if (ShouldReanchorAgainstToAlongTransitionUnsafe(activeIndexBefore.Value, feedbackLegIndex))
                {
                    // Bei Richtungswechsel AgainstLine->AlongLine:
                    // Die Stuck-Guard-Berechnung muss neu starten mit dem neuen Leg-Kontext.
                    // Setze BaseAnchor auf null, damit die Stuck-Guard nach diesem Feedback neu initialisiert wird.
                    _stuckAlertBaseAnchorCm = null;
                }
                else
                {
                    correctedAnchorCm = Math.Max(correctedAnchorCm, feedbackLegStartCm + ConsumeEpsilonCm);
                }
                forcedForwardLegSync = true;
            }

            var correctionError = correctedAnchorCm - previousHead;
            _headPositionCm = Math.Max(0.0, correctedAnchorCm);
            _feedbackTracking.MarkCalibrated(feedbackId);
            
            // StuckAlertBaseAnchor wird bei jedem Feedback neu gesetzt.
            // Bei Richtungswechsel AgainstLine->AlongLine wird damit der Kontext auf den neuen Leg ausgerichtet.
            _stuckAlertBaseAnchorCm = correctedAnchorCm;
            
            _stuckAlertTriggeredForCurrentBase = false;
            _stuckAlertSuppressedForCurrentBase = false;
            _stuckAlertMonitoringStartedTimestamp = Stopwatch.GetTimestamp();
            changed = NormalizeRuntimeUnsafe();
            if (!changed)
            {
                // Auch ohne Zustandswechsel soll der Re-Plan unmittelbar greifen.
                changed = true;
            }

            var state2 = GetRuntimeStateUnsafe();
            Logging.Debug<RouteTableService>(
                $"Feedback {feedbackId}: recalibration accepted, pos={previousHead:F1}->{state2.HeadPositionCm:F1} cm, " +
                $"error={correctionError:F1} cm, active={state2.ActiveFromWaypointId ?? "-"}, forcedForwardLegSync={(forcedForwardLegSync ? "yes" : "no")}.");

            result = new FeedbackInputActivationResult(
                true,
                previousHead,
                correctedAnchorCm,
                correctionError,
                state2.ActiveFromWaypointId,
                forcedForwardLegSync,
                false,
                null,
                null,
                correctedAnchorCm,
                null,
                false);

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
            _stuckAlertBaseAnchorCm = null;
            _stuckAlertTriggeredForCurrentBase = false;
            _stuckAlertSuppressedForCurrentBase = false;
            _stuckAlertMonitoringStartedTimestamp = null;
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
            _stuckAlertMonitoringStartedTimestamp = null;
            return changed;
        }

        if (!_stuckAlertBaseAnchorCm.HasValue)
            return changed;

        var activeLeg = _routeLegs[activeIndex.Value];
        var activeLegStartCm = GetLegStartPositionUnsafe(activeIndex.Value);

        if (!string.Equals(_activeStopPointFromWaypointId, activeLeg.FromWaypointId, StringComparison.OrdinalIgnoreCase))
        {
            _activeStopPoint = false;
            _activeStopPointFromWaypointId = null;
            _releasePendingForActiveStopPoint = false;
            _stuckAlertSuppressedForCurrentBase = false;
            _feedbackTracking.UpdateActiveWaypoint(activeLeg.FromWaypointId);
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
        _stuckAlertMonitoringStartedTimestamp = null;
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
            if (_stuckAlertBaseAnchorCm.HasValue)
                _stuckAlertBaseAnchorCm = Math.Max(0.0, _stuckAlertBaseAnchorCm.Value - removedDistanceCm);
            _consumedRouteCount++;
            _activeStopPoint = false;
            _activeStopPointFromWaypointId = null;
            _releasePendingForActiveStopPoint = false;
            changed = true;
        }


        return changed;
    }


    private int? GetActiveIndexUnsafe(double? headPositionOverrideCm = null)
    {
        if (_routeLegs.Count == 0)
            return null;

        var position = Math.Max(0.0, headPositionOverrideCm ?? _headPositionCm);
        var cumulative = 0.0;

        for (var i = 0; i < _routeLegs.Count; i++)
        {
            cumulative += _routeLegs[i].DistanceCm;
            if (position <= cumulative + ConsumeEpsilonCm)
                return i;
        }

        return null;
    }

    private StuckAlertResult? TryEvaluateStuckAlertUnsafe()
    {
        if (!RouteControlSafetyOptions.EnableStuckAlertEmergencyStop)
            return null;

        if (_safetyStopInjected || _activeStopPoint || _routeLegs.Count == 0 || _stuckAlertSuppressedForCurrentBase)
            return null;

        if (!_stuckAlertBaseAnchorCm.HasValue)
            return null;

        var currentHeadPositionCm = Math.Max(0.0, _headPositionCm);
        var lookAhead = ResolveFeedbackLookAheadPositionUnsafe(currentHeadPositionCm);
        var baseAnchorCm = lookAhead.BaseAnchorCm;
        var searchAnchorCm = lookAhead.SearchAnchorCm;

        if (!_feedbackTracking.TryGetNextExpectedInputAhead(_routeLegs, searchAnchorCm, lookAhead.TrainLengthCm, out var expected))
            return null;

        var segmentDistanceCm = expected.AnchorCm - baseAnchorCm;
        if (segmentDistanceCm <= 0.0)
            return null;

        var tolerancePercent = Math.Max(0.0, RouteControlSafetyOptions.StuckAlertTolerancePercent);
        var slackFactor = tolerancePercent / 100.0;
        var allowedOverrunCm = ResolveAllowedStuckAlertOverrunCm(segmentDistanceCm, slackFactor);
        var alertThresholdCm = expected.AnchorCm + allowedOverrunCm;
        var overrunCm = currentHeadPositionCm - expected.AnchorCm;
        var requiredElapsedSeconds = ResolveRequiredStuckAlertElapsedSecondsUnsafe(segmentDistanceCm, slackFactor);
        var elapsedSeconds = _stuckAlertMonitoringStartedTimestamp is { } startedTimestamp
            ? Math.Max(0.0, (Stopwatch.GetTimestamp() - startedTimestamp) / (double)Stopwatch.Frequency)
            : 0.0;

        if (Math.Abs(currentHeadPositionCm - expected.AnchorCm) <= ConsumeEpsilonCm)
        {
            _stuckAlertSuppressedForCurrentBase = true;
            Logging.Debug<RouteTableService>(
                $"StuckAlert suppressed: target input {expected.InputId}@{expected.AnchorCm:F1}cm was already active at monitoring start.");
            return null;
        }

        if (currentHeadPositionCm <= alertThresholdCm || _stuckAlertTriggeredForCurrentBase)
            return null;

        if (requiredElapsedSeconds is not null && elapsedSeconds <= requiredElapsedSeconds.Value)
            return null;

        _stuckAlertTriggeredForCurrentBase = true;
        _safetyStopInjected = true;
        _feedbackInputRecoveryMode = false;

        var state = GetRuntimeStateUnsafe();
        var result = new StuckAlertResult(
            BaseAnchorCm: baseAnchorCm,
            ExpectedFeedbackInputId: expected.InputId,
            ExpectedFeedbackInputAnchorCm: expected.AnchorCm,
            HeadPositionCm: currentHeadPositionCm,
            SegmentDistanceCm: segmentDistanceCm,
            AllowedOverrunCm: allowedOverrunCm,
            OverrunCm: overrunCm,
            ActiveFromWaypointId: state.ActiveFromWaypointId);

        Logging.Warning<RouteTableService>(
            $"StuckAlert: expectedInput={expected.InputId}@{expected.AnchorCm:F1}cm, head={currentHeadPositionCm:F1}cm, " +
            $"base={baseAnchorCm:F1}cm, segment={segmentDistanceCm:F1}cm, tolerancePercent={tolerancePercent:F1}, " +
            $"allowedOverrun={allowedOverrunCm:F1}cm, overrun={overrunCm:F1}cm, elapsed={elapsedSeconds:F3}s, " +
            $"requiredElapsed={(requiredElapsedSeconds is null ? "-" : $"{requiredElapsedSeconds.Value:F3}s")}, active={state.ActiveFromWaypointId ?? "-"}.");

        return result;
    }

    private void EnsureStuckAlertMonitoringStartedUnsafe(double previousHeadPositionCm)
    {
        if (!_stuckAlertBaseAnchorCm.HasValue)
            return;

        if (_stuckAlertMonitoringStartedTimestamp.HasValue)
            return;

        if (_headPositionCm <= previousHeadPositionCm + ConsumeEpsilonCm)
            return;

        _stuckAlertMonitoringStartedTimestamp = Stopwatch.GetTimestamp();
    }

    private double? ResolveRequiredStuckAlertElapsedSecondsUnsafe(double segmentDistanceCm, double slackFactor)
    {
        if (segmentDistanceCm <= 0.0)
            return null;

        var monitoringSpeedKmh = ResolveStuckAlertMonitoringSpeedKmhUnsafe();
        if (monitoringSpeedKmh <= 0.0)
            return null;

        var monitoringSpeedCmPerSecond = ModelCmPerSecondFromPrototypeKmh(monitoringSpeedKmh);
        if (monitoringSpeedCmPerSecond <= 0.0)
            return null;

        var effectiveOverrunCm = ResolveAllowedStuckAlertOverrunCm(segmentDistanceCm, slackFactor);
        return effectiveOverrunCm / monitoringSpeedCmPerSecond;
    }

    private static double ResolveAllowedStuckAlertOverrunCm(double segmentDistanceCm, double slackFactor)
    {
        if (segmentDistanceCm <= 0.0)
            return 0.0;

        // Distanz-Overrun = (Segmentlänge × Prozent) + hartes Minimum von 15 cm.
        // Diese physische Zusatzstrecke gilt sowohl für die Distanzschwelle als auch
        // – über v = s / t – für die daraus abgeleitete Zeitschwelle.
        const double minimumAllowedOverrunCm = 15.0;
        return (segmentDistanceCm * slackFactor) + minimumAllowedOverrunCm;
    }

    private double ResolveStuckAlertMonitoringSpeedKmhUnsafe()
    {
        var trainSpeedKmh = Math.Max(0, _boundTrain?.SpeedV ?? 0);
        if (trainSpeedKmh > 0)
            return trainSpeedKmh;

        var activeIndex = GetActiveIndexUnsafe();
        if (activeIndex is null)
            return 0.0;

        var activeLegMaxSpeedKmh = _routeLegs[activeIndex.Value].MaxSpeedKmh;
        if (activeLegMaxSpeedKmh <= 0 || activeLegMaxSpeedKmh == int.MaxValue)
            return 0.0;

        return activeLegMaxSpeedKmh;
    }

    private static double ModelCmPerSecondFromPrototypeKmh(double speedKmhPrototype)
        => (speedKmhPrototype / 3.6) * 100.0 / DefaultScale;

    private bool ShouldReanchorAgainstToAlongTransitionUnsafe(int activeLegIndex, int feedbackLegIndex)
    {
        if (activeLegIndex < 0 || feedbackLegIndex < 0)
            return false;

        if (feedbackLegIndex != activeLegIndex + 1)
            return false;

        if (feedbackLegIndex >= _routeLegs.Count)
            return false;

        return _routeLegs[activeLegIndex].TravelDirection == RouteTravelDirection.AgainstLine &&
               _routeLegs[feedbackLegIndex].TravelDirection == RouteTravelDirection.AlongLine;
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
            FeedbackInputRecoveryMode: _feedbackInputRecoveryMode,
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

    private (double TrainLengthCm, double TailPositionCm, double BaseAnchorCm, double SearchAnchorCm) ResolveFeedbackLookAheadPositionUnsafe(double headPositionCm)
    {
        var normalizedHeadPositionCm = Math.Max(0.0, headPositionCm);
        var trainLengthCm = ResolveTrainLengthCmUnsafe();
        var tailPositionCm = Math.Max(0.0, normalizedHeadPositionCm - trainLengthCm);
        var baseAnchorCm = Math.Max(0.0, _stuckAlertBaseAnchorCm ?? 0.0);
        var searchAnchorCm = Math.Max(baseAnchorCm, tailPositionCm);
        return (trainLengthCm, tailPositionCm, baseAnchorCm, searchAnchorCm);
    }

    private bool TryResolveFeedbackAnchorUnsafe(int feedbackId, double? headPositionOverrideCm, out double anchorCm, out int feedbackLegIndex)
    {
        anchorCm = 0.0;
        feedbackLegIndex = -1;
        var cumulative = 0.0;
        var candidates = new List<(double AnchorCm, int LegIndex)>();

        for (var legIndex = 0; legIndex < _routeLegs.Count; legIndex++)
        {
            var leg = _routeLegs[legIndex];
            if (leg.FeedbackInputActivationPoints is not null)
            {
                foreach (var marker in leg.FeedbackInputActivationPoints)
                {
                    if (marker.FeedbackId != feedbackId)
                        continue;

                    candidates.Add((cumulative + marker.OffsetCm, legIndex));
                }
            }

            cumulative += leg.DistanceCm;
        }

        if (candidates.Count == 0)
            return false;

        if (candidates.Count == 1)
        {
            anchorCm = candidates[0].AnchorCm;
            feedbackLegIndex = candidates[0].LegIndex;
            return true;
        }

        var activeIndex = GetActiveIndexUnsafe(headPositionOverrideCm);
        var headPosition = Math.Max(0.0, headPositionOverrideCm ?? _headPositionCm);

        // Prefer anchors on the active leg or immediate neighbor, then pick nearest by distance.
        var bestScore = double.MaxValue;
        var best = candidates[0];

        foreach (var candidate in candidates)
        {
            var legPenalty = 0.0;
            if (activeIndex is not null)
            {
                var legDistance = Math.Abs(candidate.LegIndex - activeIndex.Value);
                legPenalty = legDistance switch
                {
                    0 => 0.0,
                    1 => 10_000.0,
                    _ => 100_000.0 + (legDistance * 1_000.0)
                };
            }

            var anchorDistance = Math.Abs(candidate.AnchorCm - headPosition);
            var score = legPenalty + anchorDistance;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        anchorCm = best.AnchorCm;
        feedbackLegIndex = best.LegIndex;
        return true;
    }

    private int MarkRouteChangedUnsafe()
    {
        _version++;
        return _version;
    }
}

