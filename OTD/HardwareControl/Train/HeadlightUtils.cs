// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <name@example.com>
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

namespace OTD.HardwareControl.Train;

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
    /// Bestimmt den globalen Zielzustand der Stirnbeleuchtung basierend auf Modus und Betriebsmodus.
    /// </summary>
    public static FunctionState DetermineMainHeadlightState(HeadlightMode mode, TrainOperatingMode operatingMode)
        => (mode, operatingMode) switch
        {
            // HeadlightMode.Off: immer aus
            (HeadlightMode.Off, _) => FunctionState.Off,

            // HeadlightMode.Auto: abhängig vom OperatingMode
            (HeadlightMode.Auto, TrainOperatingMode.ShutDown) => FunctionState.Off,
            (HeadlightMode.Auto, TrainOperatingMode.Parking) => FunctionState.On, // ToDo: Parklicht-Abfrage via HeadlightUtils
            (HeadlightMode.Auto, TrainOperatingMode.Shunting) => FunctionState.On,
            (HeadlightMode.Auto, TrainOperatingMode.Travelling) => FunctionState.On,

            // HeadlightMode.On: immer an
            (HeadlightMode.On, _) => FunctionState.On,

            _ => FunctionState.Undefined
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
