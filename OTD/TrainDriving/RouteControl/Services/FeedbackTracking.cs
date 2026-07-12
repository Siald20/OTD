// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using OTD.TrainDriving.RouteControl.Domain;

namespace OTD.TrainDriving.RouteControl.Services;

internal readonly record struct NextExpectedInput(int InputId, double AnchorCm);

internal readonly record struct UnexpectedAheadInputMatch(
    double ActivatedAnchorCm,
    NextExpectedInput ExpectedInput,
    double DeltaToExpectedCm,
    bool IsExpectedInputActivatedEarly,
    double HeadToInputDistanceCm);

/// <summary>
/// Bündelt Feedback-Tracking-Funktionen für RouteControl:
/// - Duplikat-Schutz der Kalibrierung pro aktivem Waypoint
/// - Bestimmung des naechst erwarteten Feedbacks entlang der Rest-Route
/// - Erkennung unerwarteter Feedbackaktivierungen vor der Zugspitze
/// </summary>
internal sealed class FeedbackTracking
{
    private const double AheadEpsilonCm = 0.1;

    private readonly HashSet<int> _calibratedInputs = [];
    private string? _trackingForWaypointId;

    public void UpdateActiveWaypoint(string? newWaypointId)
    {
        if (!string.Equals(_trackingForWaypointId, newWaypointId, StringComparison.OrdinalIgnoreCase))
        {
            _calibratedInputs.Clear();
            _trackingForWaypointId = newWaypointId;
        }
    }

    public bool IsAlreadyCalibratedForCurrentWaypoint(int inputId) => _calibratedInputs.Contains(inputId);

    public void MarkCalibrated(int inputId) => _calibratedInputs.Add(inputId);

    public bool TryGetNextExpectedInputAhead(IReadOnlyList<RouteLeg> routeLegs, double headPositionCm, out NextExpectedInput expected)
        => TryGetNextExpectedInputAhead(routeLegs, headPositionCm, trainLengthCm: 0.0, out expected);

    public bool TryGetNextExpectedInputAhead(
        IReadOnlyList<RouteLeg> routeLegs,
        double headPositionCm,
        double trainLengthCm,
        out NextExpectedInput expected)
    {
        expected = default;
        var bestAnchor = double.MaxValue;
        var bestinputId = -1;
        var bestLegIndex = -1;
        var cumulative = 0.0;

        for (var legIndex = 0; legIndex < routeLegs.Count; legIndex++)
        {
            var markers = routeLegs[legIndex].FeedbackInputActivationPoints;
            if (markers is not null)
            {
                foreach (var marker in markers)
                {
                    var anchor = cumulative + marker.OffsetCm;
                    if (anchor <= headPositionCm + AheadEpsilonCm)
                        continue;

                    // A feedback that lies inside the current occupied train body must not be treated
                    // as the next StuckAlert target. Keep markers exactly on the current head position,
                    // but skip everything that is still within the train length in front of it.
                    if (trainLengthCm > AheadEpsilonCm)
                    {
                        var distanceFromHead = anchor - headPositionCm;
                        if (distanceFromHead > AheadEpsilonCm && distanceFromHead < trainLengthCm - AheadEpsilonCm)
                            continue;
                    }

                    if (anchor >= bestAnchor)
                        continue;

                    bestAnchor = anchor;
                    bestinputId = marker.FeedbackId;
                    bestLegIndex = legIndex;
                }
            }

            cumulative += routeLegs[legIndex].DistanceCm;
        }

        if (bestLegIndex < 0)
            return false;

        expected = new NextExpectedInput(bestinputId, bestAnchor);
        return true;
    }

    public bool TryMatchUnexpectedAheadInput(
        int activatedinputId,
        double activatedAnchorCm,
        double headPositionCm,
        IReadOnlyList<RouteLeg> routeLegs,
        double toleranceCm,
        out UnexpectedAheadInputMatch match)
        => TryMatchUnexpectedAheadInput(
            activatedinputId,
            activatedAnchorCm,
            headPositionCm,
            expectedSearchAnchorCm: headPositionCm,
            routeLegs,
            toleranceCm,
            trainLengthCm: 0.0,
            out match);

    public bool TryMatchUnexpectedAheadInput(
        int activatedinputId,
        double activatedAnchorCm,
        double headPositionCm,
        double expectedSearchAnchorCm,
        IReadOnlyList<RouteLeg> routeLegs,
        double toleranceCm,
        double trainLengthCm,
        out UnexpectedAheadInputMatch match)
    {
        match = default;
        if (activatedAnchorCm <= expectedSearchAnchorCm + AheadEpsilonCm)
            return false;


        if (!TryGetNextExpectedInputAhead(routeLegs, expectedSearchAnchorCm, trainLengthCm, out var expected))
            return false;

        if (activatedinputId == expected.InputId)
        {
            var headToInput = expected.AnchorCm - expectedSearchAnchorCm;
            if (headToInput > toleranceCm)
            {
                match = new UnexpectedAheadInputMatch(
                    activatedAnchorCm,
                    expected,
                    DeltaToExpectedCm: 0.0,
                    IsExpectedInputActivatedEarly: true,
                    HeadToInputDistanceCm: headToInput);
                return true;
            }
            return false;
        }

        var deltaToExpectedCm = activatedAnchorCm - expected.AnchorCm;
        if (deltaToExpectedCm <= toleranceCm)
            return false;

        match = new UnexpectedAheadInputMatch(activatedAnchorCm, expected, deltaToExpectedCm,
            IsExpectedInputActivatedEarly: false, HeadToInputDistanceCm: 0.0);
        return true;
    }
}

