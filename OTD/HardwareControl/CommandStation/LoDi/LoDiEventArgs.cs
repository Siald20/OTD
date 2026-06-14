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

namespace OTD.HardwareControl.Drivers;

// -------------------------------------------------------------------------
// Allgemeine Verbindungs-Events
// -------------------------------------------------------------------------

/// <summary>Arguments for connection state change events.</summary>
internal sealed class LoDiConnectionChangedEventArgs : EventArgs
{
    /// <summary>Indicates whether a connection to the Commander exists.</summary>
    public bool IsConnected { get; }

    /// <summary>Optional error message (only set on connection loss).</summary>
    public string? ErrorMessage { get; }

    public LoDiConnectionChangedEventArgs(bool isConnected, string? errorMessage = null)
    {
        IsConnected = isConnected;
        ErrorMessage = errorMessage;
    }
}

/// <summary>Arguments for receiving a raw LoDi packet.</summary>
internal sealed class LoDiPacketReceivedEventArgs : EventArgs
{
    /// <summary>The received and parsed packet.</summary>
    public LoDiPacket Packet { get; }

    public LoDiPacketReceivedEventArgs(LoDiPacket packet) => Packet = packet;
}

// -------------------------------------------------------------------------
// S88-Rückmeldungs-Events
// -------------------------------------------------------------------------

/// <summary>
/// Arguments for S88 state change events.
/// Raised when a single contact on an S88 module changes state.
/// </summary>
internal sealed class S88StateChangedEventArgs : EventArgs
{
    /// <summary>Address of the S88 module (1-based).</summary>
    public int ModuleAddress { get; }

    /// <summary>Contact number within the module (1-based, 1-16).</summary>
    public int ContactNumber { get; }

    /// <summary>New contact state (<c>true</c> = occupied, <c>false</c> = free).</summary>
    public bool IsOccupied { get; }

    public S88StateChangedEventArgs(int moduleAddress, int contactNumber, bool isOccupied)
    {
        ModuleAddress = moduleAddress;
        ContactNumber = contactNumber;
        IsOccupied = isOccupied;
    }
}

/// <summary>
/// Arguments for a full S88 module state snapshot.
/// Contains the state of all 16 contacts of one S88 module.
/// </summary>
internal sealed class S88ModuleStateEventArgs : EventArgs
{
    /// <summary>Address of the S88 module (1-based).</summary>
    public int ModuleAddress { get; }

    /// <summary>
    /// State bitmask of the module.
    /// Bit 0 = contact 1, Bit 1 = contact 2, ... Bit 15 = contact 16.
    /// A set bit means: section occupied.
    /// </summary>
    public ushort StateBitmask { get; }

    /// <summary>
    /// Position of this module within the current S88MelderGet response (1-based).
    /// 0 means unknown / not part of a 0x30 snapshot.
    /// </summary>
    public int SnapshotIndex { get; }

    /// <summary>
    /// Total module count in the current S88MelderGet response (first payload byte).
    /// 0 means unknown / not part of a 0x30 snapshot.
    /// </summary>
    public int SnapshotCount { get; }

    public S88ModuleStateEventArgs(int moduleAddress, ushort stateBitmask, int snapshotIndex = 0, int snapshotCount = 0)
    {
        ModuleAddress = moduleAddress;
        StateBitmask = stateBitmask;
        SnapshotIndex = snapshotIndex;
        SnapshotCount = snapshotCount;
    }

    /// <summary>Returns the state of a single contact (1-based, 1-16).</summary>
    public bool GetContactState(int contactNumber)
    {
        if (contactNumber < 1 || contactNumber > 16)
            throw new ArgumentOutOfRangeException(nameof(contactNumber), "Contact number must be between 1 and 16.");

        return (StateBitmask & (1 << (contactNumber - 1))) != 0;
    }
}

// -------------------------------------------------------------------------
// Geräte-Discovery
// -------------------------------------------------------------------------

/// <summary>Represents a LoDi device found via UDP discovery.</summary>
internal sealed class LoDiDeviceInfo
{
    /// <summary>IP address of the discovered device.</summary>
    public string IpAddress { get; }

    /// <summary>TCP port of the discovered device.</summary>
    public int TcpPort { get; }

    /// <summary>Device type (e.g. "LoDi-Rektor", "LoDi-S88-Commander").</summary>
    public string DeviceType { get; }

    /// <summary>Device name / label.</summary>
    public string DeviceName { get; }

    /// <summary>Serial number of the device.</summary>
    public string SerialNumber { get; }

    /// <summary>Firmware version of the device.</summary>
    public string FirmwareVersion { get; }

    public LoDiDeviceInfo(string ipAddress, int tcpPort, string deviceType,
        string deviceName, string serialNumber, string firmwareVersion)
    {
        IpAddress = ipAddress;
        TcpPort = tcpPort;
        DeviceType = deviceType;
        DeviceName = deviceName;
        SerialNumber = serialNumber;
        FirmwareVersion = firmwareVersion;
    }

    public override string ToString() =>
        $"{DeviceType} '{DeviceName}' (S/N: {SerialNumber}, FW: {FirmwareVersion}) @ {IpAddress}:{TcpPort}";
}
