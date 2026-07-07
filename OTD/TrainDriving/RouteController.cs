// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Runtime;
using OTD.TrainDriving.RouteControl.Services;

namespace OTD.TrainDriving;

public sealed class RouteController : IDisposable
{
    private const int DefaultScale = 87;

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
    private string? _lastLoggedActiveFromWaypointId;
    private string? _lastLoggedActiveToWaypointId;
    private long? _lastProgressTickTimestamp;
    private double _lastProgressTickPositionCm;
    private int _lastProgressTickSpeedKmh;

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

    public void AddRoute(RouteLeg leg) => _service.AddRoutes(ExpandRouteLegInput(leg));

    public void AddRoute(DynamicRouteRequest request) => _service.AddRoutes(ExpandDynamicRoute(request));

    public void AddRoutes(IReadOnlyList<RouteLeg> legs) => _service.AddRoutes(ExpandRouteLegInputs(legs));

    public void AddRoutes(IReadOnlyList<DynamicRouteRequest> requests) => _service.AddRoutes(ExpandDynamicRoutes(requests));

    public void ReplaceRoutes(IReadOnlyList<RouteLeg> legs) => _service.ReplaceRoutes(ExpandRouteLegInputs(legs));

    public void ReplaceRoutes(IReadOnlyList<DynamicRouteRequest> requests) => _service.ReplaceRoutes(ExpandDynamicRoutes(requests));

