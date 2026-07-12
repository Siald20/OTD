// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Runtime;
using OTD.TrainDriving.RouteControl.Services;

namespace OTD.TrainDriving.RouteControl.Tests;

/// <summary>
/// Verifiziert die prozentbasierte StuckAlert-Absicherung fuer erwartete Rueckmelder.
/// </summary>
public static class StuckAlertEmergencyStopTest
{
    public static void RunAll()
    {
        Console.WriteLine("=== StuckAlertEmergencyStopTest ===");

        var previousEnabled = RouteControlSafetyOptions.EnableStuckAlertEmergencyStop;
        var previousTolerance = RouteControlSafetyOptions.StuckAlertTolerancePercent;

        try
        {
            RouteControlSafetyOptions.EnableStuckAlertEmergencyStop = true;
            RouteControlSafetyOptions.StuckAlertTolerancePercent = 20.0;

            CaseProgressBeyondExpectedFeedbackRequiresDistanceAndTimeSlack();
            CasePhysicalMinimumOverrunBlocksEarlyTrigger();
            CaseSameSlackWaitsLongerAtLowerSpeed();
            CaseDisabledGuardDoesNotRequestEmergencyStop();

            Console.WriteLine("=== Ergebnis: PASS ===");
        }
        finally
        {
            RouteControlSafetyOptions.EnableStuckAlertEmergencyStop = previousEnabled;
            RouteControlSafetyOptions.StuckAlertTolerancePercent = previousTolerance;
        }
    }

    private static void CaseProgressBeyondExpectedFeedbackRequiresDistanceAndTimeSlack()
    {
        var service = BuildService(expectedFeedbackAnchorCm: 5, maxSpeedKmh: 350);

        var firstAdvance = service.AdvancePosition(20.9);
        AssertNull(firstAdvance, "Progress before tolerance must not trigger StuckAlert.");

        var prematureAlert = service.AdvancePosition(21.1);
        AssertNull(prematureAlert, "Progress beyond tolerance must still wait for time slack.");

        Thread.Sleep(350);

        var alert = service.AdvancePosition(21.1);
        AssertNotNull(alert, "Progress beyond tolerance must trigger StuckAlert after time slack at high speed.");
        AssertEqual(101, alert?.ExpectedFeedbackInputId, "Expected feedback id mismatch.");
        AssertTrue(service.GetRuntimeState().SafetyStopInjected, "SafetyStopInjected must be set after StuckAlert.");

        Console.WriteLine("  PASS CaseProgressBeyondExpectedFeedbackRequiresDistanceAndTimeSlack");
    }

    private static void CasePhysicalMinimumOverrunBlocksEarlyTrigger()
    {
        var service = BuildService(expectedFeedbackAnchorCm: 5, maxSpeedKmh: 10);

        AssertNull(service.AdvancePosition(9.0), "4cm overrun must remain below the additive 16cm threshold.");

        Thread.Sleep(5600);

        AssertNull(service.AdvancePosition(9.0), "Even after the time slack expires, distance below 16cm must not trigger StuckAlert.");

        var alert = service.AdvancePosition(21.1);
        AssertNotNull(alert, "Once the additive 16cm threshold is exceeded, StuckAlert must trigger.");
        AssertEqual(101, alert?.ExpectedFeedbackInputId, "Expected feedback id mismatch.");

        Console.WriteLine("  PASS CasePhysicalMinimumOverrunBlocksEarlyTrigger");
    }

    private static void CaseSameSlackWaitsLongerAtLowerSpeed()
    {
        var fastService = BuildService(expectedFeedbackAnchorCm: 5, maxSpeedKmh: 350);
        var slowService = BuildService(expectedFeedbackAnchorCm: 5, maxSpeedKmh: 10);

        AssertNull(fastService.AdvancePosition(21.1), "Fast service must not trigger immediately.");
        AssertNull(slowService.AdvancePosition(21.1), "Slow service must not trigger immediately.");

        Thread.Sleep(300);

        var fastAlert = fastService.AdvancePosition(21.1);
        var slowAlert = slowService.AdvancePosition(21.1);
        AssertNotNull(fastAlert, "Same slack should trigger earlier at higher speed.");
        AssertNull(slowAlert, "Same slack should wait longer at lower speed (16cm / 3.19cm/s ≈ 5.01s).");

        Thread.Sleep(5400);

        var slowDelayedAlert = slowService.AdvancePosition(21.1);
        AssertNotNull(slowDelayedAlert, "Lower speed must eventually trigger after time slack.");

        Console.WriteLine("  PASS CaseSameSlackWaitsLongerAtLowerSpeed");
    }

    private static void CaseDisabledGuardDoesNotRequestEmergencyStop()
    {
        var previousEnabled = RouteControlSafetyOptions.EnableStuckAlertEmergencyStop;
        try
        {
            RouteControlSafetyOptions.EnableStuckAlertEmergencyStop = false;
            var service = BuildService(expectedFeedbackAnchorCm: 5, maxSpeedKmh: 350);

            var alert = service.AdvancePosition(6.5);
            AssertNull(alert, "Disabled StuckAlert guard must not trigger.");
            AssertFalse(service.GetRuntimeState().SafetyStopInjected, "SafetyStopInjected must remain false when guard is disabled.");

            Console.WriteLine("  PASS CaseDisabledGuardDoesNotRequestEmergencyStop");
        }
        finally
        {
            RouteControlSafetyOptions.EnableStuckAlertEmergencyStop = previousEnabled;
        }
    }

    private static RouteTableService BuildService(int expectedFeedbackAnchorCm, int maxSpeedKmh)
    {
        var service = new RouteTableService();

        var leg = new RouteLeg(RouteTravelDirection.AlongLine, "A", "B", MaxSpeedKmh: maxSpeedKmh)
        {
            DistanceCm = 40,
            FeedbackInputActivationPoints =
            [
                new FeedbackActivationPoint(101, expectedFeedbackAnchorCm)
            ]
        };

        service.AddRoutes([leg]);
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

    private static void AssertNull(object? value, string message)
    {
        if (value is not null)
            throw new InvalidOperationException(message);
    }

    private static void AssertNotNull(object? value, string message)
    {
        if (value is null)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual(int expected, int? actual, string message)
    {
        if (actual is null || actual.Value != expected)
            throw new InvalidOperationException($"{message} expected={expected}, actual={(actual is null ? "null" : actual.Value)}");
    }
}

