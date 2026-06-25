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
using System.Xml.Linq;

namespace OTD.HardwareControl;

/// <summary>
///     Provides parsing helpers for decoder configuration values.
/// </summary>
internal static class LocoDecoderUtils
{
    internal const int DefaultSpeedSteps = 128;

    /// <summary>
    ///     Reads the decoder protocol from the decoder configuration.
    /// </summary>
    /// <param name="protocolElement">The protocol element value as string.</param>
    /// <returns>The configured decoder protocol.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown if the protocol is missing, empty, or not one of the supported values:
    ///     dcc14, dcc28, dcc128, motorola, m3, mfx.
    /// </exception>
    internal static LocoDecoderProtocol GetProtocol(string? protocolElement)
    {
        if (!string.IsNullOrWhiteSpace(protocolElement))
            switch (protocolElement.Trim().ToLowerInvariant())
            {
                case "dcc14":
                    return LocoDecoderProtocol.Dcc14;
                case "dcc28":
                    return LocoDecoderProtocol.Dcc28;
                case "dcc128":
                    return LocoDecoderProtocol.Dcc128;
                case "motorola":
                    return LocoDecoderProtocol.Motorola;
                case "m3":
                    return LocoDecoderProtocol.M3;
                case "mfx":
                    return LocoDecoderProtocol.Mfx;
            }

        throw new ArgumentOutOfRangeException(
            nameof(protocolElement),
            protocolElement,
            $"Unknown decoder protocol: '{protocolElement}'.");
    }

    /// <summary>
    ///     Reads the effective number of speed steps from the decoder configuration.
    ///     Returns <see cref="DefaultSpeedSteps" /> if the <c>&lt;speedsteps&gt;</c>
    ///     element is missing or empty.
    /// </summary>
    /// <param name="speedStepsElementValue">The decoder configuration element.</param>
    /// <returns>The configured effective speed step count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown if the configured speed step value is missing or not numeric.
    /// </exception>
    internal static int GetSpeedSteps(string? speedStepsElementValue)
    {
        // Kein Element <speedtable> oder leerer Inhalt -> Default-Wert zurückgeben
        if (string.IsNullOrWhiteSpace(speedStepsElementValue))
            return DefaultSpeedSteps;

        // Wert parsen und entsprechenden DccSpedSteps-Wert zurückgeben, oder Fehler bei ungültigem Wert
        if (int.TryParse(speedStepsElementValue, out var speedSteps))
            return speedSteps;

        throw new ArgumentOutOfRangeException(
            nameof(speedStepsElementValue),
            speedStepsElementValue,
            $"Missing or invalid speed steps value: '{speedStepsElementValue}'. The value must be a valid integer.");
    }

    /// <summary>
    ///     Reads the decoder address from the decoder configuration.
    /// </summary>
    /// <param name="addressElementValue">The address element value as string.</param>
    /// <returns>The configured decoder address.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown if the address is missing, empty, not numeric, or outside the valid range 1..10239.
    /// </exception>
    internal static int GetAddress(string? addressElementValue)
    {
        if (!string.IsNullOrWhiteSpace(addressElementValue))
            if (int.TryParse(addressElementValue, out var address) && address is >= 1 and <= 10239)
                return address;

        throw new ArgumentOutOfRangeException(
            nameof(addressElementValue),
            addressElementValue,
            $"Missing or invalid decoder address: '{addressElementValue}'. Valid range is 1..10239.");
    }

    /// <summary>
    ///     Resolves the decoder direction from train direction and vehicle orientation.
    /// </summary>
    internal static VehicleDirection ResolveDecoderDirection(
        TrainDirection trainDirection,
        VehicleOrientation orientation)
    {
        var decoderDirection = trainDirection switch
        {
            TrainDirection.A => VehicleDirection.Forward,
            TrainDirection.B => VehicleDirection.Backward,
            _ => throw new ArgumentOutOfRangeException(
                nameof(trainDirection),
                trainDirection,
                $"Unsupported train direction: {trainDirection}")
        };

        if (orientation == VehicleOrientation.Reverse)
            decoderDirection = decoderDirection == VehicleDirection.Forward
                ? VehicleDirection.Backward
                : VehicleDirection.Forward;

        return decoderDirection;
    }

