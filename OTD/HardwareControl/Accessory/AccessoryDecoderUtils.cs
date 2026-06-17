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

namespace OTD.HardwareControl;

/// <summary>
/// Provides parsing and mapping helpers for accessory decoder configuration values.
/// </summary>
internal static class AccessoryDecoderUtils
{
    /// <summary>
    /// Reads the accessory decoder protocol from configuration.
    /// </summary>
    internal static AccessoryDecoderProtocol GetProtocol(string? protocolElement)
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
    internal static string GetProtocolElementValue(AccessoryDecoderProtocol protocol)
    {
        return protocol switch
        {
            AccessoryDecoderProtocol.Dcc => "DCC",
            AccessoryDecoderProtocol.DccExtended => "DCC-Extended",
            AccessoryDecoderProtocol.Motorola => "Motorola",
            AccessoryDecoderProtocol.M3 => "M3",
            AccessoryDecoderProtocol.Mfx => "Mfx",
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported accessory decoder protocol.")
        };
    }
}

