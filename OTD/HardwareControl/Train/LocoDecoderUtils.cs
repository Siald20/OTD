// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - TrainControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <info@batec.net>
// //
// // Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// // der GNU General Public License, wie von der Free Software Foundation,
// // entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// // veröffentlichten Version, weiterverbreiten und/oder modifizieren.
// //
// // Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// // jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// // MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// // Siehe die GNU General Public License für weitere Details.
// //
// // Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// // Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace OTD.HardwareControl.Train;

/// <summary>
/// Provides parsing helpers for decoder configuration values.
/// </summary>
internal static class LocoDecoderUtils
{
    internal const int DefaultSpeedSteps = 128;

    /// <summary>
    /// Reads the decoder protocol from the decoder configuration.
    /// </summary>
    /// <param name="protocolElement">The protocol element value as string.</param>
    /// <returns>The configured decoder protocol.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the protocol is missing, empty, or not one of the supported values:
    /// dcc14, dcc28, dcc128, motorola, m3, mfx.
    /// </exception>
    internal static DecoderProtocol GetProtocol(string? protocolElement)
    {
        if (!string.IsNullOrWhiteSpace(protocolElement))
            switch (protocolElement.Trim().ToLowerInvariant())
            {
                case "dcc14":
                    return DecoderProtocol.Dcc14;
                case "dcc28":
                    return DecoderProtocol.Dcc28;
                case "dcc128":
                    return DecoderProtocol.Dcc128;
                case "motorola":
                    return DecoderProtocol.Motorola;
                case "m3":
                    return DecoderProtocol.M3;
                case "mfx":
                    return DecoderProtocol.Mfx;
            }

        throw new ArgumentOutOfRangeException(
            nameof(protocolElement),
            protocolElement,
            $"Unknown decoder protocol: '{protocolElement}'.");
    }

    /// <summary>
    /// Reads the effective number of speed steps from the decoder configuration.
    /// Returns <see cref="DefaultSpeedSteps"/> if the <c>&lt;speedsteps&gt;</c>
    /// element is missing or empty.
    /// </summary>
    /// <param name="speedStepsElementValue">The decoder configuration element.</param>
    /// <returns>The configured effective speed step count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the configured speed step value is missing or not numeric.
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
    /// Reads the decoder address from the decoder configuration.
    /// </summary>
    /// <param name="addressElementValue">The address element value as string.</param>
    /// <returns>The configured decoder address.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the address is missing, empty, not numeric, or outside the valid range 1..10239.
    /// </exception>
    internal static int GetAddress(string? addressElementValue)
    {
        if (!string.IsNullOrWhiteSpace(addressElementValue))
        {
            if (int.TryParse(addressElementValue, out var address) && address is >= 1 and <= 10239)
                return address;
        }

        throw new ArgumentOutOfRangeException(
            nameof(addressElementValue),
            addressElementValue,
            $"Missing or invalid decoder address: '{addressElementValue}'. Valid range is 1..10239.");
    }

    /// <summary>
    /// Resolves the decoder direction from train direction and vehicle orientation.
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
        {
            decoderDirection = decoderDirection == VehicleDirection.Forward
                ? VehicleDirection.Backward
                : VehicleDirection.Forward;
        }

