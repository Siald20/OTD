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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OTD.HardwareControl;

/// <summary>
///     Common contract for all types of train vehicles (locomotives, cars)
///     Allows train-level logic to address powered and non-powered vehicles uniformly.
/// </summary>
public interface IVehicle
{
    /// <summary>
    ///     Unique identifier of this vehicle.
    /// </summary>
    Guid VehicleId { get; }

    /// <summary>
    ///     Raw XML configuration element for this vehicle as loaded from the configuration file.
    ///     Both locomotives and cars share the same structure.
    /// </summary>
    XElement? VehicleConfig { get; }

    /// <inheritdoc cref="ILocoDecoder" />
    ILocoDecoder? LocoDecoder { get; }

    /// <summary>
    ///     Indicates whether this vehicle has a configured decoder.
    /// </summary>
    [MemberNotNullWhen(true, nameof(LocoDecoder))]
    public bool HasDecoder { get; }

    /// <summary>
    ///     Configured physical length.
    /// </summary>
    int Length { get; }

    /// <summary>
    ///     Scale-based minimum speed (km/h, mph) at speed step 1.
    /// </summary>
    int VMin { get; }

    /// <summary>
    ///     Scale-based maximum speed (km/h, mph).
    /// </summary>
    int VMax { get; }

    /// <summary>
    ///     Scale-based weight (tons, etc.).
    /// </summary>
    int Weight { get; }

    /// <summary>
    ///     Current traveling direction of this vehicle.
    /// </summary>
    VehicleDirection Direction { get; }

    /// <summary>
    ///     Sets the decoder direction and sets the speed step to 0 (halt).
    ///     Must be called before driving to ensure a defined decoder direction.
    /// </summary>
    /// <param name="trainDirection">Requested train travel direction.</param>
    /// <param name="orientation">Vehicle orientation within the consist.</param>
    /// <param name="forceSend">Forces command forwarding even if state is unchanged.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    Task SetDirectionAsync(
        TrainDirection trainDirection,
        VehicleOrientation orientation,
        bool forceSend = false,
        CancellationToken cancellationToken = default);
}