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
namespace OTD.HardwareControl.Drivers;

/// <summary>
/// Device information of the LoDi S88 commander.
/// Contains the configuration of both S88 buses (Bus 1 and Bus 2).
/// Each S88 module has a fixed 16 sensor inputs.
/// </summary>
/// <param name="Bus1SensorCount">Number of sensors on Bus 1 (calculated: number of modules × 16 sensor inputs per module).</param>
/// <param name="Bus2SensorCount">Number of sensors on Bus 2 (calculated: number of modules × 16 sensor inputs per module).</param>
/// <param name="RawPayload">Raw response payload for future extensions (e.g. firmware version).</param>
internal record S88DeviceInfo(
    int Bus1SensorCount,
    int Bus2SensorCount,
    byte[] RawPayload
);
