// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <name@example.com>
// //
// // Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// // der GNU General Public License, wie von der Free Software Foundation,
// // entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// // veröffentlichten Version, weiterverbreiten und/oder modifizieren.
// //
// // Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// // jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// // MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// // Siehe die GNU General Public License für weitere Details.
// //
// // Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// // Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Services;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.Examples;

/// <summary>
/// Rundfahrten mit RouteController auf Basis der RouteControl-API.
/// </summary>
public class TestRoundTripBi_BiDi
{
    private static RouteDriveProfile _driveProfile = new RouteDriveProfile(
        AccelerationPreset: AccelerationTrajectoryPresets.Linear,
        BrakingPreset: BrakingTrajectoryPreset.Linear);

    private static AccelerationStartPolicy _accelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint;
    private static int _emergencyReleasePromptActive;
    private static int _emergencyReleaseRequested;

    private static readonly Guid TurnoutW1Id = Guid.Parse("3f8a1b2c-4d5e-4f7a-8b9c-0d1e2f3a4b5c");
    private static readonly Guid TurnoutW2Id = Guid.Parse("4a9b2c3d-5e6f-4a7b-9c0d-1e2f3a4b5c6d");
    private static readonly Guid TurnoutW3Id = Guid.Parse("b2f3eaa9-43f9-414e-997c-92d44406a63b");
    private static readonly Guid ThreeWayW5W6Id = Guid.Parse("5b0c3d4e-6f7a-4b8c-9d0e-2f3a4b5c6d7e");
    private static readonly Guid TurnoutW101Id = Guid.Parse("cb5275e6-8b00-4d7d-b090-25a6db69624e");
    private static readonly Guid TurnoutW102Id = Guid.Parse("2b3c4d5e-6f70-4812-9304-0b1c2d3e4f5a");
    private static readonly Guid TurnoutW103Id = Guid.Parse("3c4d5e6f-7081-4923-a405-1c2d3e4f5a6b");
    private static readonly Guid TurnoutW104Id = Guid.Parse("4d5e6f70-8192-4a34-b506-2d3e4f5a6b7c");

    private static readonly Guid TrainBr193Id = Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e");
    private static readonly Guid TrainVt612DtId = Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6c");

    private sealed class FeedbackFeedbackForwarder(RouteController controller)
    {
        public void OnSensorStateChanged(object? _, InputStateChangedEventArgs args)
        {
            if (args.State != InputState.Active)
                return;

            Console.WriteLine($"[Feedback] {args.InputNumber} aktiv");
            controller.OnFeedbackInputActivated(args.InputNumber);

            var state = controller.GetSnapshot().RuntimeState;
            if (!state.SafetyStopInjected)
                return;

            // Eingabe wird im Hauptloop verarbeitet, damit es keine konkurrierenden Console.ReadKey-Aufrufe gibt.
            Interlocked.Exchange(ref _emergencyReleaseRequested, 1);
            if (Interlocked.CompareExchange(ref _emergencyReleasePromptActive, 1, 0) == 0)
                Console.WriteLine(
                    "[Safety] Notbremsung aktiv. Taste druecken fuer Notbremsaufhebung und Weiterfahrt gemaess RouteTable...");
        }
    }

    private static Accessory? _turnoutW1;
    private static Accessory? _turnoutW2;
    private static Accessory? _turnoutW3;
    private static Accessory? _threeWayW5W6;
    private static Accessory? _turnoutW101;
    private static Accessory? _turnoutW102;
    private static Accessory? _turnoutW103;
    private static Accessory? _turnoutW104;

    private sealed record SelectableEntry(string Id, string Display);

