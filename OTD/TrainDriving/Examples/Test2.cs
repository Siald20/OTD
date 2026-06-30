// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <name@example.com>
// //
// // Dieses Programm ist freie Software: Sie koennen es unter den Bedingungen
// // der GNU General Public License, wie von der Free Software Foundation,
// // entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder spaeteren
// // veroeffentlichten Version, weiterverbreiten und/oder modifizieren.
// //
// // Dieses Programm wird in der Hoffnung bereitgestellt, dass es nuetzlich sein wird,
// // jedoch OHNE JEDE GEWAEHRLEISTUNG; sogar ohne die implizite Gewaehrleistung der
// // MARKTFAEHIGKEIT oder EIGNUNG FUER EINEN BESTIMMTEN ZWECK.
// // Siehe die GNU General Public License fuer weitere Details.
// //
// // Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// // Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;
using System.Threading;
using System.Threading.Tasks;
using OTD.Common;
using OTD.HardwareControl;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.Examples;

public static class Test2
{
    public static void Run(CommandStation commandStation, Feedback feedbackModule)
    {
        ArgumentNullException.ThrowIfNull(commandStation);
        ArgumentNullException.ThrowIfNull(feedbackModule);
        Logging.EnableDebugFor<TrainDriving>(extended: true);
        RunAsync(commandStation).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(CommandStation commandStation)
    {
        using var cts = new CancellationTokenSource();

        var train = new Train(Guid.Parse("8b9d1f2c-6a44-4e8f-9c31-5f2a7d1e0b6e"), commandStation);
        using var controller = new RouteController(train);

        controller.AccelerationMs2 = 0.55;
        controller.UseAdaptiveSpeedStepInterval = true;
        controller.MinSpeedStepInterval = TimeSpan.FromMilliseconds(250);
        controller.MaxSpeedStepInterval = TimeSpan.FromMilliseconds(1000);

        controller.BoundTrain.OperatingMode = TrainOperatingMode.Travelling;
        controller.BoundTrain.TrainDirection = TrainDirection.A;

        controller.ReplaceRoutes([
            new RouteLeg(
                FromWaypointId: "B2",
                ToWaypointId: "K102",
                DistanceCm: 224,
                MaxSpeedKmh: 40,
                DriveProfile: new RouteDriveProfile(
                    AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                    BrakingPreset: BrakingTrajectoryPreset.Linear)),
            new RouteLeg(
                FromWaypointId: "K102",
                ToWaypointId: "H41",
                DistanceCm: 163,
                MaxSpeedKmh: 60,
                DriveProfile: new RouteDriveProfile(
                    AccelerationPreset: AccelerationTrajectoryPreset.Linear,
                    BrakingPreset: BrakingTrajectoryPreset.Linear),
                AccelerationStartPolicy: AccelerationStartPolicy.AfterTrainClearsWaypoint,
                StopPoint: new StopPoint(OffsetCm: 150, StopReason: "Zielhalt"))
        ]);

        var driveTask = controller.Run(cts.Token);

        Console.WriteLine("[Test2] Demo laeuft. Weiterfahrt am StopPoint mit Taste freigeben.");
        Console.ReadKey(true);
        controller.ReleaseGo();

        await Task.Delay(TimeSpan.FromSeconds(20), cts.Token);

        await cts.CancelAsync();
        try
        {
            await driveTask;
        }
        catch (OperationCanceledException)
        {
            // erwartet
        }

        await train.SetSpeedVAsync(0, CancellationToken.None);
        train.OperatingMode = TrainOperatingMode.Parking;
    }
}