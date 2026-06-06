// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - AccessoryControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// Dieses Programm ist freie Software: Sie koennen es unter den Bedingungen
// der GNU General Public License, wie von der Free Software Foundation,
// entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder spaeteren
// veroeffentlichten Version, weiterverbreiten und/oder modifizieren.

using System;
using System.Linq;
using System.Xml.Linq;

namespace OTD.HardwareControl.Accessory;

/// <summary>
/// Provides XML parsing helpers for accessory configuration values from <c>accessory.xml</c>.
/// </summary>
internal static class AccessoryUtils
{
    /// <summary>
    /// Reads the accessory configuration element for a specific accessory UID.
    /// </summary>
    internal static XElement GetAccessoryConfiguration(Guid accessoryId)
    {
        return Train.TrainUtils.ReadXConfiguration("accessory", accessoryId)
               ?? throw new InvalidOperationException(
                   $"Accessory configuration not found for accessory '{accessoryId}'.");
    }

    /// <summary>
    /// Parses the accessory type attribute from <c>accessory.xml</c>.
    /// </summary>
    internal static AccessoryType GetAccessoryType(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
            throw new InvalidOperationException("Missing required accessory type value.");

        return rawType.Trim().ToLowerInvariant() switch
        {
            "turnout" => AccessoryType.Turnout,
            "signal" => AccessoryType.Signal,
            "other" => AccessoryType.Other,
            _ => throw new InvalidOperationException($"Unsupported accessory type '{rawType}'.")
        };
    }

    /// <summary>
    /// Gets an XML configuration element for an accessory decoder.
    /// </summary>
    /// <param name="address">Decoder address</param>
    /// <param name="protocol">Decoder protocol</param>
    /// <returns>XML configuration element suitable for AccessoryDecoder construction</returns>
    internal static XElement GetDecoderConfiguration(int address, DecoderProtocol protocol)
    {
        return new XElement("decoder",
            new XElement("protocol", AccessoryDecoderUtils.GetProtocolElementValue(protocol)),
            new XElement("address", address.ToString()));
    }
    
    /// <summary>
    /// Parses accessory decoder protocol from configuration.
    /// </summary>
    internal static DecoderProtocol GetDecoderProtocol(string? rawProtocol)
    {
        if (string.IsNullOrWhiteSpace(rawProtocol))
            throw new InvalidOperationException(
                "Missing required <protocol> value in accessory decoder configuration. " +
                "Valid values are: DCC, DCC-Extended, Motorola, M3, Mfx.");

        return rawProtocol.Trim().ToLowerInvariant() switch
        {
            "dcc" or "dcc14" or "dcc28" or "dcc128" => DecoderProtocol.Dcc,
            "dcc-extended" or "dccextended" => DecoderProtocol.DccExtended,
            // "motorola" => DecoderProtocol.Motorola,
            // "m3" => DecoderProtocol.M3,
            // "mfx" => DecoderProtocol.Mfx,
            _ => throw new InvalidOperationException(
                $"Unsupported accessory decoder protocol '{rawProtocol}'. " +
                "Valid values are: DCC, DCC-Extended, Motorola, M3, Mfx.")
        };
    }

    /// <summary>
    /// Reads activation time (milliseconds) from decoder configuration.
    /// Returns 0 when missing, invalid or non-positive.
    /// </summary>
    internal static int GetActivationTime(XElement accessoryConfiguration)
    {
        var activationTimeElement = accessoryConfiguration.Element("decoder")?.Element("activationtime");
        return activationTimeElement is not null
               && int.TryParse(activationTimeElement.Value.Trim(), out var at)
               && at > 0
            ? at
            : 0;
    }

    /// <summary>
    /// Reads delay time (milliseconds) from decoder configuration.
    /// Returns 0 when missing, invalid or non-positive.
    /// Delay time is used to introduce delays between consecutive decoder address changes
    /// in multi-address accessories to prevent decoder overload.
    /// </summary>
    internal static int GetDelayTime(XElement accessoryConfiguration)
    {
        var delayTimeElement = accessoryConfiguration.Element("decoder")?.Element("delaytime");
        return delayTimeElement is not null
               && int.TryParse(delayTimeElement.Value.Trim(), out var dt)
               && dt > 0
            ? dt
            : 0;
    }
    
    /// <summary>
    /// Returns all configured state elements and validates that at least one state exists.
    /// </summary>
    internal static XElement[] GetStateElements(XElement accessoryConfiguration, Guid accessoryId)
    {
        var stateElements = accessoryConfiguration.Element("states")?.Elements("state").ToList() ?? [];
        return stateElements.Count == 0 ? throw new InvalidOperationException($"Accessory '{accessoryId}' does not define any <state> entries.") : stateElements.ToArray();
    }

}
