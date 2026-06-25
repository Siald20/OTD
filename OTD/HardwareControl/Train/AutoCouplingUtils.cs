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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl;

/// <summary>
///     Provides automatic uncoupling functionality for vehicles.
/// </summary>
internal static class AutoCouplingUtils
{
    /// <summary>
    ///     Parses all decoder functions of type <c>autocoupling</c> from generic vehicle functions.
    /// </summary>
    internal static List<AutoCouplingFunction> GetAutoCouplingFunctions(IReadOnlyList<VehicleFunctions> functions)
    {
        return functions
            .Where(f => string.Equals(f.Type, "autocoupling", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var directionRaw = f.Attributes
                    .FirstOrDefault(a => string.Equals(a.Key, "direction", StringComparison.OrdinalIgnoreCase))
                    .Value?
                    .Trim();

                var direction = directionRaw?.ToLowerInvariant() switch
                {
                    "forward" => VehicleDirection.Forward,
                    "backward" => VehicleDirection.Backward,
                    _ => VehicleDirection.Undefined
                };

                var durationRaw = f.Attributes
                    .FirstOrDefault(a => string.Equals(a.Key, "duration", StringComparison.OrdinalIgnoreCase))
                    .Value;

                if (string.IsNullOrWhiteSpace(durationRaw))
                    durationRaw = f.Attributes
                        .FirstOrDefault(a => string.Equals(a.Key, "activationtime", StringComparison.OrdinalIgnoreCase))
                        .Value;

                var activationTime = int.TryParse(durationRaw, out var duration)
                    ? Math.Max(0, duration)
                    : 500;

                return new AutoCouplingFunction(
                    f.Number,
                    direction,
                    activationTime);
            })
            .ToList();
    }

    /// <summary>
    ///     Returns the first matching auto coupling function for a requested direction.
    /// </summary>
    internal static AutoCouplingFunction? FindAutoCouplingFunction(
        IReadOnlyList<AutoCouplingFunction> availableAutoCouplings,
        VehicleDirection requestedDirection)
    {
        foreach (var function in availableAutoCouplings)
            if (function.Direction == requestedDirection)
                return function;

        return null;
    }

    /// <summary>
    ///     Applies a single auto coupling action on the vehicle decoder.
    ///     Requires a vehicle with a configured decoder.
    /// </summary>
    internal static async Task ApplyAutoCouplingAsync(
        IVehicle vehicle,
        AutoCouplingFunction function,
        CancellationToken cancellationToken = default)
    {
        if (!vehicle.HasDecoder)
            throw new InvalidOperationException("Auto coupling requires a configured decoder.");

        var decoder = vehicle.LocoDecoder;

        // Auto coupling is always pulse/timed activation based on activationtime.
        await decoder
            .ActivateFunctionAsync(function.FunctionNumber, function.ActivationTime, cancellationToken)
            .ConfigureAwait(false);
    }
}

public readonly record struct AutoCouplingFunction(
    int FunctionNumber,
    VehicleDirection Direction,
    int ActivationTime);