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
using System.Xml.Linq;

namespace OTD.HardwareControl;

/// <summary>
/// Controls a locomotive decoder, including driving, function handling,
/// and configuration loaded from XML
/// </summary>
public class Loco : IVehicle
{
    private readonly LocoDecoder _locoDecoder;
    private readonly List<SpeedEntry> _speedTable;

    /// <summary>
    /// Unique identifier of this locomotive.
    /// </summary>
    public Guid VehicleId { get; }


    /// <inheritdoc/>
    public XElement? VehicleConfig { get; }

    /// <summary>
    /// Indicates whether this locomotive has a configured decoder.
    /// </summary>
    public bool HasDecoder => true;

    /// <summary>
    /// Direct access to the configured locomotive decoder.
    /// </summary>
    public ILocoDecoder LocoDecoder => _locoDecoder;

    /// <summary>
    /// Current decoder direction.
    /// </summary>
    public VehicleDirection Direction => _locoDecoder.Direction;

    /// <summary>
    /// Current locomotive speed (km/h, mph, etc.).
    /// </summary>
    public int Speed { get; private set; }

    /// <summary>
    /// Configured physical length.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Scale-based minimum speed (km/h, mph) at speed step 1.
    /// </summary>
    public int VMin { get; }

    /// <summary>
    /// Scale-based maximum speed (km/h, mph).
    /// </summary>
    public int VMax { get; }

    /// <summary>
    /// Scale-based weight (tons, etc.).
    /// </summary>
    public int Weight { get; }

    /// <summary>
    /// Creates a locomotive instance and loads the locomotive configuration from XML.
    /// Command stations can be subscribed directly via <see cref="LocoDecoder"/>.
    /// </summary>
    /// <param name="vehicleId">The unique identifier of the locomotive.</param>
    public Loco(Guid vehicleId)
    {
        try
        {
            VehicleId = vehicleId;
            VehicleConfig = TrainUtils.ReadXConfiguration("loco", vehicleId);
            if (VehicleConfig is null)
            {
                throw new InvalidOperationException(
                    $"Locomotive configuration not found for locomotive '{vehicleId}'.");
            }

            var decoderConfig = VehicleConfig.Element("decoder");
            if (decoderConfig is null)
            {
                throw new InvalidOperationException(
                    $"Missing required <decoder> element in loco configuration for locomotive '{vehicleId}'.");
            }

            _locoDecoder = new LocoDecoder(decoderConfig);
            _speedTable = LocoUtils.CreateSpeedStepsTable(
                decoderConfig.Element("speedtable"),
                _locoDecoder.TotalSpeedSteps, out var vMinFromSpeedTable, out var vMaxFromSpeedTable);
            VMin = vMinFromSpeedTable;
            var modelElement = VehicleConfig.Element("model");
            VMax = TrainUtils.GetVehicleVMax(modelElement, vMaxFromSpeedTable); // Fallback auf vMax aus speedtable
            Weight = TrainUtils.GetVehicleWeight(modelElement);
            Length = TrainUtils.GetVehicleLength(VehicleConfig.Attribute("length")?.Value);

            // Externe Fahrstufen-Updates (z.B. von einem anderen Steuergerät) in km/h rückrechnen.
            _locoDecoder.StateChanged += OnDecoderStateChanged;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            Console.WriteLine($"Fehler beim Laden der Lok-Konfiguration '{vehicleId}': {ex.Message}");
            throw new InvalidOperationException($"Locomotive configuration could not be loaded for '{vehicleId}'.", ex);
        }
    }

