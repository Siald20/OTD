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
///     Protocol constants for the LoDi device API from Lokstore Digital.
/// </summary>
/// <remarks>
///     The command codes are based on the LoDi device API documentation:
///     https://lokstoredigital.jimdoweb.com/service/geräte-api/
///     TODO: Verify all byte values according to the current documentation.
/// </remarks>
internal static class LoDiProtocol
{
    // -------------------------------------------------------------------------
    // Connection parameters
    // -------------------------------------------------------------------------

    /// <summary>Default TCP/UDP port for LoDi devices (API General, p. 2)</summary>
    public const int DefaultTcpPort = 11092;

    /// <summary>Default UDP port for device discovery (broadcast)</summary>
    public const int DiscoveryUdpPort = 11092;

    /// <summary>Timeout for UDP operations in milliseconds (API General, p. 3: "within 200ms")</summary>
    public const int NetworkTimeoutMs = 200;

    /// <summary>Timeout for device discovery in milliseconds</summary>
    public const int DiscoveryTimeoutMs = 200;

    // -------------------------------------------------------------------------
    // Packet structure (API General, pp. 2-3)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     UDP packet format: [PacketType][Command][PacketNumber][Payload...]
    ///     TCP packet format: [Length High][Length Low][PacketType][Command][PacketNumber][Payload...]
    ///     Length = Bytes from PacketType to the last Payload byte (excl. length bytes)
    /// </summary>
    public const int TcpHeaderLength = 2; // Length bytes (High + Low)

    public const int MinUdpPacketLength = 3; // PacketType + Command + PacketNumber
    public const int MinTcpPacketLength = 5; // Length(2) + PacketType + Command + PacketNumber

    // -------------------------------------------------------------------------
    // Packet types (API General, pp. 2-3)
    // -------------------------------------------------------------------------

    /// <summary>Request from the control software (answered with ACK, NACK, or BUSY)</summary>
    public const byte PacketTypeRequest = 0x20;

    /// <summary>Confirmation: REQ packet was successfully processed</summary>
    public const byte PacketTypeAck = 0x21;

    /// <summary>Event: Packet automatically sent about changes (e.g. S88 feedback)</summary>
    public const byte PacketTypeEvent = 0x22;

    /// <summary>Busy: Execution of the REQ packet not yet completed, later ACK/NACK follows</summary>
    public const byte PacketTypeBusy = 0x23;

    /// <summary>Nack: REQ packet is defective, request cannot be processed</summary>
    public const byte PacketTypeNack = 0x3F;

    // -------------------------------------------------------------------------
    // Payload flags and values
    // -------------------------------------------------------------------------

    /// <summary>Device type: LoDi-Rektor (API LoDi-Rektor p. 1)</summary>
    public const byte DeviceTypeLoDiRektor = 0x03;

    /// <summary>Device type: LoDi-S88-Commander LX (API S88 p. 1)</summary>
    public const byte DeviceTypeLoDiS88Commander = 0x0A;

    // -------------------------------------------------------------------------
    // Diagnostic helper methods
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the readable name of a command code (for diagnostic log).
    ///     Unknown codes are returned as "Unknown".
    /// </summary>
    public static string GetCommandName(byte command)
    {
        return command switch
        {
            Commands.General.GetVersion => "GetVersion",
            Commands.General.CloseConnection => "CloseConnection",
            Commands.General.SetWatchdog => "SetWatchdog",
            Commands.General.DeviceConfigGet => "DeviceConfigGet",
            Commands.Booster.On => "BoosterOn",
            Commands.Booster.Status => "BoosterStatus",
            Commands.Booster.Diagnostics => "BoosterDiagnostics",
            Commands.Booster.ConfigGet => "GetBoosterConfig",
            Commands.Decoder.LocoRelease => "DecoderLocoRelease",
            Commands.Decoder.LocoSpeed => "DecoderLocoSpeed",
            Commands.Decoder.LocoFunction => "DecoderLocoFunction",
            Commands.Decoder.LocoBinary => "DecoderLocoBinary",
            Commands.Decoder.AccessoryState => "DecoderAccessoryState",
            Commands.Decoder.LocoCv => "DecoderLocoCv",
            Commands.Decoder.AccessoryCv => "DecoderAccessoryCv",
            // 0x30/0x31 werden je nach Gerätefamilie unterschiedlich interpretiert.
            Commands.Cv.ReadServiceMode => "0x30 (CvReadServiceMode / S88QueryModules)",
            Commands.Cv.WriteServiceMode => "0x31 (CvWriteServiceMode / S88GetContactState)",
            Commands.Cv.ReadPom => "CvReadPom",
            Commands.Cv.WritePom => "CvWritePom",
            Commands.S88.SetFeedbackUpdatesActive => "S88SetFeedbackUpdatesActive",
            Commands.S88.DeviceConfigGet => "S88DeviceConfigGet",
            _ => "Unknown"
        };
    }

    /// <summary>
    ///     Returns the readable name of a packet type (for diagnostic log).
    /// </summary>
    public static string GetPacketTypeName(byte packetType)
    {
        return packetType switch
        {
            PacketTypeRequest => "REQ",
            PacketTypeAck => "ACK",
            PacketTypeEvent => "EVT",
            PacketTypeBusy => "BUSY",
            PacketTypeNack => "NACK",
            _ => "UNKNOWN"
        };
    }

