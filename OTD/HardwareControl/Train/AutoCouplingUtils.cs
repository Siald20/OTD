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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl.Train;

/// <summary>
/// Provides automatic uncoupling functionality for vehicles.
/// </summary>
internal static class AutoCouplingUtils
{
    /// <summary>
    /// Parses all decoder functions of type <c>autocoupling</c> from generic vehicle functions.
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
                {
                    durationRaw = f.Attributes
                        .FirstOrDefault(a => string.Equals(a.Key, "activationtime", StringComparison.OrdinalIgnoreCase))
                        .Value;
                }

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
    /// Returns the first matching auto coupling function for a requested direction.
    /// </summary>
    internal static AutoCouplingFunction? FindAutoCouplingFunction(
        IReadOnlyList<AutoCouplingFunction> availableAutoCouplings,
        VehicleDirection requestedDirection)
    {
        foreach (var function in availableAutoCouplings)
        {
            if (function.Direction == requestedDirection)
                return function;
        }

        return null;
    }

    /// <summary>
    /// Applies a single auto coupling action on the vehicle decoder.
    /// Requires a vehicle with a configured decoder.
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

