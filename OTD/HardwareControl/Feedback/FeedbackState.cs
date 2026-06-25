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
///     Occupancy state of a single rail sensor (track circuit / S88 contact).
/// </summary>
public enum RailSensorState
{
    /// <summary>Sensor not occupied – track section free.</summary>
    Inactive,

    /// <summary>Sensor occupied – track section in use.</summary>
    Active
}

/// <summary>
///     Extensible per-sensor metadata.
/// </summary>
public readonly record struct SensorInfo(int SensorNumber, string SensorName, RailSensorState State);

/// <summary>
///     Event args raised when a sensor changes state.
/// </summary>
public sealed class SensorStateChangedEventArgs(
    Guid providerUid,
    SensorInfo sensorInfo) : EventArgs
{
    /// <summary>Unique id of the feedback provider that raised the event.</summary>
    public Guid ProviderUid { get; } = providerUid;

    /// <summary>
    ///     Provider-global sensor number (1-based, continuous across all modules).
    ///     Module address details are managed internally by the provider.
    /// </summary>
    public int SensorNumber { get; } = sensorInfo.SensorNumber;

    /// <summary>
    ///     Human-readable sensor label for orientation (e.g. "1.2").
    /// </summary>
    public string SensorName { get; } = sensorInfo.SensorName;

    /// <summary>New state of the sensor.</summary>
    public RailSensorState State { get; } = sensorInfo.State;
}