    public static void Run(CommandStation commandStation, FeedbackController feedbackModule)
    {
        ArgumentNullException.ThrowIfNull(commandStation);
        ArgumentNullException.ThrowIfNull(feedbackModule);
        Logging.EnableConsole = true;
        Logging.EnableDebugFor<LocoDecoder>(extended: false);
        Logging.EnableDebugFor<TrainDriving>(extended: true);
        Logging.EnableDebugFor<RouteController>(extended: true);
        Logging.EnableDebugFor<RouteLegResolver>(extended: true);
        Logging.EnableDebugFor<RouteTableService>(extended: true);

        RunAsync(commandStation, feedbackModule).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(CommandStation commandStation, FeedbackController feedbackModule)
    {
        EventHandler<InputStateChangedEventArgs>? feedbackHandler = null;

        try
        {
            BuildAccessories(commandStation);

            while (true)
            {
                var trainSelection = SelectOption("Wähle Zug", [
                    new SelectableEntry("BR193", "BR193"),
                    new SelectableEntry("VT612-DT", "VT612-DT")
                ]);

                var departureSelection = SelectOption("Wähle Abfahrt", [
                    new SelectableEntry("S_B1;AGAINSTLINE", "Abfahrt von Gleis 1 (Signal B1, AgainstLine)"),
                    new SelectableEntry("S_B2;AGAINSTLINE", "Abfahrt von Gleis 2 (Signal B2, AgainstLine)"),
                    new SelectableEntry("S_B3;AGAINSTLINE", "Abfahrt von Gleis 3 (Signal B3, AgainstLine)"),
                    new SelectableEntry("S_C1;ALONGLINE", "Abfahrt von Gleis 1 (Signal C1, AlongLine)"),
                    new SelectableEntry("S_C11;ALONGLINE", "Abfahrt von Gleis 1 (Signal C11, AlongLine)"),
                    new SelectableEntry("S_C2;ALONGLINE", "Abfahrt von Gleis 2 (Signal C2, AlongLine)"),
                    new SelectableEntry("S_C3;ALONGLINE", "Abfahrt von Gleis 3 (Signal C3, AlongLine)"),
                    new SelectableEntry("S_C11;SHUNTING",
                        "Rangieren: Umfahrung (C11 -> D12 -> wenden -> W5/6 -> A13 -> wenden -> W1 -> C11)")
                ]);

                var arrivalSelection =
                    new SelectableEntry("S_C11;ALONGLINE", "Ankunft auf Gleis 1 (Signal C11, AlongLine)");

                // Bestimmen ob Fahrt entlang oder entgegengesetzt zum Streckenverlauf stattfinden soll.
                var isAgainstLine =
                    departureSelection.Id.EndsWith(";AGAINSTLINE", StringComparison.OrdinalIgnoreCase);

                // Ziel-Auswahl (ausser bei Rangieren mit Umfahrung -> Ziel ist fix C11)
                if (departureSelection.Id != "S_C11;SHUNTING")
                {
                    var arrivalOptions = isAgainstLine
                        ? new[]
                        {
                            new SelectableEntry("S_B1;AGAINSTLINE", "Ankunft auf Gleis 1 (Signal B1, AgainstLine)"),
                            new SelectableEntry("S_B2;AGAINSTLINE", "Ankunft auf Gleis 2 (Signal B2, AgainstLine)"),
                            new SelectableEntry("S_B3;AGAINSTLINE", "Ankunft auf Gleis 3 (Signal B3, AgainstLine)")
                        }
                        : new[]
                        {
                            new SelectableEntry("S_C1;ALONGLINE", "Ankunft auf Gleis 1 (Signal C1, AlongLine)"),
                            new SelectableEntry("S_C11;ALONGLINE", "Ankunft auf Gleis 1 (Signal C11, AlongLine)"),
                            new SelectableEntry("S_C2;ALONGLINE", "Ankunft auf Gleis 2 (Signal C2, AlongLine)"),
                            new SelectableEntry("S_C3;ALONGLINE", "Ankunft auf Gleis 3 (Signal C3, AlongLine)")
                        };
                    arrivalSelection = SelectOption("Wähle Ankunft", arrivalOptions);
                }

                var accelerationTrajectoryPresetSelection = SelectOption("Wähle Beschleunigungsprofil", [
                    new SelectableEntry("Linear", "Linear"),
                    new SelectableEntry("Comfort", "Comfort"),
                    new SelectableEntry("EarlyAcceleration", "EarlyAcceleration"),
                    new SelectableEntry("LateAcceleration", "LateAcceleration"),
                    new SelectableEntry("BalancedControlPoint", "BalancedControlPoint")
                ]);

                var brakingTrajectoryPresetSelection = SelectOption("Wähle Bremsprofil", [
                    new SelectableEntry("Linear", "Linear"),
                    new SelectableEntry("Comfort", "Comfort"),
                    new SelectableEntry("AggressiveBrake", "AggressiveBrake"),
                    new SelectableEntry("LateBrake", "LateBrake"),
                    new SelectableEntry("EarlyBrake", "EarlyBrake")
                ]);

                _driveProfile = new RouteDriveProfile(
                    AccelerationPreset: Enum.Parse<AccelerationTrajectoryPresets>(
                        accelerationTrajectoryPresetSelection.Id, ignoreCase: true),
                    BrakingPreset: Enum.Parse<BrakingTrajectoryPreset>(
                        brakingTrajectoryPresetSelection.Id, ignoreCase: true)
                );

                var accelerationPolicySelection = SelectOption("Wähle Beschleunigungspunkt", [
                    new SelectableEntry("AfterTrainClearsWaypoint",
                        "Nach Passieren des Wegpunkts mit höherer Geschwindigkeit"),
                    new SelectableEntry("AtWaypointCrossing",
                        "Beim Passieren des Wegpunkts mit höherer Geschwindigkeit")
                ]);

                _accelerationStartPolicy = accelerationPolicySelection.Id switch
                {
                    "AfterTrainClearsWaypoint" => AccelerationStartPolicy.AfterTrainClearsWaypoint,
                    "AtWaypointCrossing" => AccelerationStartPolicy.AtWaypointCrossing,
                    _ => throw new InvalidOperationException(
                        $"Unbekannte Beschleunigungspunkt-Auswahl: {accelerationPolicySelection.Id}")
                };

                var safetySelection = SelectOption(
                    "Route Guards (Überwachung unerwartete Sensor-Aktivierung und Liegenbleiben)", new[]
                    {
                        new SelectableEntry("OFF", "Aus"),
                        new SelectableEntry("ON", "Ein")
                    });

                if (string.Equals(safetySelection.Id, "ON", StringComparison.OrdinalIgnoreCase))
                {
                    RouteController.UnexpectedAheadFeedbackInputToleranceCm = 30;
                    RouteController.EnableUnexpectedAheadFeedbackInputEmergencyStop = true;
                    RouteController.EnableStuckAlertEmergencyStop = true;
                    RouteController.StuckAlertTolerancePercent = 20;
                }
                else
                {
                    RouteController.EnableUnexpectedAheadFeedbackInputEmergencyStop = false;
                    RouteController.EnableStuckAlertEmergencyStop = false;
                }

                var selectedTrainId = trainSelection.Id switch
                {
                    "BR193" => TrainBr193Id,
                    "VT612-DT" => TrainVt612DtId,
                    _ => throw new InvalidOperationException($"Unbekannte Zugauswahl: {trainSelection.Id}")
                };

                var selectedDeparture = departureSelection.Id[..departureSelection.Id.IndexOf(';')];
                var selectedArrival = arrivalSelection.Id[..arrivalSelection.Id.IndexOf(';')];

                var selectedRouteDirection =
                    isAgainstLine ? RouteTravelDirection.AgainstLine : RouteTravelDirection.AlongLine;

                var train = new Train(selectedTrainId, commandStation);
                var layoutService = new XmlRailwayLayoutService(
                    topologyFilePath: XmlRailwayLayoutService.GetDefaultTopologyFilePath());
                var routeDefinitions = new XmlRouteDefinitionService(layoutService);
                using var controller = new RouteController(train, routeDefinitions, layoutService, initialHold: true);

                controller.AccelerationMs2 = 1;
                controller.UseAdaptiveSpeedStepInterval = true;
                controller.MinSpeedStepInterval = TimeSpan.FromMilliseconds(250);
                controller.MaxSpeedStepInterval = TimeSpan.FromMilliseconds(1000);
                controller.BoundTrain.OperatingMode = TrainOperatingMode.Travelling;
                controller.BoundTrain.TrainDirection = selectedRouteDirection == RouteTravelDirection.AlongLine
                    ? TrainDirection.A
                    : TrainDirection.B;

                var feedbackFeedbackForwarder = new FeedbackFeedbackForwarder(controller);
                feedbackHandler = feedbackFeedbackForwarder.OnSensorStateChanged;
                feedbackModule.InputStateChanged += feedbackHandler;

                try
                {
                    if (departureSelection.Id == "S_C11;SHUNTING")
                        await ShuntingBitschikon(controller);
                    else if (selectedRouteDirection == RouteTravelDirection.AlongLine)
                        await TravelDirectionAlongLine(controller, selectedDeparture, selectedArrival);
                    else
                        await TravelDirectionAgainstLine(controller, selectedDeparture, selectedArrival);
                }
                finally
                {
                    feedbackModule.InputStateChanged -= feedbackHandler;
                    feedbackHandler = null;
                }

                Console.WriteLine();
                Console.WriteLine("[Test] Runde abgeschlossen. Erneut fahren? (Enter = Ja, Esc = Beenden)");
                var endKey = Console.ReadKey(true);
                if (endKey.Key == ConsoleKey.Escape)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Test] Auswahl oder Ablauf abgebrochen.");
        }
        finally
        {
            DisposeAccessory(ref _turnoutW1);
            DisposeAccessory(ref _turnoutW2);
            DisposeAccessory(ref _turnoutW3);
            DisposeAccessory(ref _threeWayW5W6);
            DisposeAccessory(ref _turnoutW101);
            DisposeAccessory(ref _turnoutW102);
            DisposeAccessory(ref _turnoutW103);
            DisposeAccessory(ref _turnoutW104);
        }
    }

    private static async Task TravelDirectionAlongLine(RouteController controller, string selectedDeparture,
        string selectedArrival)
    {
        await RunDriveDemoAsync(controller, async ct =>
        {
            // Fahrstrasse (Weichen) S_C? -> S_E104 stellen.
            Console.WriteLine($"Weichen Fahrstrasse {selectedDeparture} -> S_E104 stellen...");
            await (selectedDeparture switch
            {
                "S_C1" or "S_C11" => ThreeWayW5W6.SetStateAsync("right", ct),
                "S_C2" => ThreeWayW5W6.SetStateAsync("straight", ct),
                "S_C3" => ThreeWayW5W6.SetStateAsync("left", ct),
                _ => Task.CompletedTask
            });

            // Route S_C? -> S_E104 zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                TravelDirection: RouteTravelDirection.AlongLine,
                FromWaypointId: selectedDeparture,
                ToWaypointId: "S_E104",
                MaxSpeedKmh: selectedDeparture is "S_C1" or "S_C11" or "S_C3" ? 40 : null, // Ausfahrt Gl. 1 und Gl. 3: 40 km/h
                DriveProfile: _driveProfile)
            {
                AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
            });

            Console.WriteLine("[Test] Route S_C2 -> S_E104 bereit. Zur Abfahrt beliebige Taste druecken.");
            Console.ReadKey(true);
            controller.ReleaseGo();

            // Fahrstrasse S_E104 -> S_I41 (Weichen) stellen.
            Console.WriteLine("Weichen Fahrstrasse S_E104 -> S_I41 stellen");
            await TurnoutW101.SetStateAsync("straight", ct);
            await TurnoutW102.SetStateAsync("diverging", ct);
            await TurnoutW103.SetStateAsync("diverging", ct);

            // Route S_E104 -> S_I41: Route zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                TravelDirection: RouteTravelDirection.AlongLine,
                FromWaypointId: "S_E104",
                ToWaypointId: "S_I41",
                DriveProfile: _driveProfile)
            {
                AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
            });

