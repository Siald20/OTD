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
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OTD.HardwareControl;

/// <summary>
/// Static helper methods for device configuration loading (XML parsing utilities).
/// Used by CommandStation and Feedback.
/// </summary>
internal static class CommandStationUtils
{
    private const string ConfigFileName = "commandstations.xml";

    /// <summary>
    /// Returns the default path to commandstations.xml relative to the application base directory.
    /// </summary>
    internal static string GetDefaultConfigFilePath()
    {
        var appDataPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AppData");
        return Path.GetFullPath(Path.Combine(appDataPath, ConfigFileName));
    }

    /// <summary>
    /// Loads and parses an XDocument from the given file path.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the file is missing or XML is malformed.</exception>
    internal static XDocument LoadXDocument(string filePath)
    {
        if (!File.Exists(filePath))
            throw new InvalidOperationException($"Device configuration file not found: {filePath}");

        try
        {
            return XDocument.Load(filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load device configuration '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads a required string attribute from an XElement.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the attribute is missing or empty.</exception>
    internal static string RequireAttribute(XElement element, string attributeName, string context)
    {
        var value = element.Attribute(attributeName)?.Value;
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"Missing required attribute '{attributeName}' in {context}.");
    }

    /// <summary>
    /// Parses an optional boolean attribute. Returns <paramref name="defaultValue"/> when absent.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not a valid boolean.</exception>
    internal static bool ParseBoolAttribute(XElement? element, string attributeName, bool defaultValue)
    {
        if (element is null)
            return defaultValue;

        var value = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (bool.TryParse(value, out var parsed))
            return parsed;

        throw new InvalidOperationException($"Invalid boolean value '{value}' for attribute '{attributeName}'.");
    }

    /// <summary>
    /// Parses an optional integer attribute within an inclusive range.
    /// Returns <paramref name="defaultValue"/> when absent.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not a valid integer or out of range.</exception>
    internal static int ParseIntAttribute(XElement? element, string attributeName, int defaultValue,
        int min = int.MinValue, int max = int.MaxValue)
    {
        if (element is null)
            return defaultValue;

        var value = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (!int.TryParse(value, out var parsed) || parsed < min || parsed > max)
            throw new InvalidOperationException(
                $"Invalid integer value '{value}' for attribute '{attributeName}'. Valid range: {min}..{max}.");

        return parsed;
    }

    /// <summary>
    /// Parses an optional integer value from a child element.
    /// Returns <paramref name="defaultValue"/> when the child element is missing.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is not a valid integer or out of range.</exception>
    internal static int ParseIntElement(XElement? parentElement, string childElementName, int defaultValue,
        int min = int.MinValue, int max = int.MaxValue)
    {
        if (parentElement is null)
            return defaultValue;

        var childElement = parentElement.Element(childElementName);
        if (childElement is null)
            return defaultValue;

        var value = childElement.Value;
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (!int.TryParse(value, out var parsed) || parsed < min || parsed > max)
            throw new InvalidOperationException(
                $"Invalid integer value '{value}' in <{childElementName}>. Valid range: {min}..{max}.");

        return parsed;
    }

    /// <summary>
    /// Parses a required Guid attribute from an XElement.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the attribute is missing or not a valid UUID.</exception>
    internal static Guid RequireGuidAttribute(XElement element, string context)
    {
        var raw = RequireAttribute(element, "uid", context);
        if (Guid.TryParse(raw, out var uid))
            return uid;

        throw new InvalidOperationException($"Invalid uid '{raw}' in {context}. A UUID is required.");
    }

    /// <summary>
    /// Loads a single &lt;commandstation&gt; element by UID from commandstations.xml.
    /// The returned element is a detached copy and can be passed to driver constructors.
    /// </summary>
    internal static XElement LoadCommandStationElement(Guid stationUid, string? configFilePath = null)
    {
        var filePath = string.IsNullOrWhiteSpace(configFilePath)
            ? GetDefaultConfigFilePath()
            : configFilePath;

        var document = LoadXDocument(filePath);
        var stationElement = document.Root?
            .Elements("commandstation")
            .FirstOrDefault(element =>
            {
                var uidRaw = element.Attribute("uid")?.Value;
                return Guid.TryParse(uidRaw, out var parsedUid) && parsedUid == stationUid;
            });

        if (stationElement is null)
            throw new InvalidOperationException(
                $"Command station with uid '{stationUid}' not found in '{filePath}'.");

        return new XElement(stationElement);
    }
}

