// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
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
    private const double StopPointBrakeSafetyMarginCm = 5.0;

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

    public void AddRoute(RouteLeg leg) => _service.AddRoute(leg);

    public void AddRoute(DynamicRouteRequest request) => _service.AddRoute(ResolveDynamicRoute(request));

    public void AddRoutes(IReadOnlyList<RouteLeg> legs) => _service.AddRoutes(legs);

    public void AddRoutes(IReadOnlyList<DynamicRouteRequest> requests) => _service.AddRoutes(ResolveDynamicRoutes(requests));

    public void ReplaceRoutes(IReadOnlyList<RouteLeg> legs) => _service.ReplaceRoutes(legs);

    public void ReplaceRoutes(IReadOnlyList<DynamicRouteRequest> requests) => _service.ReplaceRoutes(ResolveDynamicRoutes(requests));

    public void ReplaceRouteAtEnd(RouteLeg leg) => _service.ReplaceRouteAtEnd(leg);

    public void RemoveRouteAtEnd() => _service.RemoveRouteAtEnd();

    public void RemoveRoutesFromWaypoint(string fromWaypointId) => _service.RemoveRoutesFromWaypoint(fromWaypointId);

    public void UpdateActiveRouteLeg(string fromWaypointId, int? newDistanceCm = null, double? newMaxSpeedKmh = null)
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
        var activation = _service.OnSensorActivated(sensorId);
        if (!activation.Accepted)
        {
            Logging.DebugExtended<RouteController>($"Sensor {sensorId}: ignored, not part of active cycle payload.");
            return;
        }

        var previousPos = activation.PreviousHeadPositionCm ?? 0.0;
        var currentPos = activation.AnchorPositionCm ?? previousPos;
        var error = activation.CorrectionErrorCm ?? 0.0;
        var snapshot = _service.GetSnapshot();
        var cycleLabel = TryGetCycleLabel(snapshot.RuntimeState.ActiveRouteIndex, snapshot.RouteLegs);

        // Sensor-Fenster-Validierung: Größere Positionssprünge erfordern sofortige Neu-Planung der Bremsrampe.
        // Standard-Toleranzfenster: ±15 cm (Spezifikation: maximal akzeptable Sensorabweichung pro RouteControl SPEC v1).
        const double maxAcceptableErrorCm = 15.0;
        var errorAbsMagnitude = Math.Abs(error);
        var isLargeCorrection = errorAbsMagnitude > maxAcceptableErrorCm;

        Logging.Debug<RouteController>($"Sensor {sensorId}: accepted for active cycle, remaining pending sensors=-.");
        Logging.Debug<RouteController>(
            $"Sensor {sensorId}: recal=yes, pos={currentPos:F1} cm, err={error:F1} cm (magnitude={errorAbsMagnitude:F1} cm), " +
            $"active={activation.ActiveFromWaypointId ?? "-"} (from {previousPos:F1} cm), " +
            $"forcedForwardLegSync={(activation.ForcedForwardLegSync ? "yes" : "no")}, " +
            $"largeCorrection={(isLargeCorrection ? "YES" : "no")} (threshold={maxAcceptableErrorCm:F1} cm).");

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
                        $"Sensor {sensorId}: active cycle cancellation triggered for immediate replan.");
                }
            }
            catch (ObjectDisposedException)
            {
                // Race during disposal or cycle handover.
            }
        }

        Logging.DebugExtended<RouteController>(
            $"RouteTick: pos={currentPos:F1} cm, speed={Math.Max(0, BoundTrain.SpeedV)} km/h, cycle={cycleLabel}");
        Logging.DebugExtended<RouteController>(
            $"ApplyStep: delta=0.0 cm, pos={previousPos:F1}->{currentPos:F1} cm, " +
            $"traj={Math.Max(0, BoundTrain.SpeedV)} km/h, effective={Math.Max(0, BoundTrain.SpeedV)} km/h, " +
            $"sensor={sensorId}, recalibrated=yes, cycle={cycleLabel}.");
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
                await BoundTrain.SetSpeedVAsync(0, runToken).ConfigureAwait(false);
                await WaitForRouteChangeAsync(runToken).ConfigureAwait(false);
                continue;
            }

            if (state.ActiveRouteIndex is null)
            {
                await BoundTrain.SetSpeedVAsync(0, runToken).ConfigureAwait(false);
                await WaitForRouteChangeAsync(runToken).ConfigureAwait(false);
                continue;
            }

            var activeIndex = state.ActiveRouteIndex.Value;
            var activeLeg = snapshot.RouteLegs[activeIndex];
            LogActiveCycleIfChanged(activeLeg);
            var legStartCm = ComputeLegStart(snapshot.RouteLegs, activeIndex);
            var legEndCm = legStartCm + activeLeg.DistanceCm;
            var remainingDistanceCm = Math.Max(1.0, legEndCm - state.HeadPositionCm);

            var targetSpeedKmh = ResolveTargetSpeed(snapshot.RouteLegs, activeIndex, state, legStartCm);

            // Safety break: if target speed is 0 and we're very close to the end, wait for next route
            // This prevents tight spinning loops when a cycle completes with minimal remaining distance
            if (targetSpeedKmh == 0 && remainingDistanceCm <= 1.5)
            {
                await BoundTrain.SetSpeedVAsync(0, runToken).ConfigureAwait(false);
                await Task.Delay(50, runToken).ConfigureAwait(false);  // Minimal delay to prevent CPU spinning
                continue;
            }

            var cycle = new RouteCycle(
                FromWaypointId: activeLeg.FromWaypointId,
                ToWaypointId: activeLeg.ToWaypointId,
                DistanceCm: remainingDistanceCm,
                AllowedSpeedKmh: targetSpeedKmh,
                DriveProfile: activeLeg.DriveProfile);

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
        var targetSpeed = Math.Max(0, (int)Math.Round(activeLeg.MaxSpeedKmh, MidpointRounding.AwayFromZero));

        // Safety-first lookahead: if the next leg is slower, cap current target to avoid late braking.
        if (activeIndex < routeLegs.Count - 1)
        {
            var nextSpeed = Math.Max(0, (int)Math.Round(routeLegs[activeIndex + 1].MaxSpeedKmh, MidpointRounding.AwayFromZero));
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
                var previousSpeed = Math.Max(0, (int)Math.Round(previousLeg.MaxSpeedKmh, MidpointRounding.AwayFromZero));
                var trainClearsWaypointCm = activeLegStartCm + Math.Max(0.0, state.TrainLengthCm);
                if (state.HeadPositionCm < trainClearsWaypointCm)
                    targetSpeed = Math.Min(targetSpeed, previousSpeed);
            }
        }

        // Vorlauf-Bremsung: Wenn ein kommender StopPoint nahe genug ist, bereits im vorherigen Leg bremsen.
        var distanceToNextStopPointCm = TryGetDistanceToNextStopPoint(routeLegs, activeIndex, state.HeadPositionCm);
        if (distanceToNextStopPointCm is > 0)
        {
            var currentSpeedKmh = Math.Max(0, BoundTrain.SpeedV);
            var estimatedBrakeDistanceCm = EstimateBrakeDistanceCm(currentSpeedKmh, AccelerationMs2);
            if (distanceToNextStopPointCm.Value <= estimatedBrakeDistanceCm + StopPointBrakeSafetyMarginCm)
            {
                targetSpeed = 0;
            }
        }

        // Impliziter End-Halt: Im letzten RouteLeg muss zum Tabellenende regulär auf 0 gebremst werden,
        // auch wenn kein expliziter StopPoint existiert.
        if (activeIndex == routeLegs.Count - 1)
        {
            var currentSpeedKmh = Math.Max(0, BoundTrain.SpeedV);
            var estimatedBrakeDistanceCm = EstimateBrakeDistanceCm(currentSpeedKmh, AccelerationMs2);
            var distanceToImplicitEndCm = Math.Max(0.0, activeLegStartCm + activeLeg.DistanceCm - state.HeadPositionCm);

            if (distanceToImplicitEndCm <= estimatedBrakeDistanceCm + StopPointBrakeSafetyMarginCm)
            {
                targetSpeed = 0;
            }
        }

        return targetSpeed;
    }

    private static double? TryGetDistanceToNextStopPoint(
        IReadOnlyList<RouteLeg> routeLegs,
        int activeIndex,
        double headPositionCm)
    {
        var cumulativeStartCm = 0.0;
        for (var i = 0; i < activeIndex; i++)
            cumulativeStartCm += routeLegs[i].DistanceCm;

        var runningStartCm = cumulativeStartCm;
        for (var i = activeIndex; i < routeLegs.Count; i++)
        {
            var leg = routeLegs[i];
            if (leg.StopPoint is not null)
            {
                var stopAbsCm = runningStartCm + leg.StopPoint.OffsetCm;
                var distanceToStopCm = stopAbsCm - headPositionCm;
                if (distanceToStopCm > 0)
                    return distanceToStopCm;
            }

            runningStartCm += leg.DistanceCm;
        }

        return null;
    }

    private static double EstimateBrakeDistanceCm(int currentSpeedKmhPrototype, double decelerationMs2)
    {
        if (currentSpeedKmhPrototype <= 0)
            return 0.0;

        var a = decelerationMs2 <= 0 ? 0.55 : decelerationMs2;
        var vMs = currentSpeedKmhPrototype / 3.6;
        var distancePrototypeM = (vMs * vMs) / (2.0 * a);
        return distancePrototypeM * 100.0 / DefaultScale;
    }

    private static double ComputeLegStart(IReadOnlyList<RouteLeg> legs, int legIndex)
    {
        var start = 0.0;
        for (var i = 0; i < legIndex; i++)
            start += legs[i].DistanceCm;

        return start;
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
            $"Route active: {activeLeg.FromWaypointId}->{activeLeg.ToWaypointId}, dist={activeLeg.DistanceCm:F1} cm, allowed={activeLeg.MaxSpeedKmh:F0} km/h.");
        Logging.DebugExtended<RouteController>(
            $"Active cycle payload: pendingSensors={activeLeg.SensorMarkers?.Count ?? 0}, pendingActions=0.");
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

        Logging.Debug<RouteController>($"Active cycle cleared (previous={previousFrom ?? "-"}).");
    }

    private void OnProgressTick(TrainDrivingProgressTick tick)
    {
        var previousPos = _service.GetRuntimeState().HeadPositionCm;
        _service.AdvanceByDelta(tick.DeltaCmModel);
        var snapshot = _service.GetSnapshot();
        var state = snapshot.RuntimeState;
        var currentPos = state.HeadPositionCm;

        var cycleLabel = TryGetCycleLabel(state.ActiveRouteIndex, snapshot.RouteLegs);
        Logging.DebugExtended<RouteController>(
            $"RouteTick: pos={currentPos:F1} cm, speed={tick.CommandedSpeedKmhPrototype} km/h, cycle={cycleLabel}");

        Logging.DebugExtended<RouteController>(
            $"ApplyStep: delta={tick.DeltaCmModel:F1} cm, pos={previousPos:F1}->{currentPos:F1} cm, " +
            $"traj={tick.CommandedSpeedKmhPrototype} km/h, effective={tick.CommandedSpeedKmhPrototype} km/h, sensor=-, recal=no, cycle={cycleLabel}.");

        // Dynamische Replanung innerhalb eines laufenden Fahrzyklus:
        // Wenn sich durch fortschreitende Position das Bremsziel (StopPoint oder impliziter End-Halt)
        // auf eine niedrigere Zielgeschwindigkeit verschiebt, muss der aktive Zyklus abgebrochen
        // und als Bremsfahrt neu geplant werden.
        if (state.ActiveRouteIndex is { } activeIndex && activeIndex >= 0 && activeIndex < snapshot.RouteLegs.Count)
        {
            var activeLeg = snapshot.RouteLegs[activeIndex];
            var legStartCm = ComputeLegStart(snapshot.RouteLegs, activeIndex);
            var desiredTargetSpeedKmh = ResolveTargetSpeed(snapshot.RouteLegs, activeIndex, state, legStartCm);
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
                    $"Dynamic replan requested: commanded={tick.CommandedSpeedKmhPrototype} km/h, cycleTarget={activeCycleTarget} km/h, desired={desiredTargetSpeedKmh} km/h, " +
                    $"cycle={activeLeg.FromWaypointId}->{activeLeg.ToWaypointId}, pos={currentPos:F1} cm.");

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

    private static string TryGetCycleLabel(int? activeIndex, IReadOnlyList<RouteLeg> legs)
    {
        if (activeIndex is null || activeIndex < 0 || activeIndex >= legs.Count)
            return "-";

        var leg = legs[activeIndex.Value];
        return $"{leg.FromWaypointId}->{leg.ToWaypointId}";
    }

    private RouteLeg ResolveDynamicRoute(DynamicRouteRequest request)
    {
        if (_routeLegResolver is null)
        {
            throw new InvalidOperationException(
                "Dynamic route requests require a route definition service. Use RouteController(train, IRouteDefinitionService, ...)." );
        }

        return _routeLegResolver.Resolve(request);
    }

    private IReadOnlyList<RouteLeg> ResolveDynamicRoutes(IReadOnlyList<DynamicRouteRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var resolved = new List<RouteLeg>(requests.Count);
        foreach (var request in requests)
            resolved.Add(ResolveDynamicRoute(request));

        return resolved;
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
