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

namespace OTD.HardwareControl;

/// <summary>
/// Common contract for low-level locomotive decoder control.
/// Locomotive decoders differ from accessory decoders in that multiple command stations
/// can be subscribed simultaneously, because locomotives can be driven across control domains.
/// </summary>
public interface ILocoDecoder
{
    /// <summary>
    /// DCC address of the locomotive decoder (1–9999).
    /// </summary>
    int Address { get; }

    /// <summary>
    /// Communication protocol used by this decoder (e.g. DCC28, DCC128, Motorola).
    /// </summary>
    LocoDecoderProtocol Protocol { get; }

    /// <summary>
    /// Number of effective speed steps provided by this decoder.
    /// </summary>
    int TotalSpeedSteps { get; }

    /// <summary>
    /// Current driving direction of this decoder.
    /// </summary>
    VehicleDirection Direction { get; }

    /// <summary>
    /// Current speed step of this decoder.
    /// </summary>
    int SpeedStep { get; }

    /// <summary>
    /// All command stations currently subscribed to this decoder.
    /// Multiple command stations can be subscribed simultaneously for broadcast control.
    /// </summary>
    IReadOnlyList<ICommandStation> SubscribedCommandStations { get; }

    /// <summary>
    /// Subscribes a command station to receive decoder commands.
    /// Multiple command stations can be subscribed simultaneously.
    /// </summary>
    /// <param name="commandStation">The command station to subscribe.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    /// <exception cref="ArgumentNullException">Thrown if commandStation is null.</exception>
    Task SubscribeCommandStationAsync(ICommandStation commandStation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes a command station from receiving decoder commands.
    /// </summary>
    /// <param name="commandStation">The command station to unsubscribe.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    Task UnsubscribeCommandStationAsync(ICommandStation? commandStation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the command station sends a state update for this decoder
    /// (speed step, direction, or function state).
    /// </summary>
    event EventHandler<LocoStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Configured decoder functions.
    /// </summary>
    IReadOnlyList<VehicleFunctions> Functions { get; }

    /// <summary>
    /// Gets the current function state for the specified decoder function.
    /// Returns the last known state from internal tracking.
    /// </summary>
    LocoDecoderFunctionState GetFunctionState(int functionNumber);

    /// <summary>
    /// Sets a decoder function state on all subscribed command stations.
    /// </summary>
    Task SetFunctionStateAsync(int function, LocoDecoderFunctionState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a decoder function for the specified time span.
    /// The function is turned on, waits for the specified duration, then turned off.
    /// </summary>
    /// <param name="function">Locomotive decoder function number.</param>
    /// <param name="timeout">Activation duration in milliseconds.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    Task ActivateFunctionAsync(int function, int timeout, CancellationToken cancellationToken = default);
}