            // Fahrstrasse S_I41 -> S_A13 stellen.
            Console.WriteLine("Weichen Fahrstrasse S_I41 -> S_A13 stellen");
            await TurnoutW104.SetStateAsync("diverging", ct);

            controller.AddRoute(
                new RouteLeg(
                    TravelDirection: RouteTravelDirection.AlongLine,
                    FromWaypointId: "S_I41",
                    ToWaypointId: "S_A13",
                    DriveProfile: _driveProfile)
                {
                    AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
                }
            );

            Console.WriteLine("[Test] Warte, bis RouteLeg S_E104 -> S_I41 befahren wird...");
            await WaitUntilRouteLegEnteredByEventAsync(controller, "S_E104", "S_I41", ct);

            // Fahrstrasse (Weichen) S_D12 -> S_B? stellen.
            Console.WriteLine($"Weichen Fahrstrasse S_A13 -> {selectedArrival} stellen");
            
            Console.WriteLine($"[DBG] W1 command for arrival={selectedArrival}");
            await (selectedArrival switch
            {
                "S_C1" or "S_C11" or "S_C2" => TurnoutW1.SetStateAsync("diverging", ct),
                "S_C3" => TurnoutW1.SetStateAsync("straight", ct),
                _ => Task.CompletedTask
            });
            Console.WriteLine($"[DBG] W1 done");

