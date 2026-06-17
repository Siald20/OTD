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

namespace OTD.HardwareControl;

/// <summary>
///     Abstract interface of a DCC command station.
///     Enables locomotive control, function switching, and track power handling
///     independent of the command station manufacturer.
/// </summary>
/// <remarks>
///     Implementations can target LoDi Rektor, Maerklin Central, Roco z21, etc.
/// </remarks>
public interface ICommandStation : IDisposable
{
    // -------------------------------------------------------------------------
    // Eigenschaften
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Indicates whether an active connection to the command station exists.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    ///     Raised when the command station reports a locomotive state update
    ///     (for example speed step/direction or function state).
    /// </summary>
    event EventHandler<LocoStateChangedEventArgs>? LocoStateChanged;

    /// <summary>
    ///     Raised when the command station reports an accessory state update
    ///     (decoder address + value + switching state).
    /// </summary>
    event EventHandler<AccessoryStateChangedEventArgs>? AccessoryStateChanged;

    // -------------------------------------------------------------------------
    // Verbindung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Opens a connection to the command station.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Closes the connection to the command station.
    /// </summary>
    Task DisconnectAsync();

    // -------------------------------------------------------------------------
    // Gleisversorgung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Switches track power on or off.
    /// </summary>
    /// <param name="isOn"><c>true</c> = track power on; <c>false</c> = track power off.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetPowerAsync(bool isOn, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Queries the current track power state.
    /// </summary>
    /// <returns><c>true</c> if track power is on; otherwise <c>false</c>.</returns>
    Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default);

    // -------------------------------------------------------------------------
    // Lokomotivsteuerung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Registers decoder parameters that remain constant for the lifetime of the instance.
    /// </summary>
    /// <param name="address">DCC locomotive address (1-9999).</param>
    /// <param name="protocol">Decoder protocol (for example DCC14, DCC28, DCC128, Motorola, M3, mfx).</param>
    /// <param name="effectiveSpeedSteps">Effectively usable speed steps of the decoder (for example 126 for DCC128).</param>
    void InitializeDecoder(int address, LocoDecoderProtocol protocol, int effectiveSpeedSteps);

    /// <summary>
    ///     Sets speed and direction of a locomotive.
    /// </summary>
    /// <param name="address">DCC locomotive address (1-9999).</param>
    /// <param name="speedStep">
    ///     Speed step (0 = stop, depending on protocol 1-14, 1-28 or 1-126).
    ///     A value of 0 performs a regular stop (not an emergency stop).
    /// </param>
    /// <param name="direction">Travel direction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetLocoSpeedAsync(int address, int speedStep, VehicleDirection direction,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Switches a locomotive function on or off.
    /// </summary>
    /// <param name="address">DCC locomotive address (1-9999).</param>
    /// <param name="functionNumber">Function number (0 = F0/light, 1-28 = F1-F28).</param>
    /// <param name="isOn"><c>true</c> = function on; <c>false</c> = function off.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetLocoFunctionAsync(int address, int functionNumber, bool isOn,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Performs an emergency stop for a specific locomotive.
    /// </summary>
    /// <param name="address">DCC locomotive address (1-9999).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EmergencyStopAsync(int address, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Performs an emergency stop for all locomotives at once.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EmergencyStopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Queries current decoder states (speed and functions) from the command station
    ///     and raises LocoStateChanged events for each retrieved state.
    ///     This is useful to initialize a decoder with the real station state.
    /// </summary>
    /// <param name="address">DCC locomotive address (1-9999).</param>
    /// <param name="functionList">List of function numbers to query.
    ///     If empty, no functions are queried.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task QueryLocoFunctionsStateAsync(int address, System.Collections.Generic.IReadOnlyList<int> functionList,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Queries current speed step and direction of a locomotive from the command station
    ///     and raises a LocoStateChanged event with the retrieved values.
    ///     This is useful to initialize a decoder with the real station state.
    /// </summary>
    /// <param name="address">DCC locomotive address (1-9999).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task QueryLocoSpeedDirectionAsync(int address, CancellationToken cancellationToken = default);

    // -------------------------------------------------------------------------
    // Zubehördecodersteuerung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sends the protocol-specific data value to an accessory decoder.
    /// </summary>
    /// <param name="address">DCC address of the accessory decoder (1-2048).</param>
    /// <param name="value">Protocol-specific data value (for example for DCC basic: output select 0/1).</param>
    /// <param name="protocol">Decoder protocol of the accessory decoder (standard or extended).</param>
    /// <param name="state">Switch state (active/inactive).</param>
    /// <param name="activationTimeMs">
    ///     Optional duration from &lt;activationtime&gt; in milliseconds.
    ///     0 means no timed activation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAccessoryValueAsync(int address, byte value,
        AccessoryDecoderProtocol protocol,
        AccessoryFunctionState state,
        int activationTimeMs = 0,
        CancellationToken cancellationToken = default);
}
