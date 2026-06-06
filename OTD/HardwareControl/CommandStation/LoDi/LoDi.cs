// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <info@batec.net>
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
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl.CommandStation.LoDi;

/// <summary>
///     Fassade für die LoDi Geräte-API von Lokstore Digital.
///     Verwaltet den Zugriff auf den LoDi-Rektor (Steuergerät) und
///     den LoDi-S88-Commander (Rückmeldeempfänger).
/// </summary>
/// <remarks>
///     Verwendung:
///     <code>
///     var lodi = new LoDi();
///
///     // Verbinden
///     await lodi.Rektor.ConnectAsync("192.168.1.100");
///     await lodi.S88Commander.ConnectAsync("192.168.1.101");
///
///     // Lok steuern
///     await lodi.Rektor.SetLocoSpeedAsync(3, 64, LocoDirection.Cab1);
///     await lodi.Rektor.SetLocoFunctionAsync(3, 0, true);
///
///     // S88-Rückmeldungen abonnieren
///     lodi.S88Commander.ContactStateChanged += (s, e) =>
///         Console.WriteLine($"Modul {e.ModuleAddress}, Kontakt {e.ContactNumber}: {e.IsOccupied}");
///     await lodi.S88Commander.SubscribeModuleAsync(1);
///     </code>
/// </remarks>
public sealed class LoDi : IDisposable
{
    // -------------------------------------------------------------------------
    // Geräte-Instanzen
    // -------------------------------------------------------------------------

    /// <summary>LoDi-Rektor DCC-Steuergerät (Loks, Weichen, CV-Programmierung)</summary>
    public LoDiRektor Rektor { get; }

    /// <summary>LoDi-S88-Commander Rückmeldeempfänger</summary>
    public LoDiS88Commander S88Commander { get; }

    // -------------------------------------------------------------------------
    // Konstruktor
    // -------------------------------------------------------------------------

    public LoDi()
    {
        Rektor = new LoDiRektor();
        S88Commander = new LoDiS88Commander();
    }

    // -------------------------------------------------------------------------
    // Geräte-Discovery
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sucht alle LoDi-Geräte im lokalen Netzwerk per UDP-Broadcast.
    /// </summary>
    /// <param name="cancellationToken">Abbruchtoken</param>
    /// <returns>Liste der gefundenen LoDi-Geräte</returns>
    public static Task<List<LoDiDeviceInfo>> DiscoverDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        return LoDiConnection.DiscoverDevicesAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        Rektor.Dispose();
        S88Commander.Dispose();
    }
}