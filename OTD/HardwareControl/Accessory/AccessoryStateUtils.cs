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
/// Provides helpers for parsing accessory state definitions.
/// </summary>
internal static class AccessoryStateUtils
{
    /// <summary>
    /// Parses a required integer attribute from a state decoder element.
    /// </summary>
    internal static int ParseRequiredIntAttribute(XElement element, string attributeName, string context)
    {
        var rawValue = element.Attribute(attributeName)?.Value;
        if (int.TryParse(rawValue, out var value))
            return value;

        throw new InvalidOperationException($"Invalid or missing attribute '{attributeName}' in {context}.");
    }

    /// <summary>
    /// Parses the required decoder output value from a state decoder element.
    /// </summary>
    internal static int ParseRequiredOutputValueAttribute(XElement element, string context)
    {
        var outputRawValue = element.Attribute("outputvalue")?.Value;
        if (int.TryParse(outputRawValue, out var outputValue))
            return outputValue;

        throw new InvalidOperationException(
            $"Invalid or missing attribute 'outputvalue' in {context}.");
    }

    /// <summary>
    /// Resolves the current accessory state from observed readback output values.
    /// Matches the observed outputs against all configured state definitions and returns
    /// the state if exactly one state matches. Returns null if no state matches or
    /// multiple states match (ambiguous).
    /// </summary>
    /// <param name="states">All configured state definitions of the accessory</param>
    /// <param name="lastReadBackValuesByAddress">Dictionary mapping decoder addresses to last-observed output values</param>
    /// <returns>State ID if exactly one state matches, null otherwise</returns>
    internal static string? ResolveCurrentStateFromReadBack(
        IReadOnlyList<AccessoryStateDefinition> states,
        Dictionary<int, int> lastReadBackValuesByAddress)
    {
        var matchingStates = states
            .Where(state => state.Commands.All(command =>
                lastReadBackValuesByAddress.TryGetValue(command.Address, out var observedValue) &&
                observedValue == command.OutputValue))
            .Select(state => state.State)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matchingStates.Count == 1 ? matchingStates[0] : null;
    }

    /// <summary>
    /// Parses all state elements and returns lists of state definitions and id-indexed states.
    /// Validates state IDs, output values, and protocol compliance during parsing.
    /// </summary>
    /// <param name="stateElements">XML state elements from accessory configuration</param>
    /// <param name="accessoryId">Accessor UID for error reporting</param>
    /// <param name="type">Accessory type for state metadata</param>
    /// <param name="subtype">Accessory subtype for state metadata</param>
    /// <param name="id">Accessory id for state metadata</param>
    /// <param name="interlocking">Accessory interlocking for state metadata</param>
    /// <param name="protocol">Decoder protocol (for output value validation)</param>
    /// <param name="states">Output: parsed state definitions</param>
    /// <param name="statesById">Output: dictionary mapping state IDs to definitions</param>
    internal static void ParseAccessoryStates(
        IEnumerable<XElement> stateElements,
        Guid accessoryId,
        AccessoryType type,
        string subtype,
        string id,
        string interlocking,
        AccessoryDecoderProtocol protocol,
        IList<AccessoryStateDefinition> states,
        IDictionary<string, AccessoryStateDefinition> statesById)
    {
        foreach (var stateElement in stateElements)
        {
            var stateIdAttribute = stateElement.Attribute("name");
            var stateId = stateIdAttribute is null ? string.Empty : stateIdAttribute.Value.Trim();

            if (string.IsNullOrWhiteSpace(stateId))
                throw new InvalidOperationException($"Accessory '{accessoryId}' contains a <state> without required 'id' attribute.");

            var descriptionAttribute = stateElement.Attribute("description");
            var description = descriptionAttribute is null ? string.Empty : descriptionAttribute.Value.Trim();
            var commands = new List<AccessoryStateCommand>();

            foreach (var decoderElement in stateElement.Elements("decoder"))
            {
                var address = AccessoryStateUtils.ParseRequiredIntAttribute(decoderElement, "address", $"state '{stateId}'");
                var outputValue = AccessoryStateUtils.ParseRequiredOutputValueAttribute(
                    decoderElement,
                    $"state '{stateId}'");

                if (outputValue is < 0 or > 255)
                    throw new InvalidOperationException(
                        $"Accessory '{accessoryId}' contains invalid outputvalue '{outputValue}' in state '{stateId}'. Valid range is 0..255.");

                if (protocol == AccessoryDecoderProtocol.Dcc && outputValue is not (0 or 1))
                    throw new InvalidOperationException(
                        $"Accessory '{accessoryId}' uses DCC basic protocol and requires outputvalue 0 or 1 in state '{stateId}' (address {address}).");

                commands.Add(new AccessoryStateCommand(type, subtype, id, interlocking, stateId, address, outputValue));
            }

            if (commands.Count == 0)
                throw new InvalidOperationException($"Accessory '{accessoryId}' contains state '{stateId}' without any <decoder> commands.");

            var stateDefinition = new AccessoryStateDefinition(type, subtype, id, interlocking, stateId, description, commands);
            states.Add(stateDefinition);
            statesById.Add(stateDefinition.State, stateDefinition);
        }
    }

    /// <summary>
    /// Validates that a command's metadata matches the accessory's properties.
    /// Throws if there's a mismatch.
    /// </summary>
    internal static AccessoryStateCommand ValidateCommandMetadata(
        Guid accessoryId,
        AccessoryStateDefinition state,
        AccessoryStateCommand command,
        AccessoryType type,
        string subtype,
        string id,
        string interlocking)
    {
        if (command.Type != type ||
            !string.Equals(command.Subtype, subtype, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(command.Id, id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(command.Interlocking, interlocking, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(command.State, state.State, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Accessory '{accessoryId}' contains inconsistent metadata for state '{state.State}'.");
        }

        return command;
    }
}
