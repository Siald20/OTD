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

/// <summary>
///     Represents a LoDi protocol packet (send and receive direction).
/// </summary>
/// <remarks>
///     Packet format (API General, pp. 2-3):
///     
///     UDP:  [PacketType][Command][PacketNumber][Payload...]
///     TCP:  [Length High][Length Low][PacketType][Command][PacketNumber][Payload...]
///     
///     - PacketType: 0x20=REQ, 0x21=ACK, 0x22=EVT, 0x23=BUSY, 0x3F=NACK
///     - Command: Command code (e.g. 0x0F for GetVersion)
///     - PacketNumber: Reflected in the response packet (0x00..0xFF)
///     - Payload: Optional, length-specific
///     - Length (only TCP): Bytes from PacketType to the last Payload byte
///     
///     There is no XOR checksum - error protection is provided by the TCP/UDP protocol.
/// </remarks>
internal sealed class LoDiPacket
{
    /// <summary>Packet type (0x20=REQ, 0x21=ACK, 0x22=EVT, 0x23=BUSY, 0x3F=NACK)</summary>
    public byte PacketType { get; }

    /// <summary>Command code</summary>
    public byte Command { get; }

    /// <summary>Packet number (reflected in responses)</summary>
    public byte PacketNumber { get; }

    /// <summary>Payload of the packet</summary>
    public byte[] Payload { get; }

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>Creates a new packet with all components.</summary>
    public LoDiPacket(byte packetType, byte command, byte packetNumber, byte[] payload)
    {
        PacketType = packetType;
        Command = command;
        PacketNumber = packetNumber;
        Payload = payload;
    }

    /// <summary>Creates a new packet without payload.</summary>
    public LoDiPacket(byte packetType, byte command, byte packetNumber) 
        : this(packetType, command, packetNumber, Array.Empty<byte>()) { }

    // -------------------------------------------------------------------------
    // Serialization (Packet → Byte Array for Sending)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Serializes the packet into a UDP byte array (without length prefix).
    /// </summary>
    public byte[] ToUdpBytes()
    {
        var packet = new byte[3 + Payload.Length];
        packet[0] = PacketType;
        packet[1] = Command;
        packet[2] = PacketNumber;
        if (Payload.Length > 0)
            Array.Copy(Payload, 0, packet, 3, Payload.Length);
        return packet;
    }

    /// <summary>
    ///     Serializes the packet into a TCP byte array (with 2-byte length prefix).
    /// </summary>
    public byte[] ToTcpBytes()
    {
        var dataLength = 3 + Payload.Length; // PacketType + Command + PacketNumber + Payload
        var packet = new byte[2 + dataLength];

        // Length prefix (Big-Endian): Length of data from PacketType
        packet[0] = (byte)((dataLength >> 8) & 0xFF);
        packet[1] = (byte)(dataLength & 0xFF);

        // Data
        packet[2] = PacketType;
        packet[3] = Command;
        packet[4] = PacketNumber;
        if (Payload.Length > 0)
            Array.Copy(Payload, 0, packet, 5, Payload.Length);

        return packet;
    }

    // -------------------------------------------------------------------------
    // Deserialization (Byte Array → Packet on Reception)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Tries to create a packet from a UDP byte array.
    /// </summary>
    public static bool TryParseUdp(byte[] raw, out LoDiPacket? packet)
    {
        packet = null;

        if (raw.Length < LoDiProtocol.MinUdpPacketLength)
            return false;

        var packetType = raw[0];
        var command = raw[1];
        var packetNumber = raw[2];

        var payload = new byte[raw.Length - 3];
        if (payload.Length > 0)
            Array.Copy(raw, 3, payload, 0, payload.Length);

        packet = new LoDiPacket(packetType, command, packetNumber, payload);
        return true;
    }

    /// <summary>
    ///     Tries to create a packet from a TCP byte array.
    ///     Considers the 2-byte length prefix.
    /// </summary>
    public static bool TryParseTcp(byte[] raw, out LoDiPacket? packet, out int totalLength)
    {
        packet = null;
        totalLength = 0;

        if (raw.Length < LoDiProtocol.MinTcpPacketLength)
            return false;

        // Read length prefix (Big-Endian)
        var length = ((raw[0] & 0xFF) << 8) | (raw[1] & 0xFF);
        totalLength = 2 + length; // 2 length bytes + data

        // At least PacketType + Command + PacketNumber must be included.
        if (length < LoDiProtocol.MinUdpPacketLength)
            return false;

        // Check if enough data is available
        if (raw.Length < totalLength)
            return false;

        var packetType = raw[2];
        var command = raw[3];
        var packetNumber = raw[4];

        var payloadLength = length - 3; // Length minus PacketType, Command, PacketNumber
        var payload = new byte[payloadLength];
        if (payloadLength > 0)
            Array.Copy(raw, 5, payload, 0, payloadLength);

        packet = new LoDiPacket(packetType, command, packetNumber, payload);
        return true;
    }

    // -------------------------------------------------------------------------
    // Helper Methods
    // -------------------------------------------------------------------------

    public override string ToString() =>
        $"LoDiPacket [{LoDiProtocol.GetPacketTypeName(PacketType)} (0x{PacketType:X2}), " +
        $"Cmd={LoDiProtocol.GetCommandName(Command)} (0x{Command:X2}), " +
        $"Nr=0x{PacketNumber:X2}, Payload={Payload.Length} bytes]";
}
