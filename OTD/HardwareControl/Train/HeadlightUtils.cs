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

namespace OTD.HardwareControl;

/// <summary>
/// Provides complete headlight functionality: parsing, pattern matching and decoder switching.
/// </summary>
internal static class HeadlightUtils
{
    /// <summary>
    /// Parses all decoder functions of type <c>headlight</c> and returns pattern mappings
    /// for forward and backward directions.
    /// </summary>
    internal static List<HeadlightPatternFunction> GetAvailableHeadlightFunctions(IReadOnlyList<VehicleFunctions> functions)
    {
        var result = new List<HeadlightPatternFunction>();

        foreach (var function in functions.Where(f => IsFunctionType(f, "headlight")))
        {
            var isMaster = TryGetAttribute(function, "master", out var masterRaw) &&
                           (string.Equals(masterRaw, "1", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(masterRaw, "true", StringComparison.OrdinalIgnoreCase));
            var mode = GetAttributeOrEmpty(function, "mode");
            var hasMode = !string.IsNullOrWhiteSpace(mode);

            var patternForward = GetAttributeOrEmpty(function, "forward");
            if (string.IsNullOrWhiteSpace(patternForward))
                patternForward = GetAttributeOrEmpty(function, "a");
            var hasForwardPattern = !string.IsNullOrWhiteSpace(patternForward);

            var patternBackward = GetAttributeOrEmpty(function, "backward");
            if (string.IsNullOrWhiteSpace(patternBackward))
                patternBackward = GetAttributeOrEmpty(function, "b");
            var hasBackwardPattern = !string.IsNullOrWhiteSpace(patternBackward);
            var hasDirectionalPatterns = hasForwardPattern || hasBackwardPattern;

            if (hasMode)
            {
                if (hasDirectionalPatterns)
                {
                    Console.WriteLine(
                        $"Warnung: Headlight-Funktion {function.Number} kombiniert mode=\"{mode}\" mit forward/backward-Pattern. " +
                        "Bei mode-basierten Funktionen werden forward/backward ignoriert.");
                }

                result.Add(new HeadlightPatternFunction(function.Number, isMaster, mode, VehicleDirection.Forward,
                    string.Empty));
                result.Add(new HeadlightPatternFunction(function.Number, isMaster, mode, VehicleDirection.Backward,
                    string.Empty));
                continue;
            }

            if (!hasDirectionalPatterns)
            {
                Console.WriteLine(
                    $"Warnung: Headlight-Funktion {function.Number} hat weder einen mode-Eintrag noch forward/backward-Pattern und wird ignoriert.");
                continue;
            }

            if (hasForwardPattern)
                result.Add(new HeadlightPatternFunction(function.Number, isMaster, mode, VehicleDirection.Forward,
                    patternForward));

            if (hasBackwardPattern)
                result.Add(new HeadlightPatternFunction(function.Number, isMaster, mode, VehicleDirection.Backward,
                    patternBackward));
        }

        return result;
    }

    /// <summary>
    /// Determines the global target state of the headlight based on headlight mode and operating mode.
    /// </summary>
    public static LocoDecoderFunctionState DetermineMainHeadlightState(HeadlightMode mode, TrainOperatingMode operatingMode)
        => (mode, operatingMode) switch
        {
            // HeadlightMode.Off: immer aus
            (HeadlightMode.Off, _) => LocoDecoderFunctionState.Off,

            // HeadlightMode.Auto: abhängig vom OperatingMode
            (HeadlightMode.Auto, TrainOperatingMode.ShutDown) => LocoDecoderFunctionState.Off,
            (HeadlightMode.Auto, TrainOperatingMode.Parking) => LocoDecoderFunctionState.On, // ToDo: Parklicht-Abfrage via HeadlightUtils
            (HeadlightMode.Auto, TrainOperatingMode.Shunting) => LocoDecoderFunctionState.On,
            (HeadlightMode.Auto, TrainOperatingMode.Travelling) => LocoDecoderFunctionState.On,

            // HeadlightMode.On: immer an
            (HeadlightMode.On, _) => LocoDecoderFunctionState.On,

            _ => LocoDecoderFunctionState.Undefined
        };

    // Sucht und gibt übereinstimmendes Headlight-Pattern zurück
    internal static HeadlightPatternFunction FindHeadlightPattern(
        string pattern,
        VehicleDirection direction,
        IReadOnlyList<HeadlightPatternFunction> headlightPatterns)
    {
        return headlightPatterns.FirstOrDefault(h =>
            h.Direction == direction &&
            string.Equals(h.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<int> GetNonMasterFunctions(
        IReadOnlyList<HeadlightPatternFunction> availableHeadlightPatterns)
        => availableHeadlightPatterns
            .Where(h => !h.IsMaster)
            .Select(h => h.FunctionNumber)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

    /// <summary>
    /// Returns the configured master headlight function number, if available.
    /// </summary>
    internal static int? GetMasterFunctionNumber(
        IReadOnlyList<HeadlightPatternFunction> availableHeadlightPatterns)
        => ResolveMasterFunctionNumber(availableHeadlightPatterns);

    private static bool IsFunctionType(VehicleFunctions function, string expectedType)
        => string.Equals(function.Type, expectedType, StringComparison.OrdinalIgnoreCase);

    private static int? ResolveMasterFunctionNumber(IReadOnlyList<HeadlightPatternFunction> headlightPatterns)
    {
        var masterFunctionNumbers = headlightPatterns
            .Where(h => h.IsMaster)
            .Select(h => h.FunctionNumber)
            .Distinct()
            .ToList();

        if (masterFunctionNumbers.Count == 0)
            return null;

        if (masterFunctionNumbers.Count > 1)
        {
            Console.WriteLine(
                $"Warnung: Mehrere Headlight-Masterfunktionen konfiguriert ({string.Join(", ", masterFunctionNumbers)}). " +
                $"Es wird die erste verwendet: {masterFunctionNumbers[0]}.");
        }

        return masterFunctionNumbers[0];
    }

    private static string GetAttributeOrEmpty(VehicleFunctions function, string attributeName)
        => GetAttributeOrDefault(function, attributeName, string.Empty);

    private static string GetAttributeOrDefault(VehicleFunctions function, string attributeName, string defaultValue)
        => TryGetAttribute(function, attributeName, out var value) ? value : defaultValue;

    private static bool TryGetAttribute(VehicleFunctions function, string attributeName, out string value)
    {
        foreach (var attribute in function.Attributes)
        {
            if (!string.Equals(attribute.Key, attributeName, StringComparison.OrdinalIgnoreCase))
                continue;

            value = attribute.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

/// <summary>
/// Represents a headlight function together with the associated pattern for a driving direction.
/// </summary>
public readonly record struct HeadlightPatternFunction(
    int FunctionNumber,
    bool IsMaster,
    string Mode,
    VehicleDirection Direction,
    string Pattern);
