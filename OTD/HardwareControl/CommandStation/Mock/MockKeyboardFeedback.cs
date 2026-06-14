// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
using System;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl.Drivers;

namespace OTD.HardwareControl.Drivers;

/// <summary>
/// Simulated feedback test that maps keyboard keys to 40 sensors.
/// Uses MockCommandStation and a keyboard-driven mock feedback provider.
/// </summary>
internal static class MockKeyboardFeedback
{
    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            linkedCts.Cancel();
        };

        using var mockStation = new MockCommandStation();
        await mockStation.ConnectAsync("127.0.0.1", 11092, linkedCts.Token).ConfigureAwait(false);

        var providerUid = Guid.Parse("00000000-0000-0000-0000-00000000f040");
        var feedback = new KeyboardMockFeedback(providerUid);
        await feedback.ConnectAsync(linkedCts.Token).ConfigureAwait(false);

        feedback.SensorStateChanged += (_, e) =>
        {
            var marker = e.State == RailSensorState.Active ? "ON " : "OFF";
            Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] Sensor {e.SensorNumber:D2} => {marker}");
        };

        PrintIntro();

        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);

                    if (key.Key == ConsoleKey.Spacebar)
                    {
                        linkedCts.Cancel();
                        break;
                    }

                    feedback.TryHandleKey(key.Key);
                }

                // Console cannot capture key-up directly; this timeout-based release simulates key release.
                feedback.UpdateKeyReleases(DateTimeOffset.UtcNow);
                await Task.Delay(20, linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl-C or space.
        }
        finally
        {
            await feedback.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            await mockStation.DisconnectAsync().ConfigureAwait(false);
            Console.WriteLine("Feedback mock test beendet.");
        }
    }

    private static void PrintIntro()
    {
        Console.WriteLine("=== Feedback Mock Keyboard Test ===");
        Console.WriteLine("Abbruch: Leertaste oder CTRL-C");
        Console.WriteLine();
        Console.WriteLine("Tastatur-Mapping (links nach rechts):");
        Console.WriteLine("  1..0       => Sensor 01..10");
        Console.WriteLine("  Q..P       => Sensor 11..20");
        Console.WriteLine("  A..(rechts von L) => Sensor 21..30");
        Console.WriteLine("  Y..-       => Sensor 31..40");
        Console.WriteLine();
        Console.WriteLine("Toggle-Verhalten pro Taste:");
        Console.WriteLine("  1. Tastendruck  => ON");
        Console.WriteLine("  2. Tastendruck  => OFF");
        Console.WriteLine();
    }
}

