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
/// - SensorMarker kommen ausschliesslich aus dem Layout
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
            AssertEqual(8, generatedLegs.Count, "generated leg count");

            var legAm = resolver.Resolve(new DynamicRouteRequest("A", "MID"));
            var legMw = resolver.Resolve(new DynamicRouteRequest("MID", "W"));
            var legWm = resolver.Resolve(new DynamicRouteRequest("W", "MID"));
            var legWb2 = resolver.Resolve(new DynamicRouteRequest("W", "B2"));
            var legB2W = resolver.Resolve(new DynamicRouteRequest("B2", "W"));
            var legAw = resolver.Resolve(new DynamicRouteRequest("A", "W"));

            AssertLeg(legAm, expectedDistanceCm: 60, expectedSpeedKmh: 40, (30, 0, SensorType.OccupancyDetection));
            AssertLeg(legMw, expectedDistanceCm: 90, expectedSpeedKmh: 50, (145, 85, SensorType.TrackContact));
            AssertLeg(legWm, expectedDistanceCm: 90, expectedSpeedKmh: 50, (30, 0, SensorType.OccupancyDetection), (145, 5, SensorType.TrackContact));
            AssertLeg(legWb2, expectedDistanceCm: 110, expectedSpeedKmh: 30, (32, 0, SensorType.OccupancyDetection));
            AssertLeg(legB2W, expectedDistanceCm: 110, expectedSpeedKmh: 30, (32, 0, SensorType.OccupancyDetection));
            AssertLeg(legAw, expectedDistanceCm: 150, expectedSpeedKmh: 40, (30, 0, SensorType.OccupancyDetection), (145, 145, SensorType.TrackContact));

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

    private static void AssertLeg(RouteLeg leg, int expectedDistanceCm, double expectedSpeedKmh, params (int SensorId, int OffsetCm, SensorType Type)[] expectedSensors)
    {
        AssertEqual(expectedDistanceCm, leg.DistanceCm, $"distance {leg.FromWaypointId}->{leg.ToWaypointId}");
        AssertEqual(expectedSpeedKmh, leg.MaxSpeedKmh, $"speed {leg.FromWaypointId}->{leg.ToWaypointId}");
        var actualSensors = leg.SensorMarkers?.OrderBy(marker => marker.OffsetCm).ThenBy(marker => marker.SensorId).ToArray() ?? [];
        AssertEqual(expectedSensors.Length, actualSensors.Length, $"sensor count {leg.FromWaypointId}->{leg.ToWaypointId}");

        for (var i = 0; i < expectedSensors.Length; i++)
        {
            var expected = expectedSensors[i];
            var actual = actualSensors[i];
            AssertEqual(expected.SensorId, actual.SensorId, $"sensor id[{i}] {leg.FromWaypointId}->{leg.ToWaypointId}");
            AssertEqual(expected.OffsetCm, actual.OffsetCm, $"sensor offset[{i}] {leg.FromWaypointId}->{leg.ToWaypointId}");
            AssertEqual(expected.Type, actual.Type, $"sensor type[{i}] {leg.FromWaypointId}->{leg.ToWaypointId}");
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
        var sensorSummary = leg.SensorMarkers is null
            ? "-"
            : string.Join(", ", leg.SensorMarkers.Select(marker => $"{marker.SensorId}@{marker.OffsetCm}cm/{marker.Type}"));
        Console.WriteLine($"  {leg.FromWaypointId}->{leg.ToWaypointId}, dist={leg.DistanceCm}cm, sensors=[{sensorSummary}]");
    }

    private static void PrintRouteLeg(RouteLeg leg)
    {
        var sensorSummary = leg.SensorMarkers is null
            ? "-"
            : string.Join(", ", leg.SensorMarkers.Select(marker => $"{marker.SensorId}@{marker.OffsetCm}cm/{marker.Type}"));
        Console.WriteLine($"  {leg.FromWaypointId}->{leg.ToWaypointId}, dist={leg.DistanceCm}cm, vmax={leg.MaxSpeedKmh:F0}km/h, sensors=[{sensorSummary}]");
    }

    private static string BuildRailwayLayoutXml()
    {
        return """
               <railwaylayout version="2">
                   <trackelements>
                       <trackelement id="seg_A" type="simpletrack" from="nA" to="nW" length_cm="150" description="A nach W"/>
                       <trackelement id="SW1" type="turnout" description="Verzweigung bei W">
                           <path id="sw1_main" from="nW" to="nB1" length_cm="95"/>
                           <path id="sw1_branch" from="nW" to="nB2" length_cm="110"/>
                       </trackelement>
                   </trackelements>

                   <sensors>
                       <section id="sec_A" detectorId="30" host="seg_A"/>
                       <point id="pt_145" detectorId="145" host="seg_A" offset_cm="145"/>
                       <section id="sec_branch" detectorId="32" host="sw1_branch"/>
                   </sensors>

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