    public void ReplaceRouteAtEnd(RouteLeg leg) => _service.ReplaceRoutes(ExpandRouteLegInput(leg));

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
        }

        _service.ReleaseGo();
    }

    public RouteSnapshot GetSnapshot() => _service.GetSnapshot();

    public void OnEmergencyStop() => _service.OnEmergencyStop();

    public void OnEmergencyRelease() => _service.OnEmergencyRelease();

    public void AdvancePosition(double headPositionCm) => _service.AdvancePosition(headPositionCm);

    public void OnSensorActivated(int sensorId)
    {
        var estimatedHeadPositionCm = EstimateHeadPositionAtSensorEvent();
        var activation = _service.OnSensorActivated(sensorId, estimatedHeadPositionCm);
        if (!activation.Accepted)
        {
            Logging.DebugExtended<RouteController>($"Event=SensorActivationIgnored SensorId={sensorId} Reason=NotPartOfActiveCyclePayload");
            return;
        }

        var previousPos = activation.PreviousHeadPositionCm ?? 0.0;
        var currentPos = activation.AnchorPositionCm ?? previousPos;
        var error = activation.CorrectionErrorCm ?? 0.0;
        var snapshot = _service.GetSnapshot();
        var cycleLabel = TryGetCycleLabel(snapshot.RuntimeState.ActiveRouteIndex, snapshot.RouteLegs);

        // Sensor-Fenster-Validierung: Größere Positionssprünge erfordern sofortige Neu-Planung der Bremsrampe.
        // Basis-Toleranzfenster: ±15 cm (Spezifikation: maximal akzeptable Sensorabweichung pro RouteControl SPEC v1).
        // Für dicht aufeinanderfolgende Sensoren (z. B. Weiche->Weiche) wird die Schwelle proportional
        // zum topologisch bekannten Sensorabstand erweitert, damit kurze Abschnittswechsel nicht
        // fälschlich als "LargeCorrection" markiert werden.
        const double baseAcceptableErrorCm = 15.0;
        var dynamicThresholdCm = baseAcceptableErrorCm;
        double? expectedSensorSpacingCm = null;
        if (TryGetPreviousSensorSpacingCm(snapshot.RouteLegs, sensorId, currentPos, out var spacingCm))
        {
            expectedSensorSpacingCm = spacingCm;
            dynamicThresholdCm = Math.Max(baseAcceptableErrorCm, spacingCm * 0.60);
        }

        var errorAbsMagnitude = Math.Abs(error);
        var isLargeCorrection = errorAbsMagnitude > dynamicThresholdCm;

        Logging.Debug<RouteController>($"Event=SensorActivationAccepted SensorId={sensorId} RemainingPendingSensors=-");
        Logging.Debug<RouteController>(
            $"Event=SensorRecalibration SensorId={sensorId} Recalibrated=yes PositionCm={currentPos:F1} ErrorCm={error:F1} ErrorMagnitudeCm={errorAbsMagnitude:F1} " +
            $"Active={activation.ActiveFromWaypointId ?? "-"} PreviousPositionCm={previousPos:F1} ForcedForwardLegSync={(activation.ForcedForwardLegSync ? "yes" : "no")} " +
            $"LargeCorrection={(isLargeCorrection ? "yes" : "no")} ThresholdCm={dynamicThresholdCm:F1} " +
            $"ExpectedSensorSpacingCm={(expectedSensorSpacingCm is null ? "-" : expectedSensorSpacingCm.Value.ToString("F1"))}");

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
                        $"Event=ActiveCycleCancellation SensorId={sensorId} Reason=ImmediateReplan");
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
            $"SensorId={sensorId} Recalibrated=yes Cycle={cycleLabel}");
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

            if (state.ActiveRouteIndex is null)
            {
                LogActiveCycleClearedIfChanged();
            }

            bool initialHold;
            lock (_sync) { initialHold = _initialHoldActive; }

            if (state.SafetyStopInjected || state.ActiveStopPoint || initialHold)
            {
                if (Math.Max(0, BoundTrain.SpeedV) > 0)
                    await BoundTrain.SetSpeedVAsync(0, runToken).ConfigureAwait(false);
                await WaitForRouteChangeAsync(runToken).ConfigureAwait(false);
                continue;
            }

            if (state.ActiveRouteIndex is null)
            {
                if (Math.Max(0, BoundTrain.SpeedV) > 0)
                    await BoundTrain.SetSpeedVAsync(0, runToken).ConfigureAwait(false);
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

            // Safety break: if target speed is 0 and we're very close to the end, wait for next route
            // This prevents tight spinning loops when a cycle completes with minimal remaining distance
            if (targetSpeedKmh == 0 && remainingDistanceCm <= 1.5)
            {
                if (Math.Max(0, BoundTrain.SpeedV) > 0)
                    await BoundTrain.SetSpeedVAsync(0, runToken).ConfigureAwait(false);
                await Task.Delay(50, runToken).ConfigureAwait(false);  // Minimal delay to prevent CPU spinning
                continue;
            }

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
                var offsetCm = Math.Clamp((double)leg.StopPoint.OffsetCm, 0.0, leg.DistanceCm);
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

    private static bool TryGetPreviousSensorSpacingCm(
        IReadOnlyList<RouteLeg> routeLegs,
        int sensorId,
        double anchorPositionCm,
        out double spacingCm)
    {
        spacingCm = 0.0;
        var markers = new List<(int SensorId, double AnchorCm)>();
        var cumulative = 0.0;

        foreach (var leg in routeLegs)
        {
            if (leg.SensorMarkers is not null)
            {
                foreach (var marker in leg.SensorMarkers)
                    markers.Add((marker.SensorId, cumulative + marker.OffsetCm));
            }

            cumulative += leg.DistanceCm;
        }

        if (markers.Count == 0)
            return false;

        var currentIndex = -1;
        var currentBestDistance = double.MaxValue;
        for (var i = 0; i < markers.Count; i++)
        {
            if (markers[i].SensorId != sensorId)
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
        var previousAnchor = double.MinValue;
        for (var i = 0; i < markers.Count; i++)
        {
            var anchor = markers[i].AnchorCm;
            if (anchor >= currentAnchor - 0.001)
                continue;

            if (anchor > previousAnchor)
                previousAnchor = anchor;
        }

        if (previousAnchor == double.MinValue)
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
            $"Event=ActiveCyclePayload Active={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId} PendingSensors={activeLeg.SensorMarkers?.Count ?? 0} PendingActions=0");
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
        _service.AdvanceByDelta(tick.DeltaCmModel);
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
            $"SensorId=- Recalibrated=no Cycle={cycleLabel}");

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

    private double EstimateHeadPositionAtSensorEvent()
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

    private IReadOnlyList<RouteLeg> ExpandRouteLegInputs(IReadOnlyList<RouteLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);
        var expanded = new List<RouteLeg>();
        foreach (var leg in legs)
            expanded.AddRange(ExpandRouteLegInput(leg));

        return expanded;
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

        try
        {
            _routeChangeSignal.Release();
        }
        catch (ObjectDisposedException)
        {
            // Ignore during shutdown.
        }
    }

    private void OnRouteDefinitionsChanged()
    {
        Logging.Info<RouteController>("Route definitions changed. New dynamic route requests will use the updated static data.");
        OnRouteChanged(0);
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