    /// <summary>
    /// Sets the locomotive decoder direction to the resolved direction and stops the locomotive (speed step 0).
    /// Must be called before <see cref="SetSpeedVAsync"/> to ensure a defined decoder direction.
    /// </summary>
    /// <param name="trainDirection">Requested train travel direction.</param>
    /// <param name="orientation">Vehicle orientation within the consist.</param>
    /// <param name="forceSend">Forces command forwarding even if state is unchanged.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    public async Task SetDirectionAsync(
        TrainDirection trainDirection,
        VehicleOrientation orientation,
        bool forceSend = false,
        CancellationToken cancellationToken = default)
    {
        var decoderDirection = LocoDecoderUtils.ResolveDecoderDirection(trainDirection, orientation);

        if (!forceSend && _locoDecoder.Direction != VehicleDirection.Undefined && Speed == 0 &&
            _locoDecoder.Direction == decoderDirection)
        {
            Console.WriteLine(
                $"Richtungsbefehl unterdrückt (Duplikat): Richtung {decoderDirection} (Lokadresse {_locoDecoder.Address}).");
            return;
        }

        await _locoDecoder.SetSpeedStepAsync(decoderDirection, 0, forceSend, cancellationToken)
            .ConfigureAwait(false);

        Speed = 0;
    }

    /// <summary>
    /// Drives the locomotive at the specified speed using the decoder direction already set by a prior
    /// <see cref="SetDirectionAsync"/> call. The decoder direction is not changed.
    /// </summary>
    /// <param name="speed">Target speed (km/h).</param>
    /// <param name="forceSend">Forces command forwarding even if state is unchanged.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    public async Task SetSpeedVAsync(
        int speed,
        bool forceSend = false,
        CancellationToken cancellationToken = default)
    {
        // Fahrbefehl unterdrücken, wenn die angeforderte Geschwindigkeit über VMax liegt.
        if (VMax > 0 && speed > VMax)
        {
            Console.WriteLine(
                $"Fahrbefehl unterdrueckt: Angeforderte Geschwindigkeit {speed} km/h ueberschreitet VMax {VMax} km/h (Lokadresse {_locoDecoder.Address}).");
            return;
        }

        if (!forceSend && _locoDecoder.Direction != VehicleDirection.Undefined && Speed == speed)
        {
            Console.WriteLine(
                $"Fahrbefehl unterdrückt (Duplikat): {speed} km/h, Richtung {_locoDecoder.Direction} (Lokadresse {_locoDecoder.Address}).");
            return;
        }

        if (_speedTable.Count == 0 && speed > 0)
            throw new InvalidOperationException(
                "SetSpeedVAsync requires a non-empty speed table. Configure <speedtable> before driving by velocity.");

        if (speed < 0)
        {
            Console.WriteLine($"Fehler: Ungültige Geschwindigkeit {speed} für Decoderadresse {_locoDecoder.Address}.");
            return;
        }

        var speedStep = 0;

        if (speed > 0)
            speedStep = LocoUtils.ResolveSpeedStepForSpeedV(_speedTable, speed);

        await _locoDecoder.SetSpeedStepAsync(_locoDecoder.Direction, speedStep, forceSend, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"Fahrbefehl: {speed} km/h (SpeedStep {speedStep}), Richtung {_locoDecoder.Direction} (Lokadresse {_locoDecoder.Address}).");
        Speed = speed;
    }

    /// <summary>
    /// Triggers an emergency stop for the locomotive.
    /// </summary>
    protected internal Task EmergencyStopAsync(CancellationToken cancellationToken = default)
        => _locoDecoder.EmergencyStopAsync(cancellationToken);


    /// <summary>
    /// Called when the decoder reports a speed-step change from the command station
    /// (for example from an external controller). Converts the speed step back
    /// to km/h congruent with the floor mapping logic in <see cref="SetSpeedVAsync"/>.
    /// </summary>
    private void OnDecoderStateChanged(object? sender, LocoStateChangedEventArgs args)
    {
        // ToDo: Event-Kaskate endet momentan hier, Folgeevents auf Ebene Train müssen noch definiert werden.
        if (!args.HasSpeedUpdate)
            return;

        Speed = LocoUtils.ResolveSpeedVForSpeedStep(_speedTable, args.SpeedStep!.Value);
        Console.WriteLine($"Decoder-Update: Gemeldete Geschwindigkeit {Speed} km/h (SpeedStep {args.SpeedStep}), Richtung {_locoDecoder.Direction} (Lokadresse {_locoDecoder.Address}).");
    }
}
