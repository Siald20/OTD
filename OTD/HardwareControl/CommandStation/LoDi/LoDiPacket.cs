// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// der GNU General Public License, wie von der Free Software Foundation,
// entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// veröffentlichten Version, weiterverbreiten und/oder modifizieren.
//
// Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// Siehe die GNU General Public License für weitere Details.
//
// Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;

namespace OTD.HardwareControl.CommandStation.LoDi;

/// <summary>
///     Repräsentiert ein LoDi-Protokollpaket (Sende- und Empfangsrichtung).
/// </summary>
/// <remarks>
///     Paketformat (API Allgemeines, S. 2-3):
///     
///     UDP:  [Pakettyp][Kommando][Paketnummer][Payload...]
///     TCP:  [Länge High][Länge Low][Pakettyp][Kommando][Paketnummer][Payload...]
///     
///     - Pakettyp: 0x20=REQ, 0x21=ACK, 0x22=EVT, 0x23=BUSY, 0x3F=NACK
///     - Kommando: Befehlscode (z.B. 0x0F für GetVersion)
///     - Paketnummer: Wird im Antwort-Paket gespiegelt (0x00..0xFF)
///     - Payload: Optional, längespezifisch
///     - Länge (nur TCP): Bytes von Pakettyp bis zum letzten Payload-Byte
///     
///     Es gibt keine XOR-Checksumme – Fehlerschutz erfolgt durch TCP/UDP-Protokoll.
/// </remarks>
internal sealed class LoDiPacket
{
    /// <summary>Pakettyp (0x20=REQ, 0x21=ACK, 0x22=EVT, 0x23=BUSY, 0x3F=NACK)</summary>
    public byte PacketType { get; }

    /// <summary>Befehlscode</summary>
    public byte Command { get; }

    /// <summary>Paketnummer (wird in Antworten gespiegelt)</summary>
    public byte PacketNumber { get; }

    /// <summary>Nutzdaten des Pakets</summary>
    public byte[] Payload { get; }

    // -------------------------------------------------------------------------
    // Konstruktion
    // -------------------------------------------------------------------------

    /// <summary>Erstellt ein neues Paket mit allen Komponenten.</summary>
    public LoDiPacket(byte packetType, byte command, byte packetNumber, byte[] payload)
    {
        PacketType = packetType;
        Command = command;
        PacketNumber = packetNumber;
        Payload = payload ?? Array.Empty<byte>();
    }

    /// <summary>Erstellt ein neues Paket ohne Payload.</summary>
    public LoDiPacket(byte packetType, byte command, byte packetNumber) 
        : this(packetType, command, packetNumber, Array.Empty<byte>()) { }

    // -------------------------------------------------------------------------
    // Serialisierung (Paket → Byte-Array zum Senden)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Serialisiert das Paket in ein UDP-Byte-Array (ohne Längenpräfix).
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
    ///     Serialisiert das Paket in ein TCP-Byte-Array (mit 2-Byte Längenpräfix).
    /// </summary>
    public byte[] ToTcpBytes()
    {
        var dataLength = 3 + Payload.Length; // Pakettyp + Kommando + Paketnummer + Payload
        var packet = new byte[2 + dataLength];

        // Längenpräfix (Big-Endian): Länge der Daten ab Pakettyp
        packet[0] = (byte)((dataLength >> 8) & 0xFF);
        packet[1] = (byte)(dataLength & 0xFF);

        // Daten
        packet[2] = PacketType;
        packet[3] = Command;
        packet[4] = PacketNumber;
        if (Payload.Length > 0)
            Array.Copy(Payload, 0, packet, 5, Payload.Length);

        return packet;
    }

    // -------------------------------------------------------------------------
    // Deserialisierung (Byte-Array → Paket beim Empfangen)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Versucht, aus einem UDP-Byte-Array ein Paket zu erzeugen.
    /// </summary>
    public static bool TryParseUdp(byte[] raw, out LoDiPacket? packet)
    {
        packet = null;

        if (raw == null || raw.Length < LoDiProtocol.MinUdpPacketLength)
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
    ///     Versucht, aus einem TCP-Byte-Array ein Paket zu erzeugen.
    ///     Berücksichtigt das 2-Byte Längenpräfix.
    /// </summary>
    public static bool TryParseTcp(byte[] raw, out LoDiPacket? packet, out int totalLength)
    {
        packet = null;
        totalLength = 0;

        if (raw == null || raw.Length < LoDiProtocol.MinTcpPacketLength)
            return false;

        // Längenpräfix auslesen (Big-Endian)
        var length = ((raw[0] & 0xFF) << 8) | (raw[1] & 0xFF);
        totalLength = 2 + length; // 2 Längenbytes + Daten

        // Prüfen, ob genug Daten vorhanden
        if (raw.Length < totalLength)
            return false;

        var packetType = raw[2];
        var command = raw[3];
        var packetNumber = raw[4];

        var payloadLength = length - 3; // Länge minus Pakettyp, Kommando, Paketnummer
        var payload = new byte[payloadLength];
        if (payloadLength > 0)
            Array.Copy(raw, 5, payload, 0, payloadLength);

        packet = new LoDiPacket(packetType, command, packetNumber, payload);
        return true;
    }

    // -------------------------------------------------------------------------
    // Hilfsmethoden
    // -------------------------------------------------------------------------

    public override string ToString() =>
        $"LoDiPacket [{LoDiProtocol.GetPacketTypeName(PacketType)} (0x{PacketType:X2}), " +
        $"Cmd={LoDiProtocol.GetCommandName(Command)} (0x{Command:X2}), " +
        $"Nr=0x{PacketNumber:X2}, Payload={Payload.Length} bytes]";
}

