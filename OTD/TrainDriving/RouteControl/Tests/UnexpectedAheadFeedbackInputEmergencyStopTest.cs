// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Runtime;
using OTD.TrainDriving.RouteControl.Services;

namespace OTD.TrainDriving.RouteControl.Tests;

/// <summary>
/// Integrationsnahe Assertions fuer den Unexpected-Ahead-Feedback-Guard in RouteTableService.
/// </summary>
public static class UnexpectedAheadFeedbackInputEmergencyStopTest
{
    public static void RunAll()
    {
        Console.WriteLine("=== UnexpectedAheadFeedbackInputEmergencyStopTest ===");

        var previousEnabled = RouteControlSafetyOptions.EnableUnexpectedAheadFeedbackInputEmergencyStop;
        var previousTolerance = RouteControlSafetyOptions.UnexpectedAheadFeedbackInputToleranceCm;

        try
        {
            RouteControlSafetyOptions.EnableUnexpectedAheadFeedbackInputEmergencyStop = true;
            RouteControlSafetyOptions.UnexpectedAheadFeedbackInputToleranceCm = 30.0;

            CaseUnexpectedAheadFeedbackRequestsEmergencyStop();
            CaseUnexpectedAheadFeedbackWithinToleranceDoesNotRequestEmergencyStop();
            CaseUnexpectedAheadFeedbackAtToleranceBoundaryDoesNotRequestEmergencyStop();
            CaseExpectedFeedbackActivatedTooEarlyRequestsEmergencyStop();
            CaseExpectedFeedbackActivatedWithinToleranceDoesNotRequestEmergencyStop();
            CaseExpectedNextFeedbackDoesNotRequestEmergencyStop();
            CaseAgainstToAlongTransitionReanchorsAtLegStart();
            CaseGuardDisabledDoesNotRequestEmergencyStop();
            CaseFeedbackBehindTrainDoesNotRequestEmergencyStop();

            Console.WriteLine("=== Ergebnis: PASS ===");
        }
        finally
        {
            RouteControlSafetyOptions.EnableUnexpectedAheadFeedbackInputEmergencyStop = previousEnabled;
            RouteControlSafetyOptions.UnexpectedAheadFeedbackInputToleranceCm = previousTolerance;
        }
    }

    private static void CaseUnexpectedAheadFeedbackRequestsEmergencyStop()
    {
        var service = BuildTwoLegService();

        // Head steht am Start (0cm). Nächster erwarteter Feedback liegt bei 80cm (Feedback 101).
        // Feedback 303 liegt weiter vorne bei 170cm und soll daher als unerwartet gelten.
        var activation = service.OnFeedbackInputActivated(303, estimatedHeadPositionCm: 0.0);

        AssertFalse(activation.Accepted, "Unexpected feedback must not be accepted for calibration.");
        AssertTrue(activation.EmergencyStopRequested, "Unexpected ahead feedback must request emergency stop.");
        AssertEqual(101, activation.ExpectedFeedbackInputId, "Expected feedback input id mismatch.");
        AssertEqual(170.0, activation.ActivatedFeedbackInputAnchorCm, "Activated anchor mismatch.");
        AssertEqual(80.0, activation.ExpectedFeedbackInputAnchorCm, "Expected anchor mismatch.");

        Console.WriteLine("  PASS CaseUnexpectedAheadFeedbackRequestsEmergencyStop");
    }

    private static void CaseExpectedNextFeedbackDoesNotRequestEmergencyStop()
    {
        var service = BuildTwoLegService();

        // Feedback 101 liegt bei 80cm. Zug bei 55cm => Distanz=25cm <= Toleranz 30cm → kein Notstopp.
        var activation = service.OnFeedbackInputActivated(101, estimatedHeadPositionCm: 55.0);

        AssertTrue(activation.Accepted, "Expected next feedback within tolerance should be accepted.");
        AssertFalse(activation.EmergencyStopRequested, "Expected next feedback within tolerance must not request emergency stop.");

        Console.WriteLine("  PASS CaseExpectedNextFeedbackDoesNotRequestEmergencyStop");
    }

    private static void CaseUnexpectedAheadFeedbackWithinToleranceDoesNotRequestEmergencyStop()
    {
        var service = BuildToleranceService();

        // Erwarteter Feedback 111@80cm, unerwarteter Feedback 222@95cm => Delta=15cm <= Toleranz 30cm.
        var activation = service.OnFeedbackInputActivated(222, estimatedHeadPositionCm: 0.0);

        AssertTrue(activation.Accepted, "Feedback within tolerance is treated as normal calibration input.");
        AssertFalse(activation.EmergencyStopRequested, "Unexpected feedback within tolerance must not request emergency stop.");

        Console.WriteLine("  PASS CaseUnexpectedAheadFeedbackWithinToleranceDoesNotRequestEmergencyStop");
    }

