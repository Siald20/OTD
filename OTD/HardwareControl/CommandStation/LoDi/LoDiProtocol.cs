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

namespace OTD.HardwareControl.CommandStation.LoDi;

/// <summary>
///     Protokollkonstanten für die LoDi Geräte-API von Lokstore Digital.
/// </summary>
/// <remarks>
///     Die Befehlscodes basieren auf der LoDi Geräte-API Dokumentation:
///     https://lokstoredigital.jimdoweb.com/service/geräte-api/
///     TODO: Alle Bytewerte anhand der aktuellen Dokumentation verifizieren.
/// </remarks>
internal static class LoDiProtocol
{
    // -------------------------------------------------------------------------
    // Verbindungsparameter
    // -------------------------------------------------------------------------

    /// <summary>Standard TCP/UDP-Port für LoDi-Geräte (API Allgemeines, S. 2)</summary>
    public const int DefaultTcpPort = 11092;

    /// <summary>Standard UDP-Port für Geräte-Discovery (Broadcast)</summary>
    public const int DiscoveryUdpPort = 11092;

    /// <summary>Timeout für UDP-Operationen in Millisekunden (API Allgemeines, S. 3: "innerhalb von 200ms")</summary>
    public const int NetworkTimeoutMs = 200;

    /// <summary>Timeout für die Geräte-Discovery in Millisekunden</summary>
    public const int DiscoveryTimeoutMs = 200;

    // -------------------------------------------------------------------------
    // Paket-Struktur (API Allgemeines, S. 2-3)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     UDP-Paketformat: [Pakettyp][Kommando][Paketnummer][Payload...]
    ///     TCP-Paketformat: [Länge High][Länge Low][Pakettyp][Kommando][Paketnummer][Payload...]
    ///     Länge = Bytes von Pakettyp bis zum letzten Payload-Byte (nicht inkl. Längenbytes)
    /// </summary>
    public const int TcpHeaderLength = 2; // Länge-Bytes (High + Low)
    public const int MinUdpPacketLength = 3; // Pakettyp + Kommando + Paketnummer
    public const int MinTcpPacketLength = 5; // Länge(2) + Pakettyp + Kommando + Paketnummer

    // -------------------------------------------------------------------------
    // Pakettypen (API Allgemeines, S. 2-3)
    // -------------------------------------------------------------------------

    /// <summary>Anfrage von der Steuersoftware (wird mit ACK, NACK oder BUSY beantwortet)</summary>
    public const byte PacketTypeRequest = 0x20;

    /// <summary>Bestätigung: REQ-Paket wurde erfolgreich bearbeitet</summary>
    public const byte PacketTypeAck = 0x21;

    /// <summary>Event: Automatisch versendetes Paket über Änderungen (z.B. S88-Rückmeldungen)</summary>
    public const byte PacketTypeEvent = 0x22;

    /// <summary>Busy: Ausführung des REQ-Pakets noch nicht abgeschlossen, später ACK/NACK folgt</summary>
    public const byte PacketTypeBusy = 0x23;

    /// <summary>Nack: REQ-Paket ist fehlerhaft, Anfrage kann nicht bearbeitet werden</summary>
    public const byte PacketTypeNack = 0x3F;

    // -------------------------------------------------------------------------
    // Befehle (Command Codes, API Allgemeines + Rektor + S88-Commander)
    // -------------------------------------------------------------------------

    /// <summary>Befehlscodes für die Kommunikation mit LoDi-Geräten</summary>
    public static class Commands
    {
        // --- Allgemein (API Allgemeines) ---

        /// <summary>GetVersion: Gerätekennung und FW-Version abfragen (0x0F, API Allgemeines S. 4)</summary>
        public const byte GetVersion = 0x0F;

        /// <summary>CloseConnection: Verbindung trennen (0x0C, API LoDi-Rektor S. 3)</summary>
        public const byte CloseConnection = 0x0C;

        /// <summary>SetWatchdog: Watchdog-Überwachung ein/aus (0x9A, API LoDi-Rektor S. 4)</summary>
        public const byte SetWatchdog = 0x9A;

        /// <summary>DeviceConfigGet: Gerätekonfiguration lesen (0x99, API LoDi-Rektor S. 3)</summary>
        public const byte DeviceConfigGet = 0x99;

        // --- Booster-Steuerung (API LoDi-Rektor) ---

