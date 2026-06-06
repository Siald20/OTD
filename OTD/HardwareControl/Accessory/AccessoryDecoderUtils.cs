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

namespace OTD.HardwareControl.Accessory;

/// <summary>
/// Provides parsing and mapping helpers for accessory decoder configuration values.
/// </summary>
internal static class AccessoryDecoderUtils
{
    /// <summary>
    /// Reads the accessory decoder protocol from configuration.
    /// </summary>
    internal static DecoderProtocol GetProtocol(string? protocolElement)
        => AccessoryUtils.GetDecoderProtocol(protocolElement);

    /// <summary>
    /// Reads the decoder address from configuration.
    /// </summary>
    internal static int GetAddress(string? addressElementValue)
    {
        if (!string.IsNullOrWhiteSpace(addressElementValue) &&
            int.TryParse(addressElementValue, out var address) &&
            address is >= 1 and <= 10239)
        {
            return address;
        }

        throw new ArgumentOutOfRangeException(
            nameof(addressElementValue),
            addressElementValue,
            $"Missing or invalid decoder address: '{addressElementValue}'. Valid range is 1..10239.");
    }


    /// <summary>
    /// Converts a protocol enum value to the XML protocol string used in decoder elements.
    /// </summary>
    internal static string GetProtocolElementValue(DecoderProtocol protocol)
    {
        return protocol switch
        {
            DecoderProtocol.Dcc => "DCC",
            DecoderProtocol.DccExtended => "DCC-Extended",
            DecoderProtocol.Motorola => "Motorola",
            DecoderProtocol.M3 => "M3",
            DecoderProtocol.Mfx => "Mfx",
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported accessory decoder protocol.")
        };
    }
}