    // -------------------------------------------------------------------------
    // Commands (Command Codes, API General + Rektor + S88-Commander)
    // -------------------------------------------------------------------------

    /// <summary>Command codes for communication with LoDi devices, grouped by device or function area.</summary>
    public static class Commands
    {
        public static class General
        {
            /// <summary>GetVersion: Query device ID and FW version (0x0F, API General p. 4)</summary>
            public const byte GetVersion = 0x0F;

            /// <summary>CloseConnection: Disconnect (0x0C, API LoDi-Rektor p. 3)</summary>
            public const byte CloseConnection = 0x0C;

            /// <summary>SetWatchdog: Enable/disable watchdog monitoring (0x9A, API LoDi-Rektor p. 4)</summary>
            public const byte SetWatchdog = 0x9A;

            /// <summary>DeviceConfigGet: Read device configuration (0x99, API LoDi-Rektor p. 3)</summary>
            public const byte DeviceConfigGet = 0x99;
        }

        public static class Booster
        {
            /// <summary>BoosterOn: Turn booster on/off (0x90, API LoDi-Rektor p. 7)</summary>
            public const byte On = 0x90;

            /// <summary>BoosterStatus: Query booster status (0x91, API LoDi-Rektor p. 8)</summary>
            public const byte Status = 0x91;

            /// <summary>BoosterDiagnostics: Query voltage/current/temp (0x92, API LoDi-Rektor p. 9)</summary>
            public const byte Diagnostics = 0x92;

            /// <summary>GetBoosterConfig: Read booster settings (0x93, API LoDi-Rektor p. 10)</summary>
            public const byte ConfigGet = 0x93;
        }

        public static class Decoder
        {
            /// <summary>AccessoryDecoder: Remove locomotive address from refresh (0xC0, API AccessoryDecoder commands)</summary>
            public const byte LocoRelease = 0xC0;

            /// <summary>AccessoryDecoder: Set locomotive speed (0xC1, API AccessoryDecoder commands)</summary>
            public const byte LocoSpeed = 0xC1;

            /// <summary>AccessoryDecoder: Set locomotive functions (0xC2, API AccessoryDecoder commands)</summary>
            public const byte LocoFunction = 0xC2;

            /// <summary>AccessoryDecoder: Binary functions (0xC3, API AccessoryDecoder commands)</summary>
            public const byte LocoBinary = 0xC3;

            /// <summary>AccessoryDecoder: Set accessory state (0xC4, API AccessoryDecoder commands)</summary>
            public const byte AccessoryState = 0xC4;

            /// <summary>AccessoryDecoder: Locomotive CV operation (0xC5, API AccessoryDecoder commands)</summary>
            public const byte LocoCv = 0xC5;

            /// <summary>AccessoryDecoder: Accessory CV operation (0xC6, API AccessoryDecoder commands)</summary>
            public const byte AccessoryCv = 0xC6;
        }

        public static class Cv
        {
            /// <summary>CV read (Service Mode, preliminary; opcode range overlaps with S88 commands on other devices)</summary>
            public const byte ReadServiceMode = 0x30;

            /// <summary>CV write (Service Mode, preliminary; opcode range overlaps with S88 commands on other devices)</summary>
            public const byte WriteServiceMode = 0x31;

            /// <summary>CV read (POM, preliminary)</summary>
            public const byte ReadPom = 0x32;

            /// <summary>CV write (POM, preliminary)</summary>
            public const byte WritePom = 0x33;
        }

        public static class S88
        {
            /// <summary>S88 feedback reception globally enable/disable (0x01, payload: 0x01=active, 0x00=inactive).</summary>
            public const byte SetFeedbackUpdatesActive = 0x01;

            /// <summary>S88 DeviceConfigGet: Read S88 configuration (0x35, API S88 p. 3)</summary>
            public const byte DeviceConfigGet = 0x35;

            /// <summary>S88 QueryModules: Query S88 modules (0x30, opcode range overlaps with CV commands on other devices)</summary>
            public const byte QueryModules = 0x30;

            /// <summary>
            ///     S88 GetContactState: Query contact state or S88 status operations (0x31, opcode range overlaps with CV
            ///     commands on other devices)
            /// </summary>
            public const byte GetContactState = 0x31;
        }
    }
}

/// <summary>
///     Preconfigured protocol bytes for AccessoryDecoder commands (Protocol field, "AccessoryDecoder commands").
/// </summary>
internal static class LoDiDecoderProtocol
{
    /// <summary>DCC 14 speed steps (Main=0x1, Sub=0x1)</summary>
    public const byte Dcc14 = 0x11;

    /// <summary>DCC 28 speed steps (Main=0x1, Sub=0x3)</summary>
    public const byte Dcc28 = 0x31;

    /// <summary>DCC 126 speed steps (Main=0x1, Sub=0x4)</summary>
    public const byte Dcc126 = 0x41;

    /// <summary>DCC 126 Extended (Main=0x1, Sub=0x5)</summary>
    public const byte Dcc126Extended = 0x51;

    /// <summary>Motorola 14 speed steps (Main=0x2, Sub=0x1)</summary>
    public const byte Motorola14 = 0x21;

    /// <summary>M3/mfx (Main=0x3, no Sub)</summary>
    public const byte M3 = 0x30;

    public static byte Build(byte protocolMain, byte protocolSub)
    {
        return (byte)(((protocolSub & 0x0F) << 4) | (protocolMain & 0x0F));
    }
}