        return decoderDirection;
    }

    /// <summary>
    /// Parses all <c>&lt;function&gt;</c> entries from a decoder <c>&lt;functiontable&gt;</c>.
    /// Maps attribute <c>no</c> to a dedicated field, keeps attribute <c>type</c>
    /// as an open string value and stores all remaining attributes as key/value pairs.
    /// </summary>
    /// <param name="functionTableElement">The decoder <c>functiontable</c> element.</param>
    /// <returns>
    /// Parsed function entries. Invalid entries without numeric <c>no</c> are skipped.
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
                !string.Equals(GetAttributeOrDefault(f, "visible", "true"), "false", StringComparison.OrdinalIgnoreCase),
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
                !string.Equals(GetAttributeOrDefault(f, "visible", "true"), "false", StringComparison.OrdinalIgnoreCase),
                GetAttributeOrEmpty(f, "image")))
            .ToList();
    }

    private static bool HasFunctionType(VehicleFunctions function, string expectedType)
        => string.Equals(function.Type, expectedType, StringComparison.OrdinalIgnoreCase);

    private static string GetAttributeOrEmpty(VehicleFunctions function, string attributeName)
        => GetAttributeOrDefault(function, attributeName, string.Empty);

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
    
    /// <summary>
    /// Builds a speedV-to-decoder-step lookup table from the decoder configuration.
    /// Reads <c>&lt;speedtable&gt;</c> interpolates missing speed steps,
    /// and creates a 1 km/h speedV grid mapped to the nearest decoder step.
    /// Returns an empty list if no <c>&lt;speedtable&gt;</c> element is provided.
    /// </summary>
    /// <param name="speedTableElement">The <c>speedtable</c> XML element.</param>
    /// <param name="effectiveSpeedSteps">Effective count of decoder speed steps.</param>
    /// <param name="vMax">Output: configured maximum speed (Vmax) from the speed table.</param>
    /// <returns>
    /// A list of <see cref="SpeedEntry"/> values ordered by speedV,
    /// where each speedV is mapped to a decoder step.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="effectiveSpeedSteps"/> is smaller than 2.</exception>
    /// <exception cref="FormatException">Thrown when one or more speed entries contain invalid numeric values.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the speed table contains fewer than two valid <c>speed</c> entries
    /// or when the smallest configured step is not <c>1</c>.
    /// </exception>
    internal static List<SpeedEntry> CreateSpeedStepsTable(XElement? speedTableElement, int effectiveSpeedSteps,
        out int vMin, out int vMax)
    {
        vMin = 0;
        vMax = 0;

        if (speedTableElement is null)
            return [];

        if (effectiveSpeedSteps < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveSpeedSteps),
                effectiveSpeedSteps,
                $"Speed mapping requires at least 2 effective speed steps. Received: {effectiveSpeedSteps}.");
        }

        var baseEntries = new List<SpeedEntry>();

        foreach (var speedElement in speedTableElement.Elements("speed"))
        {
            var speedVRaw = speedElement.Attribute("v")?.Value.Trim();
            var stepRaw = speedElement.Attribute("step")?.Value.Trim();

            if (!int.TryParse(speedVRaw, out var speedV) || !int.TryParse(stepRaw, out var step))
            {
                throw new FormatException(
                    $"Invalid <speed> entry in <speedtable>: v='{speedVRaw}', step='{stepRaw}'. Both values must be integers.");
            }

            baseEntries.Add(new SpeedEntry(speedV, step));
        }

        if (baseEntries.Count < 2)
        {
            throw new InvalidOperationException(
                "Speed mapping requires at least two valid <speed> entries in <speedtable>.");
        }

        var minStep = baseEntries.Min(e => e.SpeedStep);
        if (minStep != 1)
        {
            throw new InvalidOperationException(
                $"Speed mapping requires the smallest step in <speedtable> to be 1, but the smallest configured step is {minStep}.");
        }

        baseEntries = baseEntries.OrderBy(e => e.SpeedStep).ToList();

        var result = InterpolateSpeedSteps(baseEntries, effectiveSpeedSteps);

        vMin = (int)Math.Round(baseEntries.First(e => e.SpeedStep == 1).SpeedV);
        vMax = (int)Math.Round(baseEntries.Max(e => e.SpeedV));
        result = BuildSpeedStageTable(result, vMax);

        return result.OrderBy(e => e.SpeedV).ToList();
    }

    /// <summary>
    /// Interpolates intermediate speed entries between the given base speed points.
    /// </summary>
    /// <param name="baseEntries">Base speed points sorted by decoder step.</param>
    /// <param name="effectiveSpeedSteps">Total number of decoder speed steps to generate.</param>
    /// <returns>Interpolated speed entries from step 1 to <paramref name="effectiveSpeedSteps"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when fewer than two base entries are provided.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="effectiveSpeedSteps"/> is smaller than 2.</exception>
