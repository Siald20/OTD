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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl.Drivers;

/// <summary>
///     Facade for the LoDi device API from Lokstore Digital.
///     Manages access to the LoDi rector (controller) and
///     the LoDi-S88 commander (feedback receiver).
/// </summary>
/// <remarks>
///     Usage:
///     <code>
///     var lodi = new LoDi();
/// 
///     // Connect
///     await lodi.Rektor.ConnectAsync("192.168.1.100");
///     await lodi.S88Commander.ConnectAsync("192.168.1.101");
/// 
///     // Control locomotive
///     await lodi.Rektor.SetLocoSpeedAsync(3, 64, LocoDirection.Cab1);
///     await lodi.Rektor.SetLocoFunctionAsync(3, 0, true);
/// 
///     // Subscribe to S88 feedback
///     lodi.S88Commander.ContactStateChanged += (s, e) =>
///         Console.WriteLine($"Module {e.ModuleAddress}, Contact {e.ContactNumber}: {e.IsOccupied}");
///     await lodi.S88Commander.SubscribeEventsAsync();
///     </code>
/// </remarks>
internal sealed class LoDi : IDisposable
{
    // -------------------------------------------------------------------------
    // Konstruktor
    // -------------------------------------------------------------------------

    public LoDi()
    {
        Rektor = new LoDiRektor();
        S88Commander = new LoDiS88Commander();
    }
    // -------------------------------------------------------------------------
    // Geräte-Instanzen
    // -------------------------------------------------------------------------

    /// <summary>LoDi rector DCC controller (locomotives, turnouts, CV programming).</summary>
    public LoDiRektor Rektor { get; }

    /// <summary>LoDi S88 commander feedback receiver.</summary>
    public LoDiS88Commander S88Commander { get; }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        Rektor.Dispose();
        S88Commander.Dispose();
    }

    // -------------------------------------------------------------------------
    // Geräte-Discovery
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Searches for all LoDi devices in the local network via UDP broadcast.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of found LoDi devices</returns>
    public static Task<List<LoDiDeviceInfo>> DiscoverDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        return LoDiConnection.DiscoverDevicesAsync(cancellationToken);
    }
}