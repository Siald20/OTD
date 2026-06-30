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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OTD.Common;

namespace OTD.HardwareControl;

/// <summary>
///     Controls a car decoder, including function handling and direction synchronization,
///     with configuration loaded from XML.
///     Cars are non-powered vehicles; speed is always 0.
/// </summary>
public class Car : IVehicle
{
    private readonly LocoDecoder? _locoDecoder;

    /// <summary>
    ///     Raw XML configuration element from cars.xml for this car.
    ///     Accessible via <see cref="VehicleConfig" /> through <see cref="IVehicle" />.
    /// </summary>
    public readonly XElement? CarConfig;

    /// <summary>
    ///     Creates a car instance and loads the car configuration from XML.
    ///     Command stations can be subscribed directly via <see cref="LocoDecoder" />.
    /// </summary>
    /// <param name="vehicleId">The unique identifier of the car.</param>
    public Car(Guid vehicleId)
    {
        try
        {
            VehicleId = vehicleId;
            CarConfig = TrainUtils.ReadXConfiguration("car", vehicleId);
            if (CarConfig is null)
                throw new InvalidOperationException(
                    $"Car configuration not found for car '{vehicleId}'.");

            // LocoDecoder-Konfiguration laden, falls vorhanden. Nicht alle Wagen müssen zwingend einen LocoDecoder haben;
            // ein fehlender oder leerer <decoder>-Knoten bedeutet: kein LocoDecoder vorhanden.
            var decoderConfig = CarConfig.Element("decoder");
            if (decoderConfig is not null && decoderConfig.HasElements) _locoDecoder = new LocoDecoder(decoderConfig);

            var modelElement = CarConfig.Element("model");
            Length = TrainUtils.GetVehicleLength(CarConfig.Attribute("length")?.Value);
            VMax = TrainUtils.GetVehicleVMax(modelElement);
            Weight = TrainUtils.GetVehicleWeight(modelElement);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            Logging.Warning<Car>($"Fehler beim Laden der Wagen-Konfiguration '{vehicleId}': {ex.Message}");
            throw new InvalidOperationException($"Car configuration could not be loaded for '{vehicleId}'.", ex);
        }
    }

    /// <summary>
    ///     Configured decoder functions (e.g. headlights, couplings).
    /// </summary>
    public IReadOnlyList<VehicleFunctions> Functions => _locoDecoder?.Functions ?? Array.Empty<VehicleFunctions>();

    /// <summary>
    ///     Unique identifier of this car.
    /// </summary>
    public Guid VehicleId { get; }

    /// <inheritdoc />
    public XElement? VehicleConfig => CarConfig;

    /// <summary>
    ///     Indicates whether this car has a configured decoder.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_locoDecoder))]
    public bool HasDecoder => _locoDecoder is not null;

    /// <summary>
    ///     Direct access to the configured car decoder, if available.
    /// </summary>
    public ILocoDecoder? LocoDecoder => _locoDecoder;

    /// <summary>
    ///     Current decoder direction.
    /// </summary>
    public VehicleDirection Direction => _locoDecoder?.Direction ?? VehicleDirection.Undefined;

    /// <summary>
    ///     Gets the configured car length.
    /// </summary>
    public int Length { get; }

    /// <summary>
    ///     Scale-based minimum speed (km/h, mph) at speed step 1.
    ///     Not applicable for cars, value is always 0.
    /// </summary>
    public int VMin { get; } = 0;

    /// <summary>
    ///     Scale-based maximum speed (km/h, mph).
    /// </summary>
    public int VMax { get; }

    /// <summary>
    ///     Scale-based weight (tons, etc.).
    /// </summary>
    public int Weight { get; }

    /// <summary>
    ///     Sets the decoder direction for this car (speed step always 0).
    ///     Keeps the decoder direction in sync with the train for headlight logic.
    ///     Has no effect when no decoder is configured.
    /// </summary>
    public async Task SetDirectionAsync(
        TrainDirection trainDirection,
        VehicleOrientation orientation,
        bool forceSend = false,
        CancellationToken cancellationToken = default)
    {
        if (!HasDecoder)
            return;

        var decoder = _locoDecoder;
        var decoderDirection = LocoDecoderUtils.ResolveDecoderDirection(trainDirection, orientation);

        if (!forceSend && decoder.Direction == decoderDirection && decoder.SpeedStep == 0)
            return;

        await decoder.SetSpeedStepAsync(decoderDirection, 0, forceSend, cancellationToken)
            .ConfigureAwait(false);
    }
}