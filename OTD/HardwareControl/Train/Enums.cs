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
///     Defines operating mode of the train.
/// </summary>
public enum TrainOperatingMode
{
    /// <summary>
    ///     train shut down (locomotives unregistered at the command station)
    /// </summary>
    ShutDown,

    /// <summary>
    ///     Train in parking mode (locomotives registred at the command station, driving commands ignored, parking lights on,
    ///     if available).
    /// </summary>
    Parking,

    /// <summary>
    ///     Train is shunting. Drive commands are accepted with reduced maximum speed.
    /// </summary>
    Shunting,

    /// <summary>
    ///     Train is travelling. Drive commands are accepted in this mode.
    /// </summary>
    Travelling
}

/// <summary>
///     Defines the selected travelling direction of the train.
/// </summary>
public enum TrainDirection
{
    /// <summary>
    ///     Train travels in logical direction A (forward).
    /// </summary>
    A,

    /// <summary>
    ///     Train travels in logical direction B (backward).
    /// </summary>
    B
}

/// <summary>
///     Defines the orientation of a vehicle within the train composition.
/// </summary>
public enum VehicleOrientation
{
    /// <summary>
    ///     The vehicle's decoder direction matches the train's selected travel direction.
    /// </summary>
    Normal,

    /// <summary>
    ///     The vehicle's decoder direction is opposite to the train's selected travel direction.
    /// </summary>
    Reverse
}

public enum LocoDecoderProtocol
{
    /// <summary>
    ///     DCC protocol with 14 speed steps.
    /// </summary>
    Dcc14,

    /// <summary>
    ///     DCC protocol with 28 speed steps.
    /// </summary>
    Dcc28,

    /// <summary>
    ///     DCC protocol with 128 speed steps (126 effective speed steps).
    /// </summary>
    Dcc128,

    /// <summary>
    ///     MM2 (Maerklin Motorola) protocol with 27 speed steps.
    /// </summary>
    Motorola,

    /// <summary>
    ///     Tams M3 protocol for mfx decoders (without feedback).
    /// </summary>
    M3,

    /// <summary>
    ///     Maerklin mfx protocol.
    /// </summary>
    Mfx
}

// Todo: Prüfen, ob "Undefined" Zustände benötigt werden, ggf. IsInitalized-Property einführen,
// Todo: um Fahrbefehle entgegengenommen werden dürfen.
/// <summary>
///     Defines the decoder-level logical travel direction of a vehicle.
/// </summary>
public enum VehicleDirection
{
    /// <summary>
    ///     Direction is undefined (before initialization).
    /// </summary>
    Undefined,

    /// <summary>
    ///     Travel direction forward
    /// </summary>
    Forward,

    /// <summary>
    ///     Travel direction backward
    /// </summary>
    Backward
}

/// <summary>
///     Defines on/off states for decoders.
/// </summary>
public enum LocoDecoderFunctionState
{
    /// <summary>
    ///     Function state is undefined (before initialization).
    /// </summary>
    Undefined,

    /// <summary>
    ///     Function is switched off.
    /// </summary>
    Off,

    /// <summary>
    ///     Function is switched on.
    /// </summary>
    On
}

public enum HeadlightMode
{
    /// <summary>
    ///     Headlights are turned on.
    /// </summary>
    Off,

    /// <summary>
    ///     Headlights are turned off.
    /// </summary>
    On,

    /// <summary>
    ///     Headlights are switched automatically, based on operating mode.
    /// </summary>
    Auto
}