private static List<SpeedEntry> InterpolateSpeedSteps(List<SpeedEntry> baseEntries, int effectiveSpeedSteps)
{
    if (baseEntries.Count < 2)
        throw new ArgumentException(
            $"Speed interpolation requires at least 2 base speed entries. Invalid value: {baseEntries.Count}.",
            nameof(baseEntries));

    if (effectiveSpeedSteps < 2)
        throw new ArgumentOutOfRangeException(
            nameof(effectiveSpeedSteps),
            effectiveSpeedSteps,
            $"Speed interpolation requires at least 2 effective speed steps (<speedsteps> in decoder configuration). Invalid value: {effectiveSpeedSteps}.");

    // Kurvenform beibehalten:
    // Wir verwenden die Reihenfolge der vorhandenen Basispunkte als Stützstellen
    // und strecken diese stückweise linear auf die gewünschte Schrittanzahl.
    var orderedBaseEntries = baseEntries.OrderBy(e => e.SpeedStep).ToList();
    var result = new List<SpeedEntry>(effectiveSpeedSteps);

    var lastSourceIndex = orderedBaseEntries.Count - 1;

    for (var targetStep = 1; targetStep <= effectiveSpeedSteps; targetStep++)
    {
        // Zielschritt auf kontinuierliche Position im Quell-Stützstellenraum abbilden.
        // targetStep=1 => sourcePosition=0
        // targetStep=effectiveSpeedSteps => sourcePosition=lastSourceIndex
        var ratio = (double)(targetStep - 1) / (effectiveSpeedSteps - 1);
        var sourcePosition = ratio * lastSourceIndex;

        var lowerIndex = (int)Math.Floor(sourcePosition);
        var upperIndex = (int)Math.Ceiling(sourcePosition);

        double interpolatedSpeedV;

        if (lowerIndex == upperIndex)
        {
            interpolatedSpeedV = orderedBaseEntries[lowerIndex].SpeedV;
        }
        else
        {
            var lower = orderedBaseEntries[lowerIndex];
            var upper = orderedBaseEntries[upperIndex];

            // Lokale Interpolation zwischen zwei benachbarten Stützpunkten.
            var localRatio = sourcePosition - lowerIndex;
            interpolatedSpeedV = lower.SpeedV + (upper.SpeedV - lower.SpeedV) * localRatio;
        }

        result.Add(new SpeedEntry(interpolatedSpeedV, targetStep));
    }

    return result;
}
    
    /// <summary>
    /// Creates a speedV-based lookup table (1 km/h resolution) from step-based entries.
    /// </summary>
    /// <param name="stepBasedEntries">Step-based speed entries.</param>
    /// <param name="vMax">Maximum speedV for the generated table.</param>
    /// <returns>speedV-indexed speed entries from 1 to <paramref name="vMax"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when no step-based entries are provided.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="vMax"/> is smaller than 1.</exception>
    private static List<SpeedEntry> BuildSpeedStageTable(List<SpeedEntry> stepBasedEntries, int vMax)
    {
        if (stepBasedEntries.Count == 0)
            throw new ArgumentException("At least one step-based entry is required.", nameof(stepBasedEntries));

        if (vMax < 1)
            throw new ArgumentOutOfRangeException(nameof(vMax), vMax, "Maximum speed (VMax) must be at least 1.");

        var result = new List<SpeedEntry>();

        for (var v = 1; v <= vMax; v++)
        {
            var closestStep = stepBasedEntries.MinBy(e => Math.Abs(e.SpeedV - v));
            // speedV ist exakt die Geschwindigkeitsstufe (1 km/h Raster)
            result.Add(closestStep with { SpeedV = v });
        }

        return result;
    }
}

/// <summary>
/// Represents a speedV-to-decoder-step mapping entry.
/// </summary>
public readonly record struct SpeedEntry(double SpeedV, int SpeedStep);

/// <summary>
/// Represents a generic decoder function parsed from <c>&lt;functiontable&gt;</c>.
/// </summary>
/// <param name="Number">Function number from attribute <c>no</c>.</param>
/// <param name="Type">Function type from attribute <c>type</c>.</param>
/// <param name="Attributes">All remaining attributes as key/value pairs.</param>
public readonly record struct VehicleFunctions(
    int Number,
    string Type,
    IReadOnlyList<KeyValuePair<string, string>> Attributes);

/// <summary>
/// Represents a sound function parsed from <c>&lt;functiontable&gt;</c>.
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
/// Represents a decoder function that is neither a headlight, sound nor auto-coupling function,
/// as parsed from <c>&lt;functiontable&gt;</c>.
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