        /// <summary>BoosterOn: Booster ein/aus (0x90, API LoDi-Rektor S. 7)</summary>
        public const byte BoosterOn = 0x90;

        /// <summary>BoosterStatus: Booster-Status abfragen (0x91, API LoDi-Rektor S. 8)</summary>
        public const byte BoosterStatus = 0x91;

        /// <summary>BoosterDiagnostics: Spannung/Strom/Temp abfragen (0x92, API LoDi-Rektor S. 9)</summary>
        public const byte BoosterDiagnostics = 0x92;

        /// <summary>GetBoosterConfig: Booster-Einstellungen lesen (0x93, API LoDi-Rektor S. 10)</summary>
        public const byte GetBoosterConfig = 0x93;

        // --- Lok-/Zubehör-/CV-Steuerung ---

        /// <summary>AccessoryDecoder: Lokadresse aus Refresh entfernen (0xC0, API AccessoryDecoder commands)</summary>
        public const byte DecoderLocoRelease = 0xC0;

        /// <summary>AccessoryDecoder: Lokgeschwindigkeit setzen (0xC1, API AccessoryDecoder commands)</summary>
        public const byte DecoderLocoSpeed = 0xC1;

        /// <summary>AccessoryDecoder: Lokfunktionen setzen (0xC2, API AccessoryDecoder commands)</summary>
        public const byte DecoderLocoFunction = 0xC2;

        /// <summary>AccessoryDecoder: Binärfunktionen (0xC3, API AccessoryDecoder commands)</summary>
        public const byte DecoderLocoBinary = 0xC3;

        /// <summary>AccessoryDecoder: Zubehörzustand setzen (0xC4, API AccessoryDecoder commands)</summary>
        public const byte DecoderAccessoryState = 0xC4;

        /// <summary>AccessoryDecoder: Lok-CV Operation (0xC5, API AccessoryDecoder commands)</summary>
        public const byte DecoderLocoCv = 0xC5;

        /// <summary>AccessoryDecoder: Zubehör-CV Operation (0xC6, API AccessoryDecoder commands)</summary>
        public const byte DecoderAccessoryCv = 0xC6;

        /// <summary>CV lesen (Service Mode, vorläufig)</summary>
        public const byte CvReadServiceMode = 0x30;

        /// <summary>CV schreiben (Service Mode, vorläufig)</summary>
        public const byte CvWriteServiceMode = 0x31;

        /// <summary>CV lesen (POM, vorläufig)</summary>
        public const byte CvReadPom = 0x32;

        /// <summary>CV schreiben (POM, vorläufig)</summary>
        public const byte CvWritePom = 0x33;

        // --- S88-Rückmeldung (API LoDi-S88-Commander LX) ---

        /// <summary>S88 DeviceConfigGet: S88-Konfiguration lesen (0x35, API S88 S. 3)</summary>
        public const byte S88DeviceConfigGet = 0x35;

        /// <summary>S88 QueryModules: S88-Module abfragen (0x30, Annahme basierend auf API-Struktur)</summary>
        public const byte S88QueryModules = 0x30;

        /// <summary>S88 GetContactState: Kontaktzustand abfragen (0x31, API S88 S. 10)</summary>
        public const byte S88GetContactState = 0x31;

        /// <summary>S88-Kompatibilitätsalias</summary>
        public const byte S88Query = S88QueryModules;

        /// <summary>S88-Kompatibilitätsalias (Subscribe nicht final spezifiziert)</summary>
        public const byte S88Subscribe = S88GetContactState;

        /// <summary>S88-Kompatibilitätsalias (Unsubscribe nicht final spezifiziert)</summary>
        public const byte S88Unsubscribe = S88GetContactState;
    }

    // -------------------------------------------------------------------------
    // Antwort-Codes (mit PacketType*)
    // -------------------------------------------------------------------------

    /// <summary>Antwortcodes sind Kombinationen aus PacketType + Kommando</summary>
    /// <remarks>
    ///     Beispiel: GetVersion-Anfrage mit Pakettyp 0x20 + Kommando 0x0F
    ///     Antwort: Pakettyp 0x21 + Kommando 0x0F (spiegelt die Anfrage)
    /// </remarks>
    public static class Responses
    {
        /// <summary>S88-Zustandsantwort / EVT-Kommando laut Beispiel in Allgemeine API</summary>
        public const byte S88State = Commands.S88GetContactState;