    /// <summary>
    ///     Parses all <c>&lt;function&gt;</c> entries from a decoder <c>&lt;functiontable&gt;</c>.
    ///     Maps attribute <c>no</c> to a dedicated field, keeps attribute <c>type</c>
    ///     as an open string value and stores all remaining attributes as key/value pairs.
    /// </summary>
    /// <param name="functionTableElement">The decoder <c>functiontable</c> element.</param>
    /// <returns>
    ///     Parsed function entries. Invalid entries without numeric <c>no</c> are skipped.
    /// </returns>
    internal static List<VehicleFunctions> GetFunctions(XElement? functionTableElement)
    {
        if (functionTableElement is null)
            return [];

        var result = new List<VehicleFunctions>();

        foreach (var functionElement in functionTableElement.Elements("function"))
        {
            var rawNo = functionElement.Attribute("no")?.Value
                        ?? functionElement.Attribute("No")?.Value;

            if (!int.TryParse(rawNo, out var number))
                continue;

            var type = functionElement.Attribute("type")?.Value.Trim() ?? string.Empty;
            var attributes = functionElement.Attributes()
                .Where(a =>
                    !string.Equals(a.Name.LocalName, "no", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(a.Name.LocalName, "type", StringComparison.OrdinalIgnoreCase))
                .Select(a => new KeyValuePair<string, string>(a.Name.LocalName, a.Value))
                .ToList();

            result.Add(new VehicleFunctions(number, type, attributes));
        }

        return result;
    }

    // ToDo: möglicherweise überflüssig
    internal static List<SoundFunction> GetSoundFunctions(IReadOnlyList<VehicleFunctions> functions)
    {
        return functions
            .Where(f => HasFunctionType(f, "sound"))
            .Select(f => new SoundFunction(
                f.Number,
                GetAttributeOrEmpty(f, "description"),
                GetAttributeOrDefault(f, "actuation", "toggle"),
                !string.Equals(GetAttributeOrDefault(f, "visible", "true"), "false",
                    StringComparison.OrdinalIgnoreCase),
                GetAttributeOrEmpty(f, "image")))
            .ToList();
    }

    // ToDo: möglicherweise überflüssig
    internal static List<OtherFunction> GetOtherFunctions(IReadOnlyList<VehicleFunctions> functions)
    {
        return functions
            .Where(f => !HasFunctionType(f, "headlight")
                        && !HasFunctionType(f, "sound")
                        && !HasFunctionType(f, "autocoupling"))
            .Select(f => new OtherFunction(
                f.Number,
                f.Type,
                GetAttributeOrEmpty(f, "description"),
                GetAttributeOrDefault(f, "actuation", "toggle"),
                !string.Equals(GetAttributeOrDefault(f, "visible", "true"), "false",
                    StringComparison.OrdinalIgnoreCase),
                GetAttributeOrEmpty(f, "image")))
            .ToList();
    }

    private static bool HasFunctionType(VehicleFunctions function, string expectedType)
    {
        return string.Equals(function.Type, expectedType, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAttributeOrEmpty(VehicleFunctions function, string attributeName)
    {
        return GetAttributeOrDefault(function, attributeName, string.Empty);
    }

    private static string GetAttributeOrDefault(VehicleFunctions function, string attributeName, string defaultValue)
    {
        return TryGetAttribute(function, attributeName, out var value)
            ? value
            : defaultValue;
    }

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
///     Represents a generic decoder function parsed from <c>&lt;functiontable&gt;</c>.
/// </summary>
/// <param name="Number">Function number from attribute <c>no</c>.</param>
/// <param name="Type">Function type from attribute <c>type</c>.</param>
/// <param name="Attributes">All remaining attributes as key/value pairs.</param>
public readonly record struct VehicleFunctions(
    int Number,
    string Type,
    IReadOnlyList<KeyValuePair<string, string>> Attributes);

/// <summary>
///     Represents a sound function parsed from <c>&lt;functiontable&gt;</c>.
/// </summary>
/// <param name="FunctionNumber">LocoDecoder function number.</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="Actuation">Actuation mode (e.g. toggle, momentary).</param>
/// <param name="Visible">Whether this function is shown in the UI.</param>
/// <param name="Image">Optional image identifier for the UI.</param>
public readonly record struct SoundFunction(
    int FunctionNumber,
    string Description,
    string Actuation,
    bool Visible,
    string Image);

/// <summary>
///     Represents a decoder function that is neither a headlight, sound nor auto-coupling function,
///     as parsed from <c>&lt;functiontable&gt;</c>.
/// </summary>
/// <param name="FunctionNumber">LocoDecoder function number.</param>
/// <param name="Type">Raw function type string from the configuration.</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="Actuation">Actuation mode (e.g. toggle, momentary).</param>
/// <param name="Visible">Whether this function is shown in the UI.</param>
/// <param name="Image">Optional image identifier for the UI.</param>
public readonly record struct OtherFunction(
    int FunctionNumber,
    string Type,
    string Description,
    string Actuation,
    bool Visible,
    string Image);