    private static void CaseUnexpectedAheadFeedbackAtToleranceBoundaryDoesNotRequestEmergencyStop()
    {
        var service = BuildToleranceBoundaryService();

        // Erwartet 111@80cm, aktiviert 333@110cm => Delta=30cm == Toleranz.
        var activation = service.OnFeedbackInputActivated(333, estimatedHeadPositionCm: 0.0);

        AssertTrue(activation.Accepted, "Feedback at tolerance boundary is treated as normal calibration input.");
        AssertFalse(activation.EmergencyStopRequested, "Delta equal tolerance must not request emergency stop.");

        Console.WriteLine("  PASS CaseUnexpectedAheadFeedbackAtToleranceBoundaryDoesNotRequestEmergencyStop");
    }

    private static void CaseExpectedFeedbackActivatedTooEarlyRequestsEmergencyStop()
    {
        var service = BuildTwoLegService();

        // Head bei 0cm. Nächster erwarteter Feedback 101@80cm => Distanz=80cm > Toleranz 30cm.
        // Feedback 101 wird manuell aktiviert → Zug ist noch viel zu weit weg.
        var activation = service.OnFeedbackInputActivated(101, estimatedHeadPositionCm: 0.0);

        AssertFalse(activation.Accepted, "Expected feedback activated too early must not be accepted.");
        AssertTrue(activation.EmergencyStopRequested, "Expected feedback activated too early must request emergency stop.");
        AssertTrue(activation.IsExpectedFeedbackInputActivatedEarly, "IsExpectedFeedbackInputActivatedEarly must be true.");
        AssertEqual(101, activation.ExpectedFeedbackInputId, "Expected feedback input id mismatch.");

        Console.WriteLine("  PASS CaseExpectedFeedbackActivatedTooEarlyRequestsEmergencyStop");
    }

    private static void CaseExpectedFeedbackActivatedWithinToleranceDoesNotRequestEmergencyStop()
    {
        var service = BuildTwoLegService();

        // Head bei 60cm. Nächster erwarteter Feedback 101@80cm => Distanz=20cm <= Toleranz 30cm.
        // Feedback 101 wird aktiviert → innerhalb Toleranz, kein Notstopp.
        var activation = service.OnFeedbackInputActivated(101, estimatedHeadPositionCm: 60.0);

        AssertTrue(activation.Accepted, "Expected feedback within tolerance should be accepted for calibration.");
        AssertFalse(activation.EmergencyStopRequested, "Expected feedback within tolerance must not request emergency stop.");
        AssertFalse(activation.IsExpectedFeedbackInputActivatedEarly, "IsExpectedFeedbackInputActivatedEarly must be false when within tolerance.");

        Console.WriteLine("  PASS CaseExpectedFeedbackActivatedWithinToleranceDoesNotRequestEmergencyStop");
    }

    private static void CaseAgainstToAlongTransitionReanchorsAtLegStart()
    {
        var service = BuildAgainstToAlongTransitionService();

        // Active Leg ist noch AgainstLine (Kopf knapp vor Ende). Feedback im ersten AlongLine-Leg
        // bleibt bei seiner echten physischen Position (120cm), nicht auf Leg-Beginn gezogen.
        var activation = service.OnFeedbackInputActivated(777, estimatedHeadPositionCm: 99.9);

        AssertTrue(activation.Accepted, "Transition feedback should be accepted.");
        AssertFalse(activation.EmergencyStopRequested, "Transition feedback must not request emergency stop.");
        AssertTrue(activation.ForcedForwardLegSync, "Transition feedback should force forward leg sync.");
        AssertEqual(120.0, activation.AnchorPositionCm, "AgainstLine->AlongLine: position remains at actual feedback location (100cm leg-start + 20cm offset).");

        Console.WriteLine("  PASS CaseAgainstToAlongTransitionReanchorsAtLegStart");
    }

