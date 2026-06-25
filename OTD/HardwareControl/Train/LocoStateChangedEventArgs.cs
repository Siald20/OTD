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
///     Event data for locomotive state updates from the command station.
///     Contains either a speed-step/direction update or a function update.
///     Protocol-independent abstraction for all command stations.
/// </summary>
public sealed class LocoStateChangedEventArgs : EventArgs
{
    public LocoStateChangedEventArgs(
        int address,
        int? speedStep,
        VehicleDirection direction,
        int? functionNumber,
        LocoDecoderFunctionState? functionStateValue,
        bool isEventPacket)
    {
        Address = address;
        SpeedStep = speedStep;
        Direction = direction;
        FunctionNumber = functionNumber;
        FunctionStateValue = functionStateValue;
        IsEventPacket = isEventPacket;
    }

    public int Address { get; }
    public int? SpeedStep { get; }
    public VehicleDirection Direction { get; }
    public int? FunctionNumber { get; }
    public LocoDecoderFunctionState? FunctionStateValue { get; }
    public bool IsEventPacket { get; }

    public bool HasSpeedUpdate => SpeedStep.HasValue;
    public bool HasFunctionUpdate => FunctionNumber.HasValue && FunctionStateValue.HasValue;
}