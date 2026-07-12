// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Exceptions;
using OTD.TrainDriving.RouteControl.Runtime;
using OTD.TrainDriving.RouteControl.Services;

namespace OTD.TrainDriving;

public sealed class RouteController : IDisposable
{
    private const int DefaultScale = 87;

    static RouteController()
    {
        EnableUnexpectedAheadFeedbackInputEmergencyStop = Global.ROUTECONTROL_ENABLE_UNEXPECTED_AHEAD_SENSOR_EMERGENCY_STOP;
        UnexpectedAheadFeedbackInputToleranceCm = Global.ROUTECONTROL_UNEXPECTED_AHEAD_SENSOR_TOLERANCE_CM;
    }

    private readonly object _sync = new();
    private readonly TrainDriving _driving;
    private readonly RouteTableService _service;
    private readonly RouteLegResolver? _routeLegResolver;
    private readonly IRouteDefinitionService? _routeDefinitionService;
    private readonly SemaphoreSlim _routeChangeSignal = new(0);
    private readonly CancellationTokenSource _lifetimeCts = new();

    private CancellationTokenSource? _activeCycleCancellation;
    private int? _activeCycleAllowedSpeedKmh;
    private bool _disposed;
    private bool _initialHoldActive;
    private bool _emergencyReleaseHoldActive;
    private string? _lastLoggedActiveFromWaypointId;
    private string? _lastLoggedActiveToWaypointId;
    private long? _lastProgressTickTimestamp;
    private double _lastProgressTickPositionCm;
    private int _lastProgressTickSpeedKmh;
    private string? _lastIdleStateSignature;
    private RouteSnapshot? _lastTransitionSnapshot;

    /// <param name="train">Zu steuernder Zug.</param>
    /// <param name="initialHold">
    /// Wenn <c>true</c>, wartet der Controller nach dem ersten AddRoute/ReplaceRoutes-Aufruf auf
    /// einen expliziten <see cref="ReleaseGo"/>-Aufruf, bevor der Zug losfährt.
    /// Der InitialHold wird durch ReleaseGo() einmalig konsumiert; danach wirkt ReleaseGo()
    /// ausschliesslich auf aktive StopPoint-Halte.
    /// </param>
    public RouteController(Train train, bool initialHold = false)
        : this(train, new RouteTableService(), routeLegResolver: null, routeDefinitionService: null, initialHold)
    {
    }

    public RouteController(Train train, RouteTableService service, bool initialHold = false)
        : this(train, service, routeLegResolver: null, routeDefinitionService: null, initialHold)
    {
    }

    public RouteController(Train train, IRouteDefinitionService routeDefinitionService, bool initialHold = false)
        : this(train,
            new RouteTableService(),
            new RouteLegResolver(routeDefinitionService),
            routeDefinitionService,
            initialHold)
    {
    }

    public RouteController(
        Train train,
        IRouteDefinitionService routeDefinitionService,
        IRailwayLayoutService trackLayoutService,
        bool initialHold = false)
        : this(train,
            new RouteTableService(),
            new RouteLegResolver(routeDefinitionService, trackLayoutService),
            routeDefinitionService,
            initialHold)
    {
    }

    private RouteController(
        Train train,
        RouteTableService service,
        RouteLegResolver? routeLegResolver,
        IRouteDefinitionService? routeDefinitionService,
        bool initialHold)
    {
        ArgumentNullException.ThrowIfNull(train);
        ArgumentNullException.ThrowIfNull(service);

        _initialHoldActive = initialHold;
        _service = service;
        _routeLegResolver = routeLegResolver;
        _routeDefinitionService = routeDefinitionService;
        _service.BindTrain(train);
        _service.RouteChanged += OnRouteChanged;
        if (_routeDefinitionService is not null)
            _routeDefinitionService.DefinitionsChanged += OnRouteDefinitionsChanged;
        _driving = new TrainDriving(train);
    }

    public Train BoundTrain => _driving.BoundTrain ?? throw new InvalidOperationException("RouteController requires a bound train.");

    public static bool EnableUnexpectedAheadFeedbackInputEmergencyStop
    {
        get => RouteControlSafetyOptions.EnableUnexpectedAheadFeedbackInputEmergencyStop;
        set => RouteControlSafetyOptions.EnableUnexpectedAheadFeedbackInputEmergencyStop = value;
    }

    public static double UnexpectedAheadFeedbackInputToleranceCm
    {
        get => RouteControlSafetyOptions.UnexpectedAheadFeedbackInputToleranceCm;
        set => RouteControlSafetyOptions.UnexpectedAheadFeedbackInputToleranceCm = Math.Max(0.0, value);
    }

    public static bool EnableStuckAlertEmergencyStop
    {
        get => RouteControlSafetyOptions.EnableStuckAlertEmergencyStop;
        set => RouteControlSafetyOptions.EnableStuckAlertEmergencyStop = value;
    }

    public static double StuckAlertTolerancePercent
    {
        get => RouteControlSafetyOptions.StuckAlertTolerancePercent;
        set => RouteControlSafetyOptions.StuckAlertTolerancePercent = Math.Max(0.0, value);
    }

    public double AccelerationMs2
    {
        get => _driving.AccelerationMs2;
        set => _driving.AccelerationMs2 = value;
    }

    public double BrakingMs2
    {
        get => _driving.BrakingMs2;
        set => _driving.BrakingMs2 = value;
    }

    public bool UseAdaptiveSpeedStepInterval
    {
        get => _driving.UseAdaptiveSpeedStepInterval;
        set => _driving.UseAdaptiveSpeedStepInterval = value;
    }

    public TimeSpan MinSpeedStepInterval
    {
        get => _driving.MinSpeedStepInterval;
        set => _driving.MinSpeedStepInterval = value;
    }

    public TimeSpan MaxSpeedStepInterval
    {
        get => _driving.MaxSpeedStepInterval;
        set => _driving.MaxSpeedStepInterval = value;
    }

    public event Action<RouteRuntimeState>? RouteTick;
    public event Action<IdleState>? IdleStateReached;
    public event Action<RouteLegTransitionEvent>? RouteLegTransition;
    public event Action<RouteLegSegmentTransitionEvent>? RouteLegSegmentTransition;

    public enum RouteLegTransitionType
    {
        Enter,
        Leave
    }