    private static void CaseGuardDisabledDoesNotRequestEmergencyStop()
    {
        var previousEnabled = RouteControlSafetyOptions.EnableUnexpectedAheadFeedbackInputEmergencyStop;
        try
        {
            RouteControlSafetyOptions.EnableUnexpectedAheadFeedbackInputEmergencyStop = false;
            var service = BuildTwoLegService();

            // Gleicher unerwarteter Feedback wie in Case 1; bei deaktiviertem Guard darf kein E-Stop angefordert werden.
            var activation = service.OnFeedbackInputActivated(303, estimatedHeadPositionCm: 0.0);

            AssertFalse(activation.EmergencyStopRequested, "Guard disabled: emergency stop must not be requested.");

            Console.WriteLine("  PASS CaseGuardDisabledDoesNotRequestEmergencyStop");
        }
        finally
        {
            RouteControlSafetyOptions.EnableUnexpectedAheadFeedbackInputEmergencyStop = previousEnabled;
        }
    }

    private static void CaseFeedbackBehindTrainDoesNotRequestEmergencyStop()
    {
        var service = BuildTwoLegService();

        // Kopfposition in Leg 2, Feedback 101 liegt bei 80cm in Leg 1 und damit hinter der Zugspitze.
        var activation = service.OnFeedbackInputActivated(101, estimatedHeadPositionCm: 120.0);

        AssertFalse(activation.Accepted, "Feedback behind train should not be accepted in active-leg processing.");
        AssertFalse(activation.EmergencyStopRequested, "Feedback behind train must not request emergency stop.");

        Console.WriteLine("  PASS CaseFeedbackBehindTrainDoesNotRequestEmergencyStop");
    }

    private static RouteTableService BuildTwoLegService()
    {
        var service = new RouteTableService();

        var leg1 = new RouteLeg(RouteTravelDirection.AlongLine, "A", "B", MaxSpeedKmh: 60)
        {
            DistanceCm = 100,
            FeedbackInputActivationPoints =
            [
                new FeedbackActivationPoint(101, 80, FeedbackType.ContactFeedback)
            ]
        };

        var leg2 = new RouteLeg(RouteTravelDirection.AlongLine, "B", "C", MaxSpeedKmh: 60)
        {
            DistanceCm = 100,
            FeedbackInputActivationPoints =
            [
                new FeedbackActivationPoint(202, 20, FeedbackType.ContactFeedback),
                new FeedbackActivationPoint(303, 70, FeedbackType.ContactFeedback)
            ]
        };

        service.AddRoutes([leg1, leg2]);
        return service;
    }

    private static RouteTableService BuildToleranceService()
    {
        var service = new RouteTableService();

        var leg = new RouteLeg(RouteTravelDirection.AlongLine, "A", "B", MaxSpeedKmh: 60)
        {
            DistanceCm = 100,
            FeedbackInputActivationPoints =
            [
                new FeedbackActivationPoint(111, 80, FeedbackType.ContactFeedback),
                new FeedbackActivationPoint(222, 95, FeedbackType.ContactFeedback)
            ]
        };

        service.AddRoutes([leg]);
        return service;
    }

    private static RouteTableService BuildToleranceBoundaryService()
    {
        var service = new RouteTableService();

        var leg = new RouteLeg(RouteTravelDirection.AlongLine, "A", "B", MaxSpeedKmh: 60)
        {
            DistanceCm = 130,
            FeedbackInputActivationPoints =
            [
                new FeedbackActivationPoint(111, 80, FeedbackType.ContactFeedback),
                new FeedbackActivationPoint(333, 110, FeedbackType.ContactFeedback)
            ]
        };

        service.AddRoutes([leg]);
        return service;
    }

    private static RouteTableService BuildAgainstToAlongTransitionService()
    {
        var service = new RouteTableService();

        var legAgainst = new RouteLeg(RouteTravelDirection.AgainstLine, "A", "B", MaxSpeedKmh: 60)
        {
            DistanceCm = 100
        };

        var legAlong = new RouteLeg(RouteTravelDirection.AlongLine, "B", "C", MaxSpeedKmh: 60)
        {
            DistanceCm = 100,
            FeedbackInputActivationPoints =
            [
                // Marker liegt 20 cm nach Leg-Beginn, Re-Ankerung muss trotzdem auf 100.1 cm gehen.
                new FeedbackActivationPoint(777, 20, FeedbackType.ContactFeedback)
            ]
        };

        service.AddRoutes([legAgainst, legAlong]);
        return service;
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual(int expected, int? actual, string message)
    {
        if (actual is null || actual.Value != expected)
            throw new InvalidOperationException($"{message} expected={expected}, actual={(actual is null ? "null" : actual.Value)}");
    }

    private static void AssertEqual(double expected, double? actual, string message)
    {
        if (actual is null || Math.Abs(actual.Value - expected) > 0.001)
            throw new InvalidOperationException($"{message} expected={expected:F1}, actual={(actual is null ? "null" : actual.Value.ToString("F1"))}");
    }
}







