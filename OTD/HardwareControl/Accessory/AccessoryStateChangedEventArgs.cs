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
///     Event arguments for accessory decoder state changes reported by a command station.
/// </summary>
public class AccessoryStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Backward-compatible view of <see cref="FunctionState"/>.
    /// </summary>
    //public bool IsActive => FunctionState == this.FunctionState.On;

    /// <summary>
    ///     Creates a new AccessoryStateChangedEventArgs instance.
    /// </summary>
    public AccessoryStateChangedEventArgs(int address, int outputValue, AccessoryFunctionState functionState)
    {
        Address = address;
        OutputValue = outputValue;
        FunctionState = functionState;
    }

    /// <summary>
    ///     Address of the accessory decoder.
    /// </summary>
    public int Address { get; }

    /// <summary>
    ///     Protocol-specific accessory output value that was reported.
    /// </summary>
    public int OutputValue { get; }

    /// <summary>
    ///     New channel function state.
    /// </summary>
    public AccessoryFunctionState FunctionState { get; }

    /// <summary>
    ///     Backward-compatible alias of <see cref="FunctionState" />.
    /// </summary>
    public AccessoryFunctionState State => FunctionState;

    public override string ToString()
    {
        return $"AccessoryDecoder {Address} OutputValue {OutputValue} = {FunctionState}";
    }
}