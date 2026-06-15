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
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl;

/// <summary>
/// Common contract for all binary feedback providers (track occupied / free).
/// Module address management is the responsibility of the concrete implementation.
/// Consumers work exclusively with flat, continuous sensor numbers (1-based).
/// </summary>
public interface IFeedback
{
    /// <summary>Unique provider id from commandstations.xml.</summary>
    Guid UniqueId { get; }

    /// <summary>True when the provider is connected.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Total number of sensors managed by this provider.
    /// Sensor numbers run from 1 to <see cref="SensorCount"/>.
    /// </summary>
    int SensorCount { get; }

    /// <summary>Raised when a sensor changes state.</summary>
    event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged;

    /// <summary>Opens the provider connection and applies startup behavior.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the provider connection.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the last known state of a single sensor.
    /// </summary>
    /// <param name="sensorNumber">1-based sensor number (1 .. <see cref="SensorCount"/>).</param>
    RailSensorState GetSensorState(int sensorNumber);


    /// <summary>
    /// Queries the device for current sensor states and returns a complete snapshot.
    /// Key = sensor number (1-based), Value = current state.
    /// </summary>
    Task<IReadOnlyDictionary<int, RailSensorState>> QueryAllSensorsAsync(
        CancellationToken cancellationToken = default);
}
