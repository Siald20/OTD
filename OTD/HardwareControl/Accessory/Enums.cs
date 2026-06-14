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
namespace OTD.HardwareControl;

/// <summary>
/// Defines operating mode of the train.
/// </summary>
public enum AccessoryType
{
    /// <summary>
    /// Turnout
    /// </summary>
    Turnout,

    /// <summary>
    /// Signal
    /// </summary>
    Signal,

    /// <summary>
    /// Other accessory (e.g. lighting, function model)
    /// </summary>
    Other
}

/// <summary>
/// Defines supported decoder communication protocols.
/// </summary>
public enum AccessoryDecoderProtocol
{
    /// <summary>
    /// DCC accessory protocol
    /// </summary>
    Dcc,

    /// <summary>
    /// extended DCC accessory protocol
    /// </summary>
    DccExtended,

    /// <summary>
    /// MM2 (Maerklin Motorola) accessory protocol
    /// </summary>
    Motorola,

    /// <summary>
    /// Tams M3 accessory protocol for mfx decoders (without feedback).
    /// </summary>
    M3,

    /// <summary>
    /// Maerklin mfx accessory protocol.
    /// </summary>
    Mfx
}

/// <summary>
/// Defines on/off states for decoders.
/// </summary>
public enum AccessoryFunctionState
{
    /// <summary>
    /// Function state is undefined (before initialization).
    /// </summary>
    Undefined,

    /// <summary>
    /// Function is switched off.
    /// </summary>
    Off,

    /// <summary>
    /// Function is switched on.
    /// </summary>
    On
}