    /// <summary>
    /// Transition-Event für den Übergang einer Zugachse in/aus ein RouteLeg.
    /// Enter: Zugspitze betritt das erste Segment der Gruppe (RouteLeg-Start).
    /// Leave: Zugschluss verlässt das letzte Segment der Gruppe (RouteLeg-Ende).
    /// GroupFromWaypointId/GroupToWaypointId: Start- und Endwaypoint des ursprünglichen (unexpandiert) RouteLeg.
    /// </summary>
    public sealed record RouteLegTransitionEvent(
        RouteLegTransitionType TransitionType,
        RouteLeg RouteLeg,
        int? RouteIndex,
        int ConsumedRouteCount,
        string? ActiveFromWaypointId,
        string GroupFromWaypointId,
        string GroupToWaypointId)
    {
        /// <summary>Gibt an ob das Leg dem RouteLeg mit den angebebenen Wegpunkten entspricht (Gruppen-Vergleich).</summary>
        public bool IsGroup(string fromWaypointId, string toWaypointId) =>
            string.Equals(GroupFromWaypointId, fromWaypointId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GroupToWaypointId, toWaypointId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Transition-Event für einzelne Segmente innerhalb einer Gruppe (RouteTable-Einträge).
    /// Enter: Zugspitze betritt das Segment (ActiveRouteIndex wechselt auf dieses Segment).
    /// Leave: Zugschluss verlässt das Segment (Segment wurde konsumiert, ConsumedRouteCount gestiegen).
    /// </summary>
    public sealed record RouteLegSegmentTransitionEvent(
        RouteLegTransitionType TransitionType,
        RouteLeg Segment,
        int? SegmentIndex,
        int ConsumedRouteCount,
        string? ActiveFromWaypointId,
        string GroupFromWaypointId,
        string GroupToWaypointId);

    public sealed record IdleState(
        string Reason,
        string? ActiveFromWaypointId,
        int? ActiveRouteIndex,
        int RouteLegCount,
        int ConsumedRouteCount,
        int CurrentSpeedKmh,
        double HeadPositionCm,
        double RemainingDistanceCm);


    public void AddRoute(RouteLeg leg)
    {
        ValidateAgainstLineRouteStart(leg);
        _service.AddRoutes(ExpandRouteLegInput(leg));
        LogRouteMutation("AddRoute", 1);
    }

    public void AddRoute(DynamicRouteRequest request)
    {
        _service.AddRoutes(ExpandDynamicRoute(request));
        LogRouteMutation("AddRouteDynamic", 1);
    }

    public void AddRoutes(IReadOnlyList<RouteLeg> legs)
    {
        _service.AddRoutes(ExpandRouteLegInputs(legs));
        LogRouteMutation("AddRoutes", legs.Count);
    }

    public void AddRoutes(IReadOnlyList<DynamicRouteRequest> requests)
    {
        _service.AddRoutes(ExpandDynamicRoutes(requests));
        LogRouteMutation("AddRoutesDynamic", requests.Count);
    }

    public void ReplaceRoutes(IReadOnlyList<RouteLeg> legs)
    {
        ValidateAgainstLineRoutes(legs);
        _service.ReplaceRoutes(ExpandRouteLegInputs(legs));
        LogRouteMutation("ReplaceRoutes", legs.Count);
    }

    public void ReplaceRoutes(IReadOnlyList<DynamicRouteRequest> requests)
    {
        _service.ReplaceRoutes(ExpandDynamicRoutes(requests));
        LogRouteMutation("ReplaceRoutesDynamic", requests.Count);
    }

    public void ReplaceRouteAtEnd(RouteLeg leg)
    {
        ValidateAgainstLineRouteStart(leg);
        _service.ReplaceRoutes(ExpandRouteLegInput(leg));
        LogRouteMutation("ReplaceRouteAtEnd", 1);
    }

    public void RemoveRouteAtEnd() => _service.RemoveRouteAtEnd();

    public void RemoveRoutesFromWaypoint(string fromWaypointId) => _service.RemoveRoutesFromWaypoint(fromWaypointId);

    public void UpdateActiveRouteLeg(string fromWaypointId, int? newDistanceCm = null, int? newMaxSpeedKmh = null)
        => _service.UpdateActiveRouteLeg(fromWaypointId, newDistanceCm, newMaxSpeedKmh);

    /// <summary>
    /// Gibt die Weiterfahrt frei. Ist ein InitialHold aktiv, wird dieser zuerst konsumiert (einmalig).
    /// Danach wirkt ReleaseGo() auf den aktiven StopPoint des aktuellen RouteLeg.
    /// </summary>
    public void ReleaseGo()
    {
        lock (_sync)
        {
            if (_initialHoldActive)
            {
                _initialHoldActive = false;
                try { _routeChangeSignal.Release(); }
                catch (ObjectDisposedException) { }
                return;
            }

            if (_emergencyReleaseHoldActive)
            {
                _emergencyReleaseHoldActive = false;
                try { _routeChangeSignal.Release(); }
                catch (ObjectDisposedException) { }
                return;
            }
        }

        _service.ReleaseGo();
    }

    public RouteSnapshot GetSnapshot() => _service.GetSnapshot();

    /// <summary>
    /// Liefert die aktuell in der RouteTable befindlichen RouteLegs als Snapshot.
    /// </summary>
    public IReadOnlyList<RouteLeg> GetRouteLegsSnapshot() => _service.GetSnapshot().RouteLegs;

    /// <summary>
    /// Formatiert die aktuelle RouteTable kompakt fuer Log-/Console-Ausgaben.
    /// </summary>
    public string DescribeRouteTable()
    {
        var snapshot = _service.GetSnapshot();
        if (snapshot.RouteLegs.Count == 0)
            return "RouteTable=<empty>";

        var activeIndex = snapshot.RuntimeState.ActiveRouteIndex;
        var parts = new List<string>(snapshot.RouteLegs.Count);

        for (var i = 0; i < snapshot.RouteLegs.Count; i++)
        {
            var leg = snapshot.RouteLegs[i];
            var activeMarker = activeIndex == i ? "*" : "";
            var groupLabel = string.IsNullOrWhiteSpace(leg.GroupId) ? "-" : leg.GroupId;
            parts.Add(
                $"{activeMarker}{i}:{leg.FromWaypointId}->{leg.ToWaypointId}(d={leg.DistanceCm.ToString("F1", CultureInfo.InvariantCulture)},v={leg.MaxSpeedKmh},g={groupLabel})");
        }

        return string.Join(" | ", parts);
    }

    public void OnEmergencyStop()
    {
        _service.OnEmergencyStop();
        SignalRouteWakeup("EmergencyStop");
    }

    public void OnEmergencyRelease() => ReleaseEmergencyStop();

    public void ReleaseEmergencyStop()
    {
        _service.OnEmergencyRelease();
        lock (_sync)
        {
            _emergencyReleaseHoldActive = true;
        }
        SignalRouteWakeup("EmergencyRelease");
    }

    public RouteTableService.StuckAlertResult? AdvancePosition(double headPositionCm, bool suppressStuckAlert = false)
        => _service.AdvancePosition(headPositionCm, suppressStuckAlert);

    public void OnFeedbackInputActivated(int feedbackId)
    {
        var estimatedHeadPositionCm = EstimateHeadPositionAtFeedbackEvent();
        var activation = _service.OnFeedbackInputActivated(feedbackId, estimatedHeadPositionCm);
        if (!activation.Accepted)
        {
            if (activation.EmergencyStopRequested)
            {
                TriggerUnexpectedFeedbackEmergencyStop(feedbackId, activation);
                return;
            }

            Logging.DebugExtended<RouteController>($"Event=FeedbackActivationIgnored FeedbackId={feedbackId} Reason=NotPartOfActiveCyclePayload");
            return;
        }

        var previousPos = activation.PreviousHeadPositionCm ?? 0.0;
        var currentPos = activation.AnchorPositionCm ?? previousPos;
        var error = activation.CorrectionErrorCm ?? 0.0;
        var snapshot = _service.GetSnapshot();
        var cycleLabel = TryGetCycleLabel(snapshot.RuntimeState.ActiveRouteIndex, snapshot.RouteLegs);

        // Feedback-Fenster-Validierung: Größere Positionssprünge erfordern sofortige Neu-Planung der Bremsrampe.
        // Basis-Toleranzfenster: ±15 cm (Spezifikation: maximal akzeptable Feedbackabweichung pro RouteControl SPEC v1).
        // Für dicht aufeinanderfolgende Feedbacken (z. B. Weiche->Weiche) wird die Schwelle proportional
        // zum topologisch bekannten Feedbackabstand erweitert, damit kurze Abschnittswechsel nicht
        // fälschlich als "LargeCorrection" markiert werden.
        const double baseAcceptableErrorCm = 15.0;
        var dynamicThresholdCm = baseAcceptableErrorCm;
        double? expectedFeedbackSpacingCm = null;
        if (TryGetPreviousFeedbackSpacingCm(snapshot.RouteLegs, feedbackId, currentPos, out var spacingCm))
        {
            expectedFeedbackSpacingCm = spacingCm;
            dynamicThresholdCm = Math.Max(baseAcceptableErrorCm, spacingCm * 0.60);
        }

        var errorAbsMagnitude = Math.Abs(error);
        var isLargeCorrection = errorAbsMagnitude > dynamicThresholdCm;

        Logging.Debug<RouteController>($"Event=FeedbackActivationAccepted FeedbackId={feedbackId} RemainingPendingFeedbacks=-");
        Logging.Debug<RouteController>(
            $"Event=FeedbackRecalibration FeedbackId={feedbackId} Recalibrated=yes PositionCm={currentPos:F1} ErrorCm={error:F1} ErrorMagnitudeCm={errorAbsMagnitude:F1} " +
            $"Active={activation.ActiveFromWaypointId ?? "-"} PreviousPositionCm={previousPos:F1} ForcedForwardLegSync={(activation.ForcedForwardLegSync ? "yes" : "no")} " +
            $"LargeCorrection={(isLargeCorrection ? "yes" : "no")} ThresholdCm={dynamicThresholdCm:F1} " +
            $"ExpectedFeedbackSpacingCm={(expectedFeedbackSpacingCm is null ? "-" : expectedFeedbackSpacingCm.Value.ToString("F1"))}");

        // Erzwingt unmittelbar eine Neuberechnung des Brems-/Fahrprofils nach großem Positionssprung.
        // Dies gewährleistet, dass die Bremsrampe sofort neu auf die korrigierte Zielposition kalibriert wird.
        lock (_sync)
        {
            try
            {
                if (_activeCycleCancellation is { IsCancellationRequested: false })
                {
                    _activeCycleCancellation.Cancel();
                    Logging.DebugExtended<RouteController>(
                        $"Event=ActiveCycleCancellation FeedbackId={feedbackId} Reason=ImmediateReplan");
                }
            }
            catch (ObjectDisposedException)
            {
                // Race during disposal or cycle handover.
            }
        }

        Logging.DebugExtended<RouteController>(
            $"Event=RouteTick PositionCm={currentPos:F1} SpeedKmh={Math.Max(0, BoundTrain.SpeedV)} Cycle={cycleLabel}");
        Logging.DebugExtended<RouteController>(
            $"Event=ApplyStep DeltaCm=0.0 PreviousPositionCm={previousPos:F1} PositionCm={currentPos:F1} " +
            $"TrajectorySpeedKmh={Math.Max(0, BoundTrain.SpeedV)} EffectiveSpeedKmh={Math.Max(0, BoundTrain.SpeedV)} " +
            $"FeedbackId={feedbackId} Recalibrated=yes Cycle={cycleLabel}");
    }

    private void TriggerUnexpectedFeedbackEmergencyStop(int feedbackId, RouteTableService.FeedbackInputActivationResult activation)
    {
        _service.OnEmergencyStop();

        lock (_sync)
        {
            try
            {
                if (_activeCycleCancellation is { IsCancellationRequested: false })
                    _activeCycleCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Race during disposal or cycle handover.
            }
        }

        var expectedFeedbackInput = activation.ExpectedFeedbackInputId is null
            ? "-"
            : $"{activation.ExpectedFeedbackInputId.Value}@{activation.ExpectedFeedbackInputAnchorCm.GetValueOrDefault():F1}cm";
        var activatedAnchor = activation.ActivatedFeedbackInputAnchorCm is null
            ? "-"
            : $"{activation.ActivatedFeedbackInputAnchorCm.Value:F1}cm";
        var delta = activation.UnexpectedDeltaToExpectedCm is null
            ? "-"
            : $"{activation.UnexpectedDeltaToExpectedCm.Value:F1}cm";

        Logging.Warning<RouteController>(
            $"Event=UnexpectedAheadFeedbackEmergencyStop FeedbackId={feedbackId} " +
            $"Expected={expectedFeedbackInput} ActivatedAnchor={activatedAnchor} DeltaToExpected={delta}.");

        _ = ExecuteUnexpectedFeedbackEmergencyStopAsync(feedbackId);
    }

    private void TriggerStuckAlertEmergencyStop(RouteTableService.StuckAlertResult alert)
    {
        _service.OnEmergencyStop();

        lock (_sync)
        {
            try
            {
                if (_activeCycleCancellation is { IsCancellationRequested: false })
                    _activeCycleCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Race during disposal or cycle handover.
            }
        }

        Logging.Warning<RouteController>(
            $"Event=StuckAlertEmergencyStop ExpectedInput={alert.ExpectedFeedbackInputId}@{alert.ExpectedFeedbackInputAnchorCm:F1}cm " +
            $"BaseAnchorCm={alert.BaseAnchorCm:F1} HeadPositionCm={alert.HeadPositionCm:F1} SegmentDistanceCm={alert.SegmentDistanceCm:F1} " +
            $"AllowedOverrunCm={alert.AllowedOverrunCm:F1} OverrunCm={alert.OverrunCm:F1} Active={alert.ActiveFromWaypointId ?? "-"}.");

        _ = ExecuteStuckAlertEmergencyStopAsync();
    }

    private async Task ExecuteUnexpectedFeedbackEmergencyStopAsync(int feedbackId)
    {
        try
        {
            await BoundTrain.EmergencyStopAsync().ConfigureAwait(false);
            Logging.Warning<RouteController>($"Event=UnexpectedAheadFeedbackEmergencyStopExecuted FeedbackId={feedbackId}.");
        }
        catch (Exception ex)
        {
            Logging.Error<RouteController>($"Unexpected feedback emergency stop failed for feedback {feedbackId}: {ex.Message}", ex);
        }
    }

    private async Task ExecuteStuckAlertEmergencyStopAsync()
    {
        try
        {
            await BoundTrain.EmergencyStopAsync().ConfigureAwait(false);
            Logging.Warning<RouteController>("Event=StuckAlertEmergencyStopExecuted.");
        }
        catch (Exception ex)
        {
            Logging.Error<RouteController>($"StuckAlert emergency stop failed: {ex.Message}", ex);
        }
    }

    public async Task Run(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        var runToken = runCancellation.Token;

        while (!runToken.IsCancellationRequested)
        {
            var snapshot = _service.GetSnapshot();
            var state = snapshot.RuntimeState;
            RouteTick?.Invoke(state);
            PublishRouteLegTransitions(snapshot);

            if (state.ActiveRouteIndex is null)
            {
                LogActiveCycleClearedIfChanged();
            }

            bool initialHold;
            bool emergencyReleaseHold;
            lock (_sync)
            {
                initialHold = _initialHoldActive;
                emergencyReleaseHold = _emergencyReleaseHoldActive;
            }

            if (state.SafetyStopInjected || state.ActiveStopPoint || initialHold || emergencyReleaseHold)
            {
                if (Math.Max(0, BoundTrain.SpeedV) > 0)
                    await BoundTrain.SetSpeedVAsync(0, runToken).ConfigureAwait(false);
                var holdReason = state.SafetyStopInjected
                    ? "SafetyStop"
                    : state.ActiveStopPoint
                        ? "StopPointHold"
                        : emergencyReleaseHold
                            ? "EmergencyReleaseHold"
                            : "InitialHold";
                PublishIdleStateOnce(
                    holdReason,
                    snapshot,
                    state,
                    Math.Max(0, BoundTrain.SpeedV),
                    remainingDistanceCm: 0.0);
                await WaitForRouteChangeAsync(runToken).ConfigureAwait(false);
                continue;
            }

            if (state.ActiveRouteIndex is null)
            {
                if (Math.Max(0, BoundTrain.SpeedV) > 0)
                    await BoundTrain.SetSpeedVAsync(0, runToken).ConfigureAwait(false);
                PublishIdleStateOnce(
                    "NoActiveRoute",
                    snapshot,
                    state,
                    Math.Max(0, BoundTrain.SpeedV),
                    remainingDistanceCm: 0.0);
                await WaitForRouteChangeAsync(runToken).ConfigureAwait(false);
                continue;
            }

            var activeIndex = state.ActiveRouteIndex.Value;
            var activeLeg = snapshot.RouteLegs[activeIndex];
            LogActiveCycleIfChanged(activeLeg);
            var legStartCm = ComputeLegStart(snapshot.RouteLegs, activeIndex);
            var remainingDistanceCm = Math.Max(1.0, ComputeRemainingDistanceForActiveGroup(snapshot.RouteLegs, activeIndex, state.HeadPositionCm, legStartCm));

            var targetSpeedKmh = ResolveTargetSpeed(snapshot.RouteLegs, activeIndex, state, legStartCm);
            var currentSpeedKmh = Math.Max(0, BoundTrain.SpeedV);

            // One-route-leg lookahead: inspect only the immediately following route-leg group.
            if (targetSpeedKmh > 0 &&
                TryGetNextRouteLegSpeedDropConstraint(
                    snapshot.RouteLegs,
                    activeIndex,
                    state.HeadPositionCm,
                    legStartCm,
                    out var reducedSpeedKmh,
                    out var distanceToReductionBoundaryCm,
                    out var reductionBoundaryWaypointId) &&
                currentSpeedKmh > reducedSpeedKmh)
            {
                targetSpeedKmh = Math.Min(targetSpeedKmh, reducedSpeedKmh);
                remainingDistanceCm = Math.Min(remainingDistanceCm, Math.Max(1.0, distanceToReductionBoundaryCm));
                Logging.DebugExtended<RouteController>(
                    $"Event=NextLegSpeedReductionApplied Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} " +
                    $"CurrentSpeedKmh={currentSpeedKmh} TargetSpeedKmh={targetSpeedKmh} Boundary={reductionBoundaryWaypointId} " +
                    $"DistanceToBoundaryCm={distanceToReductionBoundaryCm:F1} Reason=RouteLegBoundaryProfile");
            }

            if (TryGetUpcomingStopPointBrakingWindow(
                    snapshot.RouteLegs,
                    activeIndex,
                    state.HeadPositionCm,
                    out var preBrakeStartCm,
                    out var stopPositionCm,
                    out var stopLegLabel) &&
                state.HeadPositionCm >= preBrakeStartCm)
            {
                targetSpeedKmh = 0;
                remainingDistanceCm = Math.Max(1.0, stopPositionCm - state.HeadPositionCm);
                Logging.DebugExtended<RouteController>(
                    $"Event=StopPointPreBrakeApplied Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} " +
                    $"StopLeg={stopLegLabel} PreBrakeStartCm={preBrakeStartCm:F1} StopPositionCm={stopPositionCm:F1} " +
                    $"RemainingDistanceCm={remainingDistanceCm:F1}");
            }

            // Safety break: if target speed is 0 and we're very close to the end, ensure the stop position
            // is reached so NormalizeRuntimeUnsafe can activate the StopPoint, then wait for route change.
            if (targetSpeedKmh == 0 && remainingDistanceCm <= 1.5)
            {
                if (Math.Max(0, BoundTrain.SpeedV) > 0)
                    await BoundTrain.SetSpeedVAsync(0, runToken).ConfigureAwait(false);

                // Zug steht, aber die Stop-Position wurde noch nicht exakt erreicht:
                // Position manuell auf stopPositionCm setzen, damit NormalizeRuntimeUnsafe
                // den ActiveStopPoint aktiviert und der Controller korrekt in WaitForRouteChange haengt.
                // stopPositionCm == 0 bedeutet: kein aktiver StopPoint (z.B. letztes Leg) -> kein Advance noetig.
                if (stopPositionCm > 0 && state.HeadPositionCm < stopPositionCm)
                    _service.AdvancePosition(stopPositionCm, suppressStuckAlert: true);

                PublishIdleStateOnce(
                    "RouteCompleted",
                    snapshot,
                    state,
                    Math.Max(0, BoundTrain.SpeedV),
                    remainingDistanceCm);

                await WaitForRouteChangeAsync(runToken).ConfigureAwait(false);
                continue;
            }

            // Wenn bereits Stillstand und Zielgeschwindigkeit ebenfalls 0 ist, darf kein neuer
            // 0-km/h-Fahrzyklus gestartet werden (wuerde sofort zurueckkehren und die Run-Schleife belasten).
            if (targetSpeedKmh == 0 && currentSpeedKmh == 0)
            {
                if (stopPositionCm > 0 && state.HeadPositionCm < stopPositionCm)
                    _service.AdvancePosition(stopPositionCm, suppressStuckAlert: true);

                PublishIdleStateOnce(
                    "ZeroSpeedTargetReached",
                    snapshot,
                    state,
                    currentSpeedKmh,
                    remainingDistanceCm);

                await WaitForRouteChangeAsync(runToken).ConfigureAwait(false);
                continue;
            }

            _lastIdleStateSignature = null;

            var cycle = new RouteCycle(
                FromWaypointId: activeLeg.FromWaypointId,
                ToWaypointId: activeLeg.ToWaypointId,
                DistanceCm: remainingDistanceCm,
                AllowedSpeedKmh: targetSpeedKmh,
                DriveProfile: ResolveGroupDriveProfile(snapshot.RouteLegs, activeIndex));

            CancellationTokenSource? cycleCts = null;
            _driving.ProgressTick += OnProgressTick;
            try
            {
                lock (_sync)
                {
                    _activeCycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(runToken);
                    _activeCycleAllowedSpeedKmh = cycle.AllowedSpeedKmh;
                    cycleCts = _activeCycleCancellation;
                }

                await _driving.DriveRouteCycleAsync(cycle, cycleCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!runToken.IsCancellationRequested)
            {
                // A route mutation interrupted the active execution cycle; restart loop with fresh snapshot.
            }
            finally
            {
                _driving.ProgressTick -= OnProgressTick;
                lock (_sync)
                {
                    if (ReferenceEquals(_activeCycleCancellation, cycleCts))
                    {
                        _activeCycleCancellation = null;
                        _activeCycleAllowedSpeedKmh = null;
                    }
                }

                cycleCts?.Dispose();
            }
        }
    }

    public async Task Stop(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (BoundTrain.SpeedV > 0)
            await BoundTrain.SetSpeedVAsync(0, cancellationToken).ConfigureAwait(false);
    }

    private int ResolveTargetSpeed(
        IReadOnlyList<RouteLeg> routeLegs,
        int activeIndex,
        RouteRuntimeState state,
        double activeLegStartCm)
    {
        var activeLeg = routeLegs[activeIndex];
        var targetSpeed = Math.Max(0, activeLeg.MaxSpeedKmh);

        // Safety-first lookahead: if the next leg is slower, cap current target to avoid late braking.
        if (activeIndex < routeLegs.Count - 1)
        {
            var nextSpeed = Math.Max(0, routeLegs[activeIndex + 1].MaxSpeedKmh);
            if (nextSpeed < targetSpeed)
                targetSpeed = nextSpeed;
        }

        // For faster transitions, optionally defer acceleration until the complete train entered the new leg.
        if (activeIndex > 0)
        {
            var previousLeg = routeLegs[activeIndex - 1];
            if (activeLeg.MaxSpeedKmh > previousLeg.MaxSpeedKmh &&
                activeLeg.AccelerationStartPolicy == AccelerationStartPolicy.AfterTrainClearsWaypoint)
            {
                var previousSpeed = Math.Max(0, previousLeg.MaxSpeedKmh);
                var trainClearsWaypointCm = activeLegStartCm + Math.Max(0.0, state.TrainLengthCm);
                if (state.HeadPositionCm < trainClearsWaypointCm)
                    targetSpeed = Math.Min(targetSpeed, previousSpeed);
            }
        }

        // Vorverlagerte StopPoint-Bremsung:
        // StopPoint.OffsetCm ist als Abstand zum Leg-Ende interpretiert.
        // Die Bremsung startet daher bereits OffsetCm vor Leg-Beginn des StopPoint-Legs,
        // damit das Profil identisch zu "Bremsung ueber volle StopPoint-Leg-Laenge" bleibt.
        if (TryGetUpcomingStopPointBrakingWindow(
                routeLegs,
                activeIndex,
                state.HeadPositionCm,
                out var preBrakeStartCm,
                out _,
                out _) &&
            state.HeadPositionCm >= preBrakeStartCm)
        {
            targetSpeed = 0;
        }

        // Impliziter End-Halt: Im letzten RouteLeg wird immer ueber die Reststrecke auf 0 gebremst.
        if (activeIndex == routeLegs.Count - 1)
            targetSpeed = 0;

        return targetSpeed;
    }

    private static bool TryGetNextRouteLegSpeedDropConstraint(
        IReadOnlyList<RouteLeg> routeLegs,
        int activeIndex,
        double headPositionCm,
        double activeLegStartCm,
        out int reducedSpeedKmh,
        out double distanceToReductionBoundaryCm,
        out string reductionBoundaryWaypointId)
    {
        reducedSpeedKmh = 0;
        distanceToReductionBoundaryCm = 0.0;
        reductionBoundaryWaypointId = string.Empty;

        if (activeIndex < 0 || activeIndex >= routeLegs.Count - 1)
            return false;

        var activeLeg = routeLegs[activeIndex];
        var activeGroupId = activeLeg.GroupId;
        var currentGroupEndIndex = activeIndex;
        if (!string.IsNullOrWhiteSpace(activeGroupId))
        {
            while (currentGroupEndIndex + 1 < routeLegs.Count &&
                   string.Equals(routeLegs[currentGroupEndIndex + 1].GroupId, activeGroupId, StringComparison.OrdinalIgnoreCase))
            {
                currentGroupEndIndex++;
            }
        }

        var nextGroupStartIndex = currentGroupEndIndex + 1;
        if (nextGroupStartIndex >= routeLegs.Count)
        {
            Logging.DebugExtended<RouteController>(
                $"Event=NextLegLookahead Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} " +
                $"ActiveGroup={FormatGroupLabel(activeGroupId, activeIndex, currentGroupEndIndex)} NextGroup=- Outcome=NoFollowingRouteLeg");
            return false;
        }

        var nextGroupId = routeLegs[nextGroupStartIndex].GroupId;
        var nextGroupEndIndex = nextGroupStartIndex;
        if (!string.IsNullOrWhiteSpace(nextGroupId))
        {
            while (nextGroupEndIndex + 1 < routeLegs.Count &&
                   string.Equals(routeLegs[nextGroupEndIndex + 1].GroupId, nextGroupId, StringComparison.OrdinalIgnoreCase))
            {
                nextGroupEndIndex++;
            }
        }

        var currentRouteLegSpeed = Math.Max(0, activeLeg.MaxSpeedKmh);
        var runningLegStartCm = activeLegStartCm;
        for (var i = activeIndex; i < nextGroupStartIndex; i++)
            runningLegStartCm += routeLegs[i].DistanceCm;

        var nextGroupHeadLeg = routeLegs[nextGroupStartIndex];
        var nextGroupTailLeg = routeLegs[nextGroupEndIndex];
        Logging.DebugExtended<RouteController>(
            $"Event=NextLegLookahead Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} " +
            $"ActiveGroup={FormatGroupLabel(activeGroupId, activeIndex, currentGroupEndIndex)} " +
            $"NextGroup={FormatGroupLabel(nextGroupId, nextGroupStartIndex, nextGroupEndIndex)} " +
            $"NextRouteLeg={nextGroupHeadLeg.FromWaypointId}->{nextGroupTailLeg.ToWaypointId} " +
            $"CurrentMaxKmh={currentRouteLegSpeed} HeadPositionCm={headPositionCm:F1} Outcome=Inspecting");

        for (var legIndex = nextGroupStartIndex; legIndex <= nextGroupEndIndex; legIndex++)
        {
            var nextLegSpeed = Math.Max(0, routeLegs[legIndex].MaxSpeedKmh);
            if (nextLegSpeed < currentRouteLegSpeed)
            {
                reducedSpeedKmh = nextLegSpeed;
                distanceToReductionBoundaryCm = Math.Max(0.0, runningLegStartCm - headPositionCm);
                reductionBoundaryWaypointId = routeLegs[legIndex].FromWaypointId;
                Logging.Debug<RouteController>(
                    $"Event=NextLegSpeedDropCandidate Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} " +
                    $"NextRouteLeg={nextGroupHeadLeg.FromWaypointId}->{nextGroupTailLeg.ToWaypointId} " +
                    $"Boundary={reductionBoundaryWaypointId} Segment={routeLegs[legIndex].FromWaypointId}->{routeLegs[legIndex].ToWaypointId} " +
                    $"CurrentMaxKmh={currentRouteLegSpeed} ReducedMaxKmh={reducedSpeedKmh} DistanceToBoundaryCm={distanceToReductionBoundaryCm:F1}");
                return true;
            }

            runningLegStartCm += routeLegs[legIndex].DistanceCm;
        }

        Logging.DebugExtended<RouteController>(
            $"Event=NextLegLookahead Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} " +
            $"NextRouteLeg={nextGroupHeadLeg.FromWaypointId}->{nextGroupTailLeg.ToWaypointId} Outcome=NoLowerSpeedSegment");

        return false;
    }

    private static bool TryGetUpcomingStopPointBrakingWindow(
        IReadOnlyList<RouteLeg> routeLegs,
        int activeIndex,
        double headPositionCm,
        out double preBrakeStartCm,
        out double stopPositionCm,
        out string stopLegLabel)
    {
        preBrakeStartCm = 0.0;
        stopPositionCm = 0.0;
        stopLegLabel = string.Empty;

        if (activeIndex < 0 || activeIndex >= routeLegs.Count)
            return false;

        var runningLegStartCm = ComputeLegStart(routeLegs, activeIndex);
        for (var i = activeIndex; i < routeLegs.Count; i++)
        {
            var leg = routeLegs[i];
            if (leg.StopPoint is not null)
            {
                // Nach topologischer Auflösung ist StopPoint.OffsetCm bereits ABSOLUT
                // relativ zum Leg-Anfang (nicht zum Leg-Ende).
                // Die Offset-Position wird so interpretiert:
                // - StopPoint.OffsetCm = Abstand vom Leg-Anfang zum StopPoint
                // - Vorverlagerung: Bremsung beginnt um diesen Offset VOR Leg-Anfang
                var offsetCm = Math.Clamp(leg.StopPoint.OffsetCm, 0.0, leg.DistanceCm);
                var candidateStopPositionCm = runningLegStartCm + offsetCm;
                var candidatePreBrakeStartCm = runningLegStartCm - offsetCm;

                // Skip bereits passierte StopPoints innerhalb derselben Resttabelle.
                if (candidateStopPositionCm + 0.001 < headPositionCm)
                {
                    runningLegStartCm += leg.DistanceCm;
                    continue;
                }

                // Vorverlagerung kann in vorherige Legs gehen.
                preBrakeStartCm = Math.Max(0.0, candidatePreBrakeStartCm);
                stopPositionCm = candidateStopPositionCm;
                stopLegLabel = $"{leg.FromWaypointId}->{leg.ToWaypointId}";
                return true;
            }

            runningLegStartCm += leg.DistanceCm;
        }

        return false;
    }

    private static string FormatGroupLabel(string? groupId, int startIndex, int endIndex)
    {
        var label = string.IsNullOrWhiteSpace(groupId) ? "single" : groupId;
        return startIndex == endIndex ? $"{label}[{startIndex}]" : $"{label}[{startIndex}-{endIndex}]";
    }

    private static double ComputeLegStart(IReadOnlyList<RouteLeg> legs, int legIndex)
    {
        var start = 0.0;
        for (var i = 0; i < legIndex; i++)
            start += legs[i].DistanceCm;

        return start;
    }

    private static bool TryGetPreviousFeedbackSpacingCm(
        IReadOnlyList<RouteLeg> routeLegs,
        int feedbackId,
        double anchorPositionCm,
        out double spacingCm)
    {
        spacingCm = 0.0;
        var markers = new List<(int FeedbackId, double AnchorCm)>();
        var cumulative = 0.0;

        foreach (var leg in routeLegs)
        {
            if (leg.FeedbackInputActivationPoints is not null)
            {
                foreach (var marker in leg.FeedbackInputActivationPoints)
                    markers.Add((marker.FeedbackId, cumulative + marker.OffsetCm));
            }

            cumulative += leg.DistanceCm;
        }

        if (markers.Count == 0)
            return false;

        var currentIndex = -1;
        var currentBestDistance = double.MaxValue;
        for (var i = 0; i < markers.Count; i++)
        {
            if (markers[i].FeedbackId != feedbackId)
                continue;

            var distanceToAnchor = Math.Abs(markers[i].AnchorCm - anchorPositionCm);
            if (distanceToAnchor >= currentBestDistance)
                continue;

            currentBestDistance = distanceToAnchor;
            currentIndex = i;
        }

        if (currentIndex < 0)
            return false;

        var currentAnchor = markers[currentIndex].AnchorCm;
        var previousAnchor = 0.0;
        var hasPreviousAnchor = false;
        for (var i = 0; i < markers.Count; i++)
        {
            var anchor = markers[i].AnchorCm;
            if (anchor >= currentAnchor - 0.001)
                continue;

            if (!hasPreviousAnchor || anchor > previousAnchor)
            {
                previousAnchor = anchor;
                hasPreviousAnchor = true;
            }
        }

        if (!hasPreviousAnchor)
            return false;

        spacingCm = Math.Max(0.0, currentAnchor - previousAnchor);
        return spacingCm > 0.0;
    }

    private void LogActiveCycleIfChanged(RouteLeg activeLeg)
    {
        lock (_sync)
        {
            if (string.Equals(_lastLoggedActiveFromWaypointId, activeLeg.FromWaypointId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_lastLoggedActiveToWaypointId, activeLeg.ToWaypointId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastLoggedActiveFromWaypointId = activeLeg.FromWaypointId;
            _lastLoggedActiveToWaypointId = activeLeg.ToWaypointId;
        }

        Logging.Debug<RouteController>(
            $"Event=ActiveCycleChanged Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} DistanceCm={activeLeg.DistanceCm:F1} AllowedSpeedKmh={activeLeg.MaxSpeedKmh:F0}");
        Logging.DebugExtended<RouteController>(
            $"Event=ActiveCyclePayload Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} PendingFeedbacks={activeLeg.FeedbackInputActivationPoints?.Count ?? 0} PendingActions=0");
    }

    private void LogActiveCycleClearedIfChanged()
    {
        string? previousFrom;
        lock (_sync)
        {
            if (_lastLoggedActiveFromWaypointId is null && _lastLoggedActiveToWaypointId is null)
                return;

            previousFrom = _lastLoggedActiveFromWaypointId;
            _lastLoggedActiveFromWaypointId = null;
            _lastLoggedActiveToWaypointId = null;
        }

        Logging.Debug<RouteController>($"Event=ActiveCycleCleared Previous={previousFrom ?? "-"}");
    }

    private void OnProgressTick(TrainDrivingProgressTick tick)
    {
        var previousPos = _service.GetRuntimeState().HeadPositionCm;
        var stuckAlert = _service.AdvanceByDelta(tick.DeltaCmModel);
        var snapshot = _service.GetSnapshot();
        var state = snapshot.RuntimeState;
        var currentPos = state.HeadPositionCm;
        _lastProgressTickTimestamp = Stopwatch.GetTimestamp();
        _lastProgressTickPositionCm = currentPos;
        _lastProgressTickSpeedKmh = Math.Max(0, tick.CommandedSpeedKmhPrototype);

        var cycleLabel = TryGetCycleLabel(state.ActiveRouteIndex, snapshot.RouteLegs);
        Logging.DebugExtended<RouteController>(
            $"Event=RouteTick PositionCm={currentPos:F1} SpeedKmh={tick.CommandedSpeedKmhPrototype} Cycle={cycleLabel}");

        Logging.DebugExtended<RouteController>(
            $"Event=ApplyStep DeltaCm={tick.DeltaCmModel:F1} PreviousPositionCm={previousPos:F1} PositionCm={currentPos:F1} " +
            $"TrajectorySpeedKmh={tick.CommandedSpeedKmhPrototype} EffectiveSpeedKmh={tick.CommandedSpeedKmhPrototype} " +
            $"FeedbackId=- Recalibrated=no Cycle={cycleLabel}");

        if (stuckAlert is not null)
        {
            TriggerStuckAlertEmergencyStop(stuckAlert.Value);
            RouteTick?.Invoke(state);
            return;
        }

        // Dynamische Replanung innerhalb eines laufenden Fahrzyklus:
        // Wenn sich durch fortschreitende Position das Bremsziel (StopPoint oder impliziter End-Halt)
        // auf eine niedrigere Zielgeschwindigkeit verschiebt, muss der aktive Zyklus abgebrochen
        // und als Bremsfahrt neu geplant werden.
        if (state.ActiveRouteIndex is { } activeIndex && activeIndex >= 0 && activeIndex < snapshot.RouteLegs.Count)
        {
            var activeLeg = snapshot.RouteLegs[activeIndex];
            var legStartCm = ComputeLegStart(snapshot.RouteLegs, activeIndex);
            var desiredTargetSpeedKmh = ResolveTargetSpeed(snapshot.RouteLegs, activeIndex, state, legStartCm);

            if (TryGetNextRouteLegSpeedDropConstraint(
                    snapshot.RouteLegs,
                    activeIndex,
                    state.HeadPositionCm,
                    legStartCm,
                    out var reducedSpeedKmh,
                    out _,
                    out _) &&
                Math.Max(0, BoundTrain.SpeedV) > reducedSpeedKmh)
            {
                desiredTargetSpeedKmh = Math.Min(desiredTargetSpeedKmh, reducedSpeedKmh);
            }
            int? activeCycleAllowedSpeedKmh;
            lock (_sync)
            {
                activeCycleAllowedSpeedKmh = _activeCycleAllowedSpeedKmh;
            }

            // Nur neu planen, wenn das aktuell laufende Zyklus-Ziel wirklich zu hoch ist.
            // Beispiel: laufender Bremszyklus zielt bereits auf 0 km/h -> kein Tick-fuer-Tick-Abbruch.
            var activeCycleTarget = activeCycleAllowedSpeedKmh ?? tick.CommandedSpeedKmhPrototype;

            if (desiredTargetSpeedKmh < activeCycleTarget)
            {
                Logging.Debug<RouteController>(
                    $"Event=DynamicReplanRequested Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} PositionCm={currentPos:F1} " +
                    $"CommandedSpeedKmh={tick.CommandedSpeedKmhPrototype} CycleTargetKmh={activeCycleTarget} DesiredTargetKmh={desiredTargetSpeedKmh}");

                lock (_sync)
                {
                    try
                    {
                        if (_activeCycleCancellation is { IsCancellationRequested: false })
                            _activeCycleCancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Race during disposal or cycle handover.
                    }
                }
            }
        }

        RouteTick?.Invoke(state);
    }

    private double EstimateHeadPositionAtFeedbackEvent()
    {
        var basePositionCm = _lastProgressTickTimestamp is null
            ? _service.GetRuntimeState().HeadPositionCm
            : _lastProgressTickPositionCm;

        if (_lastProgressTickTimestamp is null)
            return Math.Max(0.0, basePositionCm);

        var elapsedTicks = Stopwatch.GetTimestamp() - _lastProgressTickTimestamp.Value;
        if (elapsedTicks <= 0)
            return Math.Max(0.0, basePositionCm);

        var speedKmh = _lastProgressTickSpeedKmh > 0
            ? _lastProgressTickSpeedKmh
            : Math.Max(0, BoundTrain.SpeedV);
        var dtSeconds = elapsedTicks / (double)Stopwatch.Frequency;
        var deltaCm = ModelCmPerSecondFromPrototypeKmh(speedKmh) * dtSeconds;

        return Math.Max(0.0, basePositionCm + deltaCm);
    }

    private static double ModelCmPerSecondFromPrototypeKmh(double speedKmhPrototype)
        => (speedKmhPrototype / 3.6) * 100.0 / DefaultScale;

    private static string TryGetCycleLabel(int? activeIndex, IReadOnlyList<RouteLeg> legs)
    {
        if (activeIndex is null || activeIndex < 0 || activeIndex >= legs.Count)
            return "-";

        var leg = legs[activeIndex.Value];
        return $"{leg.FromWaypointId}->{leg.ToWaypointId}";
    }

    private IReadOnlyList<RouteLeg> ExpandDynamicRoute(DynamicRouteRequest request)
    {
        if (_routeLegResolver is null)
        {
            throw new InvalidOperationException(
                "Dynamic route requests require a route definition service. Use RouteController(train, IRouteDefinitionService, ...)." );
        }

        return _routeLegResolver.Expand(request);
    }

    private IReadOnlyList<RouteLeg> ExpandRouteLegInput(RouteLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);
        if (leg.IsTopologyResolved)
            return [leg];

        if (_routeLegResolver is null)
        {
            throw new InvalidOperationException(
                "RouteLeg without resolved topology requires a route definition service. Use RouteController(train, IRouteDefinitionService, ...)." );
        }

        return _routeLegResolver.Expand(leg);
    }

    private void LogRouteMutation(string operation, int requestedCount)
    {
        var snapshot = _service.GetSnapshot();
        Logging.Debug<RouteController>(
            $"Event=RouteMutationApplied Operation={operation} Requested={requestedCount} RouteTableCount={snapshot.RouteLegs.Count}");
    }

    
    private IReadOnlyList<RouteLeg> ExpandRouteLegInputs(IReadOnlyList<RouteLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);
        var expanded = new List<RouteLeg>();
        foreach (var leg in legs)
            expanded.AddRange(ExpandRouteLegInput(leg));

        return expanded;
    }

    private void ValidateAgainstLineRoutes(IReadOnlyList<RouteLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);
        foreach (var leg in legs)
            ValidateAgainstLineRouteStart(leg);
    }

    private void ValidateAgainstLineRouteStart(RouteLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);
        if (leg.TravelDirection != RouteTravelDirection.AgainstLine)
            return;

        if (_routeLegResolver is null)
            return;

        var snapshot = _service.GetSnapshot();
        if (snapshot.RouteLegs.Count == 0)
            return;

        var currentRouteEndWaypointId = snapshot.RouteLegs[^1].ToWaypointId;
        if (string.Equals(currentRouteEndWaypointId, leg.FromWaypointId, StringComparison.OrdinalIgnoreCase))
            return;

        var trainLengthCm = Math.Max(0.0, snapshot.RuntimeState.TrainLengthCm);
        var availableSectionCm = _routeLegResolver.ResolvePathDistanceCm(currentRouteEndWaypointId, leg.FromWaypointId);

        if (trainLengthCm > availableSectionCm + 0.1)
        {
            throw new RouteValidationException(
                $"AgainstLine RouteLeg '{leg.FromWaypointId}->{leg.ToWaypointId}' cannot be created: train length {trainLengthCm:F1} cm exceeds distance from current route end '{currentRouteEndWaypointId}' to new start '{leg.FromWaypointId}' ({availableSectionCm:F1} cm)."
            );
        }
    }

    private IReadOnlyList<RouteLeg> ExpandDynamicRoutes(IReadOnlyList<DynamicRouteRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var expanded = new List<RouteLeg>();
        foreach (var request in requests)
            expanded.AddRange(ExpandDynamicRoute(request));

        return expanded;
    }

    private static double ComputeRemainingDistanceForActiveGroup(
        IReadOnlyList<RouteLeg> routeLegs,
        int activeIndex,
        double headPositionCm,
        double activeLegStartCm)
    {
        var activeLeg = routeLegs[activeIndex];
        if (string.IsNullOrWhiteSpace(activeLeg.GroupId) || activeLeg.GroupTotalDistanceCm <= 0)
        {
            var legEndCm = activeLegStartCm + activeLeg.DistanceCm;
            return Math.Max(1.0, legEndCm - headPositionCm);
        }

        var groupStartAbsCm = activeLegStartCm - activeLeg.GroupOffsetStartCm;
        var groupEndAbsCm = groupStartAbsCm + activeLeg.GroupTotalDistanceCm;
        return Math.Max(1.0, groupEndAbsCm - headPositionCm);
    }

    private static RouteDriveProfile? ResolveGroupDriveProfile(IReadOnlyList<RouteLeg> routeLegs, int activeIndex)
    {
        var activeLeg = routeLegs[activeIndex];
        if (activeLeg.DriveProfile is not null)
            return activeLeg.DriveProfile;

        if (string.IsNullOrWhiteSpace(activeLeg.GroupId))
            return null;

        for (var i = activeIndex - 1; i >= 0; i--)
        {
            var candidate = routeLegs[i];
            if (!string.Equals(candidate.GroupId, activeLeg.GroupId, StringComparison.OrdinalIgnoreCase))
                break;

            if (candidate.DriveProfile is not null)
                return candidate.DriveProfile;
        }

        for (var i = activeIndex + 1; i < routeLegs.Count; i++)
        {
            var candidate = routeLegs[i];
            if (!string.Equals(candidate.GroupId, activeLeg.GroupId, StringComparison.OrdinalIgnoreCase))
                break;

            if (candidate.DriveProfile is not null)
                return candidate.DriveProfile;
        }

        return null;
    }

    private void OnRouteChanged(int _)
    {
        var snapshot = _service.GetSnapshot();
        lock (_sync)
        {
            try
            {
                _activeCycleCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Race during disposal.
            }
        }

        Logging.DebugExtended<RouteController>(
            $"Event=RouteChangedSignal RouteTableCount={snapshot.RouteLegs.Count} ActiveIndex={snapshot.RuntimeState.ActiveRouteIndex?.ToString() ?? "-"}");

        try
        {
            _routeChangeSignal.Release();
        }
        catch (ObjectDisposedException)
        {
            // Ignore during shutdown.
        }
    }

    private void PublishIdleStateOnce(
        string reason,
        RouteSnapshot snapshot,
        RouteRuntimeState state,
        int currentSpeedKmh,
        double remainingDistanceCm)
    {
        var signature =
            $"{reason}|{state.ActiveFromWaypointId ?? "-"}|{state.ActiveRouteIndex?.ToString() ?? "-"}|{snapshot.RouteLegs.Count}|{state.ConsumedRouteCount}|{Math.Round(state.HeadPositionCm, 1)}|{currentSpeedKmh}";

        if (string.Equals(_lastIdleStateSignature, signature, StringComparison.Ordinal))
            return;

        _lastIdleStateSignature = signature;
        IdleStateReached?.Invoke(new IdleState(
            Reason: reason,
            ActiveFromWaypointId: state.ActiveFromWaypointId,
            ActiveRouteIndex: state.ActiveRouteIndex,
            RouteLegCount: snapshot.RouteLegs.Count,
            ConsumedRouteCount: state.ConsumedRouteCount,
            CurrentSpeedKmh: currentSpeedKmh,
            HeadPositionCm: state.HeadPositionCm,
             RemainingDistanceCm: remainingDistanceCm));
    }

    private void PublishRouteLegTransitions(RouteSnapshot current)
    {
        var previous = _lastTransitionSnapshot;
        _lastTransitionSnapshot = current;

        if (previous is null)
            return;

        var prevState = previous.RuntimeState;
        var currState = current.RuntimeState;

        // === Segment-Level: Enter ===
        // Zugspitze betritt neues Segment (ActiveRouteIndex geändert).
        if (currState.ActiveRouteIndex is { } currIdx &&
            currIdx >= 0 && currIdx < current.RouteLegs.Count)
        {
            var currSeg = current.RouteLegs[currIdx];
            var currGroupId = currSeg.GroupId;
            var (currGroupFrom, currGroupTo) = ResolveGroupEndpoints(current.RouteLegs, currGroupId);

            var prevSegFromId = default(string?);
            var prevSegToId = default(string?);
            var prevGroupId = default(string?);

            if (prevState.ActiveRouteIndex is { } prevIdx &&
                prevIdx >= 0 && prevIdx < previous.RouteLegs.Count)
            {
                var prevSeg = previous.RouteLegs[prevIdx];
                prevSegFromId = prevSeg.FromWaypointId;
                prevSegToId = prevSeg.ToWaypointId;
                prevGroupId = prevSeg.GroupId;
            }

            var segmentChanged =
                prevSegFromId is null ||
                !string.Equals(prevSegFromId, currSeg.FromWaypointId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(prevSegToId, currSeg.ToWaypointId, StringComparison.OrdinalIgnoreCase);

            if (segmentChanged)
            {
                RouteLegSegmentTransition?.Invoke(new RouteLegSegmentTransitionEvent(
                    RouteLegTransitionType.Enter,
                    currSeg,
                    currIdx,
                    currState.ConsumedRouteCount,
                    currState.ActiveFromWaypointId,
                    currGroupFrom,
                    currGroupTo));
                Logging.DebugExtended<RouteController>(
                    $"Event=SegmentEnter Segment={currSeg.FromWaypointId}->{currSeg.ToWaypointId} " +
                    $"Group={currGroupFrom}->{currGroupTo} SegIdx={currIdx} Consumed={currState.ConsumedRouteCount}");
            }

            // === Gruppen-Level: Enter ===
            var groupChanged =
                prevGroupId is null ||
                !string.Equals(prevGroupId, currGroupId, StringComparison.OrdinalIgnoreCase);

            if (groupChanged)
            {
                RouteLegTransition?.Invoke(new RouteLegTransitionEvent(
                    RouteLegTransitionType.Enter,
                    currSeg,
                    currIdx,
                    currState.ConsumedRouteCount,
                    currState.ActiveFromWaypointId,
                    currGroupFrom,
                    currGroupTo));
                Logging.Debug<RouteController>(
                    $"Event=RouteLegEnter Group={currGroupFrom}->{currGroupTo} " +
                    $"SegIdx={currIdx} Consumed={currState.ConsumedRouteCount}");
            }
        }

        // === Segment-Level + Gruppen-Level: Leave ===
        // ConsumedRouteCount gestiegen => Zugschluss hat Segmente verlassen.
        if (currState.ConsumedRouteCount > prevState.ConsumedRouteCount)
        {
            var consumedDelta = currState.ConsumedRouteCount - prevState.ConsumedRouteCount;
            var prevLegs = previous.RouteLegs;
            var emittedGroups = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < consumedDelta && i < prevLegs.Count; i++)
            {
                var leftSeg = prevLegs[i];
                var groupId = leftSeg.GroupId ?? leftSeg.FromWaypointId;
                var (groupFrom, groupTo) = ResolveGroupEndpoints(prevLegs, leftSeg.GroupId);

                // Segment-Leave: einmal pro konsumiertem Segment
                RouteLegSegmentTransition?.Invoke(new RouteLegSegmentTransitionEvent(
                    RouteLegTransitionType.Leave,
                    leftSeg,
                    i,
                    currState.ConsumedRouteCount,
                    currState.ActiveFromWaypointId,
                    groupFrom,
                    groupTo));
                Logging.DebugExtended<RouteController>(
                    $"Event=SegmentLeave Segment={leftSeg.FromWaypointId}->{leftSeg.ToWaypointId} " +
                    $"Group={groupFrom}->{groupTo} SegIdx={i} Consumed={currState.ConsumedRouteCount}");

                // Gruppen-Leave: nur wenn letztes Segment dieser Gruppe konsumiert wurde
                if (emittedGroups.Add(groupId))
                {
                    var lastSegmentOfGroup = FindLastSegmentIndexOfGroup(prevLegs, leftSeg.GroupId);
                    if (lastSegmentOfGroup < consumedDelta)
                    {
                        RouteLegTransition?.Invoke(new RouteLegTransitionEvent(
                            RouteLegTransitionType.Leave,
                            leftSeg,
                            i,
                            currState.ConsumedRouteCount,
                            currState.ActiveFromWaypointId,
                            groupFrom,
                            groupTo));
                        Logging.Debug<RouteController>(
                            $"Event=RouteLegLeave Group={groupFrom}->{groupTo} " +
                            $"SegIdx={i} Consumed={currState.ConsumedRouteCount}");
                    }
                }
            }
        }
    }

    private static (string FromWaypointId, string ToWaypointId) ResolveGroupEndpoints(
        System.Collections.Generic.IReadOnlyList<RouteLeg> legs,
        string? groupId)
    {
        var firstFrom = string.Empty;
        var lastTo = string.Empty;

        foreach (var leg in legs)
        {
            if (groupId is not null &&
                !string.Equals(leg.GroupId, groupId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrEmpty(firstFrom))
                firstFrom = leg.FromWaypointId;

            lastTo = leg.ToWaypointId;
        }

        return (firstFrom, lastTo);
    }

    private static int FindLastSegmentIndexOfGroup(
        System.Collections.Generic.IReadOnlyList<RouteLeg> legs,
        string? groupId)
    {
        var lastIndex = -1;
        for (var i = 0; i < legs.Count; i++)
        {
            if (groupId is null || string.Equals(legs[i].GroupId, groupId, StringComparison.OrdinalIgnoreCase))
                lastIndex = i;
        }

        return lastIndex;
    }

    private void OnRouteDefinitionsChanged()
    {
        Logging.Info<RouteController>("Route definitions changed. New dynamic route requests will use the updated static data.");
        OnRouteChanged(0);
    }

    private void SignalRouteWakeup(string reason)
    {
        try
        {
            _routeChangeSignal.Release();
            Logging.DebugExtended<RouteController>($"Event=RouteWakeup Reason={reason}");
        }
        catch (ObjectDisposedException)
        {
            // Ignore during shutdown.
        }
    }

    private async Task WaitForRouteChangeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _routeChangeSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // Ignore during disposal.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _service.RouteChanged -= OnRouteChanged;
        if (_routeDefinitionService is not null)
            _routeDefinitionService.DefinitionsChanged -= OnRouteDefinitionsChanged;
        _lifetimeCts.Cancel();

        lock (_sync)
        {
            _activeCycleCancellation?.Cancel();
            _activeCycleCancellation?.Dispose();
            _activeCycleCancellation = null;
        }

        _routeChangeSignal.Dispose();
        _lifetimeCts.Dispose();
    }
}
