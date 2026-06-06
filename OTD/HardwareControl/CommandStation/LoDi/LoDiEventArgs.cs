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
using OTD.HardwareControl;
using OTD.HardwareControl.Train;
// Note: LocoStateChangedEventArgs has been moved to OTD.HardwareControl.Train for protocol abstraction.
using LocoStateChangedEventArgs = OTD.HardwareControl.Train.LocoStateChangedEventArgs;

namespace OTD.HardwareControl.CommandStation.LoDi;

// -------------------------------------------------------------------------
// Allgemeine Verbindungs-Events
// -------------------------------------------------------------------------

/// <summary>Argumente für Verbindungsänderungs-Events</summary>
public sealed class LoDiConnectionChangedEventArgs : EventArgs
{
    /// <summary>Gibt an, ob das Gerät verbunden ist.</summary>
    public bool IsConnected { get; }

    /// <summary>Optionale Fehlermeldung (nur bei Verbindungsverlust)</summary>
    public string? ErrorMessage { get; }

    public LoDiConnectionChangedEventArgs(bool isConnected, string? errorMessage = null)
    {
        IsConnected = isConnected;
        ErrorMessage = errorMessage;
    }
}


/// <summary>Argumente für den Empfang eines Rohdaten-Pakets</summary>
internal sealed class LoDiPacketReceivedEventArgs : EventArgs
{
    /// <summary>Das empfangene, geparste Paket</summary>
    public LoDiPacket Packet { get; }

    public LoDiPacketReceivedEventArgs(LoDiPacket packet) => Packet = packet;
}

// -------------------------------------------------------------------------
// S88-Rückmeldungs-Events
// -------------------------------------------------------------------------

/// <summary>
///     Argumente für S88-Zustandsänderungs-Events.
///     Wird ausgelöst, wenn sich der Zustand eines S88-Kontakts ändert.
/// </summary>
public sealed class S88StateChangedEventArgs : EventArgs
{
    /// <summary>Adresse des S88-Moduls (1-basiert)</summary>
    public int ModuleAddress { get; }

    /// <summary>Kontaktnummer innerhalb des Moduls (1-basiert)</summary>
    public int ContactNumber { get; }

    /// <summary>Neuer Zustand des Kontakts (<c>true</c> = belegt, <c>false</c> = frei)</summary>
    public bool IsOccupied { get; }

    public S88StateChangedEventArgs(int moduleAddress, int contactNumber, bool isOccupied)
    {
        ModuleAddress = moduleAddress;
        ContactNumber = contactNumber;
        IsOccupied = isOccupied;
    }
}

/// <summary>
///     Argumente für einen vollständigen S88-Modulstatus.
///     Enthält den Zustand aller Kontakte eines S88-Moduls.
/// </summary>
public sealed class S88ModuleStateEventArgs : EventArgs
{
    /// <summary>Adresse des S88-Moduls (1-basiert)</summary>
    public int ModuleAddress { get; }

    /// <summary>
    ///     Zustandsbitmask des Moduls.
    ///     Bit 0 = Kontakt 1, Bit 1 = Kontakt 2, ... Bit 15 = Kontakt 16.
    ///     Ein gesetztes Bit bedeutet: Abschnitt belegt.
    /// </summary>
    public ushort StateBitmask { get; }

    public S88ModuleStateEventArgs(int moduleAddress, ushort stateBitmask)
    {
        ModuleAddress = moduleAddress;
        StateBitmask = stateBitmask;
    }

    /// <summary>Gibt den Zustand eines einzelnen Kontakts zurück (1-basiert).</summary>
    public bool GetContactState(int contactNumber)
    {
        if (contactNumber < 1 || contactNumber > 16)
            throw new ArgumentOutOfRangeException(nameof(contactNumber), "Kontaktnummer muss zwischen 1 und 16 liegen.");

        return (StateBitmask & (1 << (contactNumber - 1))) != 0;
    }
}

// -------------------------------------------------------------------------
// Geräte-Discovery
// -------------------------------------------------------------------------

/// <summary>Repräsentiert ein via UDP-Discovery gefundenes LoDi-Gerät.</summary>
public sealed class LoDiDeviceInfo
{
    /// <summary>IP-Adresse des gefundenen Geräts</summary>
    public string IpAddress { get; }

    /// <summary>TCP-Port des gefundenen Geräts</summary>
    public int TcpPort { get; }

    /// <summary>Gerätetyp (z.B. "LoDi-Rektor", "LoDi-S88-Commander")</summary>
    public string DeviceType { get; }

    /// <summary>Gerätename / Bezeichnung</summary>
    public string DeviceName { get; }

    /// <summary>Seriennummer des Geräts</summary>
    public string SerialNumber { get; }

    /// <summary>Firmware-Version des Geräts</summary>
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