        /// <summary>S88-Zustandsänderung (gleiches Kommando, unterschieden über Pakettyp EVT)</summary>
        public const byte S88StateChanged = Commands.S88GetContactState;
    }

    // -------------------------------------------------------------------------
    // Nutzdaten-Flags und Werte
    // -------------------------------------------------------------------------

    /// <summary>Gerätetyp: LoDi-Rektor (API LoDi-Rektor S. 1)</summary>
    public const byte DeviceTypeLoDiRektor = 0x03;

    /// <summary>Gerätetyp: LoDi-S88-Commander LX (API S88 S. 1)</summary>
    public const byte DeviceTypeLoDiS88Commander = 0x0A;

    // -------------------------------------------------------------------------
    // Diagnose-Hilfsmethoden
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Gibt den lesbaren Namen eines Befehlscodes zurück (für Diagnose-Log).
    ///     Unbekannte Codes werden als "Unknown" zurückgegeben.
    /// </summary>
    public static string GetCommandName(byte command) => command switch
    {
        Commands.GetVersion          => "GetVersion",
        Commands.CloseConnection     => "CloseConnection",
        Commands.SetWatchdog         => "SetWatchdog",
        Commands.DeviceConfigGet     => "DeviceConfigGet",
        Commands.BoosterOn           => "BoosterOn",
        Commands.BoosterStatus       => "BoosterStatus",
        Commands.BoosterDiagnostics  => "BoosterDiagnostics",
        Commands.GetBoosterConfig    => "GetBoosterConfig",
        Commands.DecoderLocoRelease  => "DecoderLocoRelease",
        Commands.DecoderLocoSpeed    => "DecoderLocoSpeed",
        Commands.DecoderLocoFunction => "DecoderLocoFunction",
        Commands.DecoderLocoBinary   => "DecoderLocoBinary",
        Commands.DecoderAccessoryState => "DecoderAccessoryState",
        Commands.DecoderLocoCv       => "DecoderLocoCv",
        Commands.DecoderAccessoryCv  => "DecoderAccessoryCv",
        // 0x30/0x31/0x32/0x33 sind sowohl CV- als auch S88-Codes (Überlappung im Protokoll)
        Commands.CvReadServiceMode   => "CvRead/S88Query",
        Commands.CvWriteServiceMode  => "CvWrite/S88GetContactState",
        Commands.CvReadPom           => "CvReadPom",
        Commands.CvWritePom          => "CvWritePom",
        Commands.S88DeviceConfigGet  => "S88DeviceConfigGet",
        _                            => "Unknown"
    };

    /// <summary>
    ///     Gibt den lesbaren Namen eines Pakettyps zurück (für Diagnose-Log).
    /// </summary>
    public static string GetPacketTypeName(byte packetType) => packetType switch
    {
        PacketTypeRequest => "REQ",
        PacketTypeAck     => "ACK",
        PacketTypeEvent   => "EVT",
        PacketTypeBusy    => "BUSY",
        PacketTypeNack    => "NACK",
        _                 => "UNKNOWN"
    };
}

/// <summary>
///     Vorkonfigurierte Protokoll-Bytes für AccessoryDecoder-Kommandos (Protocol-Feld, "AccessoryDecoder commands").
/// </summary>
internal static class LoDiDecoderProtocol
{
    /// <summary>DCC 14 Fahrstufen (Main=0x1, Sub=0x1)</summary>
    public const byte Dcc14 = 0x11;

    /// <summary>DCC 28 Fahrstufen (Main=0x1, Sub=0x3)</summary>
    public const byte Dcc28 = 0x31;

    /// <summary>DCC 126 Fahrstufen (Main=0x1, Sub=0x4)</summary>
    public const byte Dcc126 = 0x41;

    /// <summary>DCC 126 Extended (Main=0x1, Sub=0x5)</summary>
    public const byte Dcc126Extended = 0x51;

    /// <summary>Motorola 14 Fahrstufen (Main=0x2, Sub=0x1)</summary>
    public const byte Motorola14 = 0x21;

    /// <summary>M3/mfx (Main=0x3, kein Sub)</summary>
    public const byte M3 = 0x30;

    public static byte Build(byte protocolMain, byte protocolSub)
        => (byte)(((protocolSub & 0x0F) << 4) | (protocolMain & 0x0F));
}

