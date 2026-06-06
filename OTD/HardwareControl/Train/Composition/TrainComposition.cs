// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <info@batec.net>

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OTD.HardwareControl.Train.Composition;

/// <summary>
/// Immutable train composition that contains the ordered vehicle entries and their runtime instances.
/// </summary>
public sealed class TrainComposition : IReadOnlyList<TrainVehicle>
{
    private readonly TrainVehicle[] _vehicles;

    /// <summary>
    /// Empty composition instance.
    /// </summary>
    public static TrainComposition Empty { get; } = new(Array.Empty<TrainVehicle>());

    /// <summary>
    /// Total train length of the composition.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Minimum speed of the composition.
    /// </summary>
    public int VMin { get; }

    /// <summary>
    /// Maximum speed of the composition.
    /// </summary>
    public int VMax { get; }

    /// <summary>
    /// Total weight of the composition.
    /// Returns 0 if one or more vehicle weights are unknown.
    /// </summary>
    public int Weight { get; }

    /// <summary>
    /// Number of vehicles in the composition.
    /// </summary>
    public int Count => _vehicles.Length;

    /// <summary>
    /// Returns the vehicle entry at the given train position.
    /// </summary>
    public TrainVehicle this[int index] => _vehicles[index];

    /// <summary>
    /// Exposes the ordered vehicle entries as read-only list.
    /// </summary>
    public IReadOnlyList<TrainVehicle> Vehicles => Array.AsReadOnly(_vehicles);

    internal TrainComposition(IEnumerable<TrainVehicle> vehicles)
    {
        if (vehicles is null)
            throw new ArgumentNullException(nameof(vehicles));

        _vehicles = vehicles
            .Select((vehicle, index) => vehicle with { Position = index + 1 })
            .ToArray();

        var keyValues = TrainUtils.CalculateCompositionKeyValues(_vehicles);
        Length = keyValues.Length;
        VMin = keyValues.VMin;
        VMax = keyValues.VMax;
        Weight = keyValues.Weight;
    }

    /// <summary>
    /// Returns a builder pre-populated with the current composition entries.
    /// Runtime vehicle instances are intentionally not preserved.
    /// </summary>
    public TrainCompositionBuilder ToBuilder()
        => new(_vehicles);

    /// <summary>
    /// Finds a vehicle entry by its unique identifier.
    /// </summary>
    public TrainVehicle? FindByVehicleId(Guid vehicleId)
    {
        foreach (var vehicle in _vehicles)
        {
            if (vehicle.VehicleId.Equals(vehicleId))
                return vehicle;
        }

        return null;
    }

    /// <summary>
    /// Returns the index of a vehicle entry or -1 if it is not part of the composition.
    /// </summary>
    public int IndexOfVehicleId(Guid vehicleId)
    {
        for (var index = 0; index < _vehicles.Length; index++)
        {
            if (_vehicles[index].VehicleId.Equals(vehicleId))
                return index;
        }

        return -1;
    }

    /// <summary>
    /// Returns whether the composition contains the specified vehicle.
    /// </summary>
    public bool ContainsVehicle(Guid vehicleId)
        => IndexOfVehicleId(vehicleId) >= 0;

    public IEnumerator<TrainVehicle> GetEnumerator()
        => ((IEnumerable<TrainVehicle>)_vehicles).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}


