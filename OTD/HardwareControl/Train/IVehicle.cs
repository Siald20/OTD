// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <name@example.com>

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OTD.HardwareControl.Train;

/// <summary>
/// Common contract for all types of train vehicles (locomotives, cars)
/// Allows train-level logic to address powered and non-powered vehicles uniformly.
/// </summary>
public interface IVehicle
{
    /// <summary>
    /// Unique identifier of this vehicle.
    /// </summary>
    Guid VehicleId { get; }

    /// <summary>
    /// Raw XML configuration element for this vehicle as loaded from the configuration file.
    /// Both locomotives and cars share the same structure.
    /// </summary>
    XElement? VehicleConfig { get; }

    /// <inheritdoc cref="ILocoDecoder"/>
    ILocoDecoder? LocoDecoder { get; }

    /// <summary>
    /// Indicates whether this vehicle has a configured decoder.
    /// </summary>
    [MemberNotNullWhen(true, nameof(LocoDecoder))]
    public bool HasDecoder { get; }

    /// <summary>
    /// Configured physical length.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Scale-based minimum speed (km/h, mph) at speed step 1.
    /// </summary>
    int VMin { get; }

    /// <summary>
    /// Scale-based maximum speed (km/h, mph).
    /// </summary>
    int VMax { get; }

    /// <summary>
    /// Scale-based weight (tons, etc.).
    /// </summary>
    int Weight { get; }

    /// <summary>
    /// Current traveling direction of this vehicle.
    /// </summary>
    VehicleDirection Direction { get; }

    /// <summary>
    /// Sets the decoder direction and sets the speed step to 0 (halt).
    /// Must be called before driving to ensure a defined decoder direction.
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
