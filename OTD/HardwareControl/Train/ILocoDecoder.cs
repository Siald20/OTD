// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl.CommandStation;
namespace OTD.HardwareControl.Train;

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
    DecoderProtocol Protocol { get; }

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
    FunctionState GetFunctionState(int functionNumber);

    /// <summary>
    /// Sets a decoder function state on all subscribed command stations.
    /// </summary>
    Task SetFunctionStateAsync(int function, FunctionState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a decoder function for the specified time span.
    /// The function is turned on, waits for the specified duration, then turned off.
    /// </summary>
    /// <param name="function">Locomotive decoder function number.</param>
    /// <param name="timeout">Activation duration in milliseconds.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    Task ActivateFunctionAsync(int function, int timeout, CancellationToken cancellationToken = default);
}
