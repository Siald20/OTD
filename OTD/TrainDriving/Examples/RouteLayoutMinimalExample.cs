// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Services;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Minimales, hardwarefreies Beispiel fuer das topologiebasierte RailwayLayout:
/// - RouteLegs werden automatisch aus Waypoints generiert
/// - FeedbackActivationPoint kommen ausschliesslich aus dem Layout
/// - routesegments.xml enthaelt nur Speed-/Default-Overrides
/// </summary>
public static class RouteLayoutMinimalExample
{
    public static void Run()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "otd-route-layout-minimal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var trackLayoutPath = Path.Combine(tempRoot, "railwaylayout.xml");
        var routeLegsPath = Path.Combine(tempRoot, "routesegments.xml");

        try
        {
            File.WriteAllText(trackLayoutPath, BuildRailwayLayoutXml());
            File.WriteAllText(routeLegsPath, BuildRouteLegsXml());

            var trackLayout = new XmlRailwayLayoutService(trackLayoutPath);
            var routeDefinitions = new XmlRouteDefinitionService(trackLayout, routeLegsPath);
            var resolver = new RouteLegResolver(routeDefinitions, trackLayout);

            var generatedLegs = trackLayout.GetAllGeneratedLegs().OrderBy(leg => leg.FromWaypointId).ThenBy(leg => leg.ToWaypointId).ToList();
            AssertEqual(9, generatedLegs.Count, "generated leg count");

            var legAm = resolver.Resolve(new DynamicRouteRequest(RouteTravelDirection.AlongLine, "A", "MID"));
            var legMw = resolver.Resolve(new DynamicRouteRequest(RouteTravelDirection.AlongLine, "MID", "W"));
            var legWm = resolver.Resolve(new DynamicRouteRequest(RouteTravelDirection.AlongLine, "W", "MID"));
            var legWb2 = resolver.Resolve(new DynamicRouteRequest(RouteTravelDirection.AlongLine, "W", "B2"));
            var legB2W = resolver.Resolve(new DynamicRouteRequest(RouteTravelDirection.AlongLine, "B2", "W"));
            var legAw = resolver.Resolve(new DynamicRouteRequest(RouteTravelDirection.AlongLine, "A", "W"));

            AssertLeg(legAm, expectedDistanceCm: 60, expectedSpeedKmh: 40, (30, 0, FeedbackType.OccupancyFeedback));
            AssertLeg(legMw, expectedDistanceCm: 90, expectedSpeedKmh: 50, (145, 85, FeedbackType.ContactFeedback));
            AssertLeg(legWm, expectedDistanceCm: 90, expectedSpeedKmh: 50, (145, 5, FeedbackType.ContactFeedback));
            AssertLeg(legWb2, expectedDistanceCm: 110, expectedSpeedKmh: 30, (32, 0, FeedbackType.OccupancyFeedback));
            AssertLeg(legB2W, expectedDistanceCm: 110, expectedSpeedKmh: 30);
            AssertLeg(legAw, expectedDistanceCm: 150, expectedSpeedKmh: 40, (30, 0, FeedbackType.OccupancyFeedback), (145, 145, FeedbackType.ContactFeedback));

            Console.WriteLine("[Minimal] Automatisch generierte Legs:");
            foreach (var leg in generatedLegs)
                PrintLeg(leg);

            Console.WriteLine("[Minimal] Aufgeloeste RouteLegs:");
            PrintRouteLeg(legAm);
            PrintRouteLeg(legMw);
            PrintRouteLeg(legWm);
            PrintRouteLeg(legWb2);
            PrintRouteLeg(legB2W);
            PrintRouteLeg(legAw);
            Console.WriteLine("[Minimal] Alle Assertions erfolgreich.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Temp-Ordner bleibt im Fehlerfall fuer Diagnose erhalten.
            }
        }
    }

    private static void AssertLeg(RouteLeg leg, int expectedDistanceCm, double expectedSpeedKmh, params (int FeedbackId, int OffsetCm, FeedbackType Type)[] expectedFeedbacks)
    {
        AssertEqual(expectedDistanceCm, leg.DistanceCm, $"distance {leg.FromWaypointId}->{leg.ToWaypointId}");
        AssertEqual(expectedSpeedKmh, leg.MaxSpeedKmh, $"speed {leg.FromWaypointId}->{leg.ToWaypointId}");
        var actualFeedbacks = leg.FeedbackInputActivationPoints?.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.FeedbackId).ToArray() ?? [];
        AssertEqual(expectedFeedbacks.Length, actualFeedbacks.Length, $"feedback count {leg.FromWaypointId}->{leg.ToWaypointId}");

        for (var i = 0; i < expectedFeedbacks.Length; i++)
        {
            var expected = expectedFeedbacks[i];
            var actual = actualFeedbacks[i];
            AssertEqual(expected.FeedbackId, actual.FeedbackId, $"feedback id[{i}] {leg.FromWaypointId}->{leg.ToWaypointId}");
            AssertEqual(expected.OffsetCm, actual.OffsetCm, $"feedback offset[{i}] {leg.FromWaypointId}->{leg.ToWaypointId}");
            AssertEqual(expected.Type, actual.Type, $"feedback type[{i}] {leg.FromWaypointId}->{leg.ToWaypointId}");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
        where T : notnull
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"Assertion failed for {label}: expected='{expected}', actual='{actual}'.");
    }

    private static void PrintLeg(GeneratedTrackLeg leg)
    {
        var feedbackSummary = leg.FeedbackActivationPoints is null
            ? "-"
            : string.Join(", ", leg.FeedbackActivationPoints.Select(marker => $"{marker.FeedbackId}@{marker.OffsetCm}cm/{marker.Type}"));
        Console.WriteLine($"  {leg.FromWaypointId}->{leg.ToWaypointId}, dist={leg.DistanceCm}cm, feedbacks=[{feedbackSummary}]");
    }

    private static void PrintRouteLeg(RouteLeg leg)
    {
        var feedbackSummary = leg.FeedbackInputActivationPoints is null
            ? "-"
            : string.Join(", ", leg.FeedbackInputActivationPoints.Select(marker => $"{marker.FeedbackId}@{marker.OffsetCm}cm/{marker.Type}"));
        Console.WriteLine($"  {leg.FromWaypointId}->{leg.ToWaypointId}, dist={leg.DistanceCm}cm, vmax={leg.MaxSpeedKmh:F0}km/h, feedbacks=[{feedbackSummary}]");
    }

    private static string BuildRailwayLayoutXml()
    {
        return """
               <railwaylayout version="2">
                   <trackelements>
                       <trackelement id="seg_A" type="simpletrack" from="nA" to="nW" length_cm="150" description="A nach W"/>
                       <trackelement id="seg_A_rev" type="simpletrack" from="nW" to="nA" length_cm="150" description="W nach A"/>
                       <trackelement id="SW1" type="turnout" description="Verzweigung bei W">
                           <path id="sw1_main" from="nW" to="nB1" length_cm="95"/>
                           <path id="sw1_branch" from="nW" to="nB2" length_cm="110"/>
                       </trackelement>
                       <trackelement id="SW1_rev" type="turnout" description="Rückwärts-Verzweigung">
                           <path id="sw1_main_rev" from="nB1" to="nW" length_cm="95"/>
                           <path id="sw1_branch_rev" from="nB2" to="nW" length_cm="110"/>
                       </trackelement>
                   </trackelements>

                    <feedbacks>
                        <occupancy id="sec_A" detectorId="30" host="seg_A"/>
                        <contact id="pt_145" detectorId="145" host="seg_A" offset_cm="145"/>
                        <occupancy id="sec_branch" detectorId="32" host="sw1_branch"/>
                    </feedbacks>

                   <waypoints>
                       <waypoint id="A" node="nA"/>
                       <waypoint id="MID" host="seg_A" offset_cm="60"/>
                       <waypoint id="W" node="nW"/>
                       <waypoint id="B1" node="nB1"/>
                       <waypoint id="B2" node="nB2"/>
                   </waypoints>
               </railwaylayout>
               """;
    }

    private static string BuildRouteLegsXml()
    {
        return """
               <routesegments>
                   <routesegment from="A" to="MID">
                       <speedlimits>
                           <speedlimit speedClass="default" speed_kmh="40"/>
                       </speedlimits>
                   </routesegment>
                   <routesegment from="MID" to="W">
                       <speedlimits>
                           <speedlimit speedClass="default" speed_kmh="50"/>
                       </speedlimits>
                   </routesegment>
                   <routesegment from="W" to="B1">
                       <speedlimits>
                           <speedlimit speedClass="default" speed_kmh="60"/>
                       </speedlimits>
                   </routesegment>
                   <routesegment from="W" to="B2">
                       <speedlimits>
                           <speedlimit speedClass="default" speed_kmh="30"/>
                       </speedlimits>
                   </routesegment>
               </routesegments>
               """;
    }
}

