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
/// Common contract for accessory decoders (turnouts, signals, etc.).
/// Accessory decoders differ fundamentally from locomotive decoders:
/// - They are commanded using protocol-specific data values per decoder address
/// - They use different DCC protocols (SetAccessoryValueAsync vs SetLocoSpeedAsync)
/// - They support no speed control or function mapping like locomotives
/// - Like locomotive decoders, they use a subscribe model for command stations
/// </summary>
public interface IAccessoryDecoder
{
    /// <summary>
    /// DCC address of the accessory decoder (1–2048).
    /// </summary>
    int Address { get; }

    /// <summary>
    /// AccessoryDecoder communication protocol.
    /// </summary>
    AccessoryDecoderProtocol Protocol { get; }

    /// <summary>
    /// The currently subscribed command station.
    /// Accessory decoders are stationary and can be bound to exactly one station at a time.
    /// </summary>
    ICommandStation? SubscribedCommandStation { get; }

    /// <summary>
    /// Subscribes a command station to receive commands from this accessory decoder.
    /// </summary>
    Task SubscribeCommandStationAsync(ICommandStation commandStation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes a command station from this accessory decoder.
    /// </summary>
    Task UnsubscribeCommandStationAsync(ICommandStation? commandStation);

    /// <summary>
    /// Raised when the command station sends a state update for this accessory decoder.
    /// </summary>
    event EventHandler<AccessoryStateChangedEventArgs>? StateChanged;
    
    /// <summary>
    /// Sends a protocol-specific output value and activation state to the subscribed command station.
    /// </summary>
    Task SetFunctionAsync(int outputValue, AccessoryFunctionState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates an output value for the specified time span.
    /// For protocols without native timing, the output is switched on, waits for the timeout,
    /// then switched off in software. Protocols with native timing may encode the timeout
    /// directly into the transmitted command instead.
    /// </summary>
    Task ActivateFunctionAsync(int outputValue, int timeout, CancellationToken cancellationToken = default);
}