            Console.WriteLine($"[DBG] W2 command for arrival={selectedArrival}");
            await (selectedArrival switch
            {
                "S_C1" or "S_C11" => TurnoutW2.SetStateAsync("crossing", ct),
                "S_C2" => TurnoutW2.SetStateAsync("crossing-straight", ct),
                _ => Task.CompletedTask
            });
            Console.WriteLine($"[DBG] W2 done");

            Console.WriteLine($"[DBG] W3 command for arrival={selectedArrival}");
            await (selectedArrival switch
            {
                "S_C1" or "S_C11" => TurnoutW3.SetStateAsync("diverging", ct),
                _ => Task.CompletedTask
            });
            Console.WriteLine($"[DBG] W3 done");

            // Route S_A13 -> S_C? zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                TravelDirection: RouteTravelDirection.AlongLine,
                FromWaypointId: "S_A13",
                ToWaypointId: selectedArrival,
                MaxSpeedKmh: selectedArrival is "S_C1" or "S_C11" or "S_C2" ? 40 : null, // Einfahrt Gl. 1 und Gl. 2: 40 km/h
                DriveProfile: _driveProfile,
                StopPointToTargetCm: selectedArrival switch
                {
                    "S_C1" => 15, // Ende Perron Gl. 1
                    "S_C2" or "S_C3" => 25, // Ende Perron Gl. 2/3
                    _ => null
                })
                {
                    AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
                }
            );
            // Warten, bis Zug (Lok) bei S_C11 angekommen ist (Fahrtziel)
            await WaitUntilTrainStoppedAtRouteEndAsync(controller, ct);
            
            Console.WriteLine($"[Test] Fahrt 'AlongLine' von {selectedDeparture} nach {selectedArrival} abgeschlossen. Zurueck zur Auswahl: beliebige Taste druecken.");
        });
    }

    private static async Task TravelDirectionAgainstLine(RouteController controller, string selectedDeparture,
        string selectedArrival)
    {
        await RunDriveDemoAsync(controller, async ct =>
        {
            // Fahrstrasse S_B? -> S_K102 stellen.
            Console.WriteLine($"Weichen Fahrstrasse {selectedDeparture} -> S_K102 stellen");

            await (selectedDeparture switch
            {
                "S_B1" => TurnoutW3.SetStateAsync("diverging", ct),
                _ => Task.CompletedTask
            });

            await (selectedDeparture switch
            {
                "S_B1" => TurnoutW2.SetStateAsync("crossing", ct),
                "S_B2" => TurnoutW2.SetStateAsync("crossing-straight", ct),
                _ => Task.CompletedTask
            });

            await (selectedDeparture switch
            {
                "S_B1" or "S_B2" => TurnoutW1.SetStateAsync("diverging", ct),
                "S_B3" => TurnoutW1.SetStateAsync("straight", ct),
                _ => Task.CompletedTask
            });

            // Route S_B? -> S_K102 zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                    TravelDirection: RouteTravelDirection.AgainstLine,
                    FromWaypointId: selectedDeparture,
                    ToWaypointId: "S_K102",
                    MaxSpeedKmh: selectedDeparture is "S_B1" or "S_B2" ? 40 : null, // Ausfahrt Gl. 1 und Gl. 2: 40 km/h,
                    DriveProfile: _driveProfile)
                {
                    AccelerationStartPolicy = _accelerationStartPolicy
                }
            );

            Console.WriteLine("[Test] Route S_B3 -> S_K102 bereit. Zur Abfahrt beliebige Taste druecken.");
            Console.ReadKey(true);
            controller.ReleaseGo();

            // Fahrstrasse S_K102 -> S_H41 (Weichen) stellen.
            Console.WriteLine("Weichen Fahrstrasse S_K102 -> S_H41 stellen");
            await TurnoutW104.SetStateAsync("diverging", ct);

            // Route S_K102 -> S_H41 zu RouteTable hinzufügen.
            controller.AddRoute(
                new RouteLeg(
                    TravelDirection: RouteTravelDirection.AgainstLine,
                    FromWaypointId: "S_K102",
                    ToWaypointId: "S_H41",
                    DriveProfile: _driveProfile)
                {
                    AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
                }
            );

            // Fahrstrasse S_H41 -> S_D12 (Weichen) stellen.
            Console.WriteLine("Weichen Fahrstrasse S_H41 -> S_D12 stellen");
            await TurnoutW103.SetStateAsync("diverging", ct);
            await TurnoutW102.SetStateAsync("diverging", ct);
            await TurnoutW101.SetStateAsync("straight", ct);

            // Route S_H41 -> S_D12: Route zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                TravelDirection: RouteTravelDirection.AgainstLine,
                FromWaypointId: "S_H41",
                ToWaypointId: "S_D12",
                DriveProfile: _driveProfile)
            {
                AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
            });

            Console.WriteLine("[Test] Warte, bis RouteLeg S_K102 -> S_H41 befahren wird...");
            await WaitUntilRouteLegEnteredByEventAsync(controller, "S_K102", "S_H41", ct);

            // Fahrstrasse (Weiche) S_D12 -> S_B? stellen.
            Console.WriteLine($"Weichen Fahrstrasse  S_D12 -> {selectedArrival} stellen");
            await (selectedArrival switch
            {
                "S_B1" => ThreeWayW5W6.SetStateAsync("right", ct),
                "S_B2" => ThreeWayW5W6.SetStateAsync("straight", ct),
                "S_B3" => ThreeWayW5W6.SetStateAsync("left", ct),
                _ => Task.CompletedTask
            });

            // Route zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                TravelDirection: RouteTravelDirection.AgainstLine,
                FromWaypointId: "S_D12",
                ToWaypointId: selectedArrival,
                MaxSpeedKmh: selectedArrival is "S_B1" or "S_B3"  ? 40 : null, // Einfahrt Gl. 1 und Gl. 3: 40 km/h,
                DriveProfile: _driveProfile)
            {
                AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
            });
            
            // Warten, bis Zug (Lok) bei S_C11 angekommen ist (Fahrtziel)
            await WaitUntilTrainStoppedAtRouteEndAsync(controller, ct);
            Console.WriteLine($"[Test] Fahrt 'AgainstLine' von {selectedDeparture} nach {selectedArrival} abgeschlossen. Zurueck zur Auswahl: beliebige Taste druecken.");
        });
    }

    private static async Task ShuntingBitschikon(RouteController controller)
    {
        await RunDriveDemoAsync(controller, async ct =>
        {
            Console.WriteLine($"Weichen Fahrstrasse S_C11 -> S_D12 stellen.");
            await ThreeWayW5W6.SetStateAsync("right", ct);
            
            // Route S_C11 -> S_D12 zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                    TravelDirection: RouteTravelDirection.AlongLine,
                    FromWaypointId: "S_C11",
                    ToWaypointId: "D_12",
                    MaxSpeedKmh: 40,
                    DriveProfile: _driveProfile)
                {
                    AccelerationStartPolicy = _accelerationStartPolicy
                }
            );

            Console.WriteLine("[Test] Route C11 -> S_D12 bereit. Zur Abfahrt beliebige Taste druecken.");
            Console.ReadKey(true);
            controller.ReleaseGo();

            // Warten, bis Zug (Lok) bei S_D12 angekommen ist (Zug wird im nächsten RouteLeg gewendet von AlongLine auf AgainstLine)
            await WaitUntilTrainStoppedAtRouteEndAsync(controller, ct);
            
            // Fahrstrasse N_W5/6 -> S_A13 stellen.
            Console.WriteLine("Weichen Fahrstrasse N_W5/6 -> S_A13 stellen");
            await ThreeWayW5W6.SetStateAsync("left", ct);
            await TurnoutW1.SetStateAsync("straight", ct);

            // Route N_W5/6 -> S_A13 zu RouteTable hinzufügen.
            controller.AddRoute(
                new RouteLeg(
                    TravelDirection: RouteTravelDirection.AgainstLine,
                    FromWaypointId: "N_W5/6",
                    ToWaypointId: "S_A13",
                    MaxSpeedKmh: 40,
                    DriveProfile: _driveProfile)
                {
                    AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
                }
            );

            // Warten, bis Zug (Lok) bei S_A13 angekommen ist (Zug wird im nächsten RouteLeg gewendet von AgainstLine auf AlongLine)
            await WaitUntilTrainStoppedAtRouteEndAsync(controller, ct);

            // Fahrstrasse N_W1 -> S_C11 (Weichen) stellen.
            Console.WriteLine("Weichen Fahrstrasse N_W1 -> S_C11 stellen");
            await TurnoutW1.SetStateAsync("diverging", ct);
            await TurnoutW2.SetStateAsync("crossing", ct);
            await TurnoutW3.SetStateAsync("diverging", ct);

            // Route N_W1 -> S_C11: Route zu RouteTable hinzufügen.
            controller.AddRoute(new RouteLeg(
                TravelDirection: RouteTravelDirection.AgainstLine,
                FromWaypointId: "N_W1",
                ToWaypointId: "S_C11",
                MaxSpeedKmh: 40,
                DriveProfile: _driveProfile)
            {
                AccelerationStartPolicy = AccelerationStartPolicy.AfterTrainClearsWaypoint
            });
            
            // Warten, bis Zug (Lok) bei S_C11 angekommen ist (Fahrtziel)
            await WaitUntilTrainStoppedAtRouteEndAsync(controller, ct);
            
            Console.WriteLine("[Test] Rangierfahrt (Umfahrung) abgeschlossen. Zurueck zur Auswahl: beliebige Taste druecken.");
        });
    }

    private static void DisposeAccessory(ref Accessory? accessory)
    {
        accessory?.Dispose();
        accessory = null;
    }

    private static void BuildAccessories(CommandStation commandStation)
    {
        _turnoutW1 = new Accessory(TurnoutW1Id, commandStation);
        _turnoutW2 = new Accessory(TurnoutW2Id, commandStation);
        _turnoutW3 = new Accessory(TurnoutW3Id, commandStation);
        _threeWayW5W6 = new Accessory(ThreeWayW5W6Id, commandStation);
        _turnoutW101 = new Accessory(TurnoutW101Id, commandStation);
        _turnoutW102 = new Accessory(TurnoutW102Id, commandStation);
        _turnoutW103 = new Accessory(TurnoutW103Id, commandStation);
        _turnoutW104 = new Accessory(TurnoutW104Id, commandStation);
    }

    private static Accessory TurnoutW1 => _turnoutW1 ?? throw new InvalidOperationException("W1 nicht initialisiert.");
    private static Accessory TurnoutW2 => _turnoutW2 ?? throw new InvalidOperationException("W2 nicht initialisiert.");
    private static Accessory TurnoutW3 => _turnoutW3 ?? throw new InvalidOperationException("W3 nicht initialisiert.");

    private static Accessory ThreeWayW5W6 =>
        _threeWayW5W6 ?? throw new InvalidOperationException("W5/W6 nicht initialisiert.");

    private static Accessory TurnoutW101 =>
        _turnoutW101 ?? throw new InvalidOperationException("W101 nicht initialisiert.");

    private static Accessory TurnoutW102 =>
        _turnoutW102 ?? throw new InvalidOperationException("W102 nicht initialisiert.");

    private static Accessory TurnoutW103 =>
        _turnoutW103 ?? throw new InvalidOperationException("W103 nicht initialisiert.");

    private static Accessory TurnoutW104 =>
        _turnoutW104 ?? throw new InvalidOperationException("W104 nicht initialisiert.");

    private static SelectableEntry SelectOption(string title, IReadOnlyList<SelectableEntry> options)
    {
        if (options.Count == 0)
            throw new InvalidOperationException("Keine Optionen vorhanden.");

        var index = 0;

        while (true)
        {
            RenderMenu(title, options, index);
            var key = Console.ReadKey(true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    index = index == 0 ? options.Count - 1 : index - 1;
                    break;
                case ConsoleKey.DownArrow:
                    index = (index + 1) % options.Count;
                    break;
                case ConsoleKey.Enter:
                    Console.WriteLine($"Ausgewaehlt: {options[index].Display}");
                    return options[index];
                case ConsoleKey.Escape:
                    throw new OperationCanceledException("Auswahl durch Benutzer abgebrochen.");
            }
        }
    }

    private static void RenderMenu(string title, IReadOnlyList<SelectableEntry> options, int selectedIndex)
    {
        Console.Clear();
        Console.WriteLine("=== TestRoundTripBi_BiDi ===");
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine("(Pfeil hoch/runter, Enter = waehlen, Esc = abbrechen)");
        Console.WriteLine();

        for (var i = 0; i < options.Count; i++)
        {
            var isSelected = i == selectedIndex;
            Console.WriteLine(isSelected
                ? $"=> [ {options[i].Display} ]"
                : $"   {options[i].Display}");
        }
    }

    private static double PromptDouble(string prompt, double defaultValue, double minValue, double maxValue)
    {
        while (true)
        {
            Console.Write($"{prompt} [{defaultValue:F1}] ({minValue:F1}-{maxValue:F1}): ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;

            if (double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) ||
                double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                if (parsed >= minValue && parsed <= maxValue)
                    return parsed;
            }

            Console.WriteLine($"Ungueltiger Wert. Bitte Zahl zwischen {minValue:F1} und {maxValue:F1} eingeben.");
        }
    }

    private static async Task RunDriveDemoAsync(RouteController controller,
        Func<CancellationToken, Task> configureAndRunRoutes)
    {
        using var cts = new CancellationTokenSource();
        var driveTask = controller.Run(cts.Token);
        var consumedAtStart = controller.GetSnapshot().RuntimeState.ConsumedRouteCount;
        var routeIdleReached =
            new TaskCompletionSource<RouteController.IdleState>(TaskCreationOptions.RunContinuationsAsynchronously);
        _emergencyReleaseRequested = 0;
        _emergencyReleasePromptActive = 0;

        void OnIdleStateReached(RouteController.IdleState idle)
        {
            if (idle.ConsumedRouteCount <= consumedAtStart)
                return;

            if (idle.Reason is "RouteCompleted" or "ZeroSpeedTargetReached" or "NoActiveRoute")
                routeIdleReached.TrySetResult(idle);
        }

        controller.IdleStateReached += OnIdleStateReached;

        using var setupInputCts = new CancellationTokenSource();
        var setupEmergencyInputTask = ObserveEmergencyReleaseKeysAsync(controller, setupInputCts.Token);

        try
        {
            await configureAndRunRoutes(cts.Token);
            setupInputCts.Cancel();
            try
            {
                await setupEmergencyInputTask;
            }
            catch (OperationCanceledException)
            {
                // erwartet
            }

            Console.WriteLine("[Test] Demo laeuft. Nach Fahrabschluss geht es automatisch zurueck ins Menue.");

            while (true)
            {
                if (routeIdleReached.Task.IsCompleted)
                {
                    var idle = await routeIdleReached.Task.ConfigureAwait(false);
                    Console.WriteLine(
                        $"[Test] Fahrt abgeschlossen ({idle.Reason}, legs={idle.RouteLegCount}, active={idle.ActiveFromWaypointId ?? "-"}, activeIndex={(idle.ActiveRouteIndex?.ToString() ?? "-")}, speed={idle.CurrentSpeedKmh}, head={idle.HeadPositionCm:F1}, remaining={idle.RemainingDistanceCm:F1}). Rueckkehr ins Menue...");
                    break;
                }

                var snapshot = controller.GetSnapshot();
                var state = snapshot.RuntimeState;
                var currentSpeedKmh = Math.Max(0, controller.BoundTrain.SpeedV);

                // Finalzustand: Zug steht und RouteControl wartet nur noch auf neue Route/Hold-Aufhebung.
                if (snapshot.RouteLegs.Count == 0 &&
                    state.ActiveRouteIndex is null &&
                    state.ConsumedRouteCount > consumedAtStart)
                {
                    Console.WriteLine("[Test] Alle RouteLegs abgeschlossen. Rueckkehr ins Menue...");
                    break;
                }

                if (currentSpeedKmh == 0 && (state.ActiveStopPoint || state.ActiveRouteIndex is null))
                {
                    Console.WriteLine("[Test] Fahrt abgeschlossen. Rueckkehr ins Menue...");
                    break;
                }

                if (Interlocked.Exchange(ref _emergencyReleaseRequested, 0) == 1)
                {
                    ApplyEmergencyRelease(controller);
                }

                await Task.Delay(50, cts.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            controller.IdleStateReached -= OnIdleStateReached;
            setupInputCts.Cancel();
            await cts.CancelAsync();
            try
            {
                await driveTask;
            }
            catch (OperationCanceledException)
            {
                // erwartet
            }
        }
    }

    private static async Task ObserveEmergencyReleaseKeysAsync(RouteController controller,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (Interlocked.CompareExchange(ref _emergencyReleaseRequested, 0, 0) != 1)
            {
                await Task.Delay(50, cancellationToken);
                continue;
            }

            if (!Console.KeyAvailable)
            {
                await Task.Delay(50, cancellationToken);
                continue;
            }

            Console.ReadKey(true);
            if (Interlocked.Exchange(ref _emergencyReleaseRequested, 0) == 1)
                ApplyEmergencyRelease(controller);
        }
    }

    private static void ApplyEmergencyRelease(RouteController controller)
    {
        controller.OnEmergencyRelease();
        Interlocked.Exchange(ref _emergencyReleasePromptActive, 0);
        Console.WriteLine("[Safety] Notbremsung aufgehoben. RouteControl setzt die Fahrt fort.");
    }

    private static async Task WaitUntilIdleStateReachedAsync(
        RouteController controller,
        CancellationToken ct,
        params string[] reasons)
    {
        var tcs = new TaskCompletionSource<RouteController.IdleState>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnIdleStateReached(RouteController.IdleState idle)
        {
            if (reasons.Length > 0 && Array.IndexOf(reasons, idle.Reason) < 0)
                return;

            tcs.TrySetResult(idle);
        }

        controller.IdleStateReached += OnIdleStateReached;

        try
        {
            using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
            var idle = await tcs.Task.ConfigureAwait(false);
            Console.WriteLine(
                $"[Test] IdleState erreicht ({idle.Reason}, active={idle.ActiveFromWaypointId ?? "-"}, legs={idle.RouteLegCount}, speed={idle.CurrentSpeedKmh}, remaining={idle.RemainingDistanceCm:F1}).");
        }
        finally
        {
            controller.IdleStateReached -= OnIdleStateReached;
        }
    }

    private static Task WaitUntilTrainStoppedAtRouteEndAsync(
        RouteController controller,
        CancellationToken ct)
    {
        return WaitUntilIdleStateReachedAsync(controller, ct, "RouteCompleted", "ZeroSpeedTargetReached");
    }

    private static async Task WaitUntilRouteLegEnteredByEventAsync(
        RouteController controller,
        string fromWaypointId,
        string toWaypointId,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnTransition(RouteController.RouteLegTransitionEvent e)
        {
            if (e.TransitionType != RouteController.RouteLegTransitionType.Enter)
                return;

            if (!e.IsGroup(fromWaypointId, toWaypointId))
                return;

            tcs.TrySetResult();
        }

        controller.RouteLegTransition += OnTransition;

        try
        {
            using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            controller.RouteLegTransition -= OnTransition;
        }

        Console.WriteLine(
            $"[Test] RouteLeg {fromWaypointId}->{toWaypointId} betreten.");
    }

    private static async Task WaitUntilRouteLegLeftByEventAsync(
        RouteController controller,
        string fromWaypointId,
        string toWaypointId,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnTransition(RouteController.RouteLegTransitionEvent e)
        {
            if (e.TransitionType != RouteController.RouteLegTransitionType.Leave)
                return;

            if (!e.IsGroup(fromWaypointId, toWaypointId))
                return;

            tcs.TrySetResult();
        }

        controller.RouteLegTransition += OnTransition;

        try
        {
            using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            controller.RouteLegTransition -= OnTransition;
        }

        Console.WriteLine(
            $"[Test] RouteLeg {fromWaypointId}->{toWaypointId} verlassen.");
    }
}