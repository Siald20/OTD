// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - AccessoryControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// der GNU General Public License, wie von der Free Software Foundation,
// entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// veröffentlichten Version, weiterverbreiten und/oder modifizieren.
//
// Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// Siehe die GNU General Public License für weitere Details.
//
// Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl.CommandStation;

namespace OTD.HardwareControl.Accessory;

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
    DecoderProtocol Protocol { get; }

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
    Task SetFunctionAsync(int outputValue, FunctionState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates an output value for the specified time span.
    /// For protocols without native timing, the output is switched on, waits for the timeout,
    /// then switched off in software. Protocols with native timing may encode the timeout
    /// directly into the transmitted command instead.
    /// </summary>
    Task ActivateFunctionAsync(int outputValue, int timeout, CancellationToken cancellationToken = default);
}
