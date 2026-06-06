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
using System.Threading;
using System.Threading.Tasks;
using AccessoryStateChangedEventArgs = OTD.HardwareControl.Accessory.AccessoryStateChangedEventArgs;
using OTD.HardwareControl.CommandStation.LoDi;
using OTD.HardwareControl.Train;

namespace OTD.HardwareControl.CommandStation;

/// <summary>
///     Abstrahierte Schnittstelle einer DCC-Kommandozentrale.
///     Ermöglicht die Ansteuerung von Lokomotiven, Funktionen und Gleisspannungsversorgung
///     unabhängig vom Hersteller der Kommandozentrale.
/// </summary>
/// <remarks>
///     Implementierungen können LoDi-Rektor, Märklin Central, Roco z21, etc. sein.
/// </remarks>
public interface ICommandStation : IDisposable
{
    // -------------------------------------------------------------------------
    // Eigenschaften
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Gibt an, ob eine aktive Verbindung zur Kommandozentrale besteht.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    ///     Wird ausgelöst, wenn die Zentrale ein Lokzustands-Update liefert
    ///     (z.B. Fahrstufe/Fahrtrichtung oder Funktionszustand).
    /// </summary>
    event EventHandler<LocoStateChangedEventArgs>? LocoStateChanged;

    /// <summary>
    ///     Wird ausgelöst, wenn die Zentrale ein Zubehörzustands-Update liefert
    ///     (Decoderadresse + value + Schaltzustand).
    /// </summary>
    event EventHandler<AccessoryStateChangedEventArgs>? AccessoryStateChanged;

    // -------------------------------------------------------------------------
    // Verbindung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Stellt eine Verbindung zur Kommandozentrale her.
    /// </summary>
    /// <param name="address">IP-Adresse oder Hostname der Zentrale</param>
    /// <param name="port">Port der Zentrale (Standard hängt von Implementierung ab)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    Task ConnectAsync(string address, int port, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Trennt die Verbindung zur Kommandozentrale.
    /// </summary>
    Task DisconnectAsync();

    // -------------------------------------------------------------------------
    // Gleisversorgung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Schaltet die Gleisversorgung (Fahrstrom) ein oder aus.
    /// </summary>
    /// <param name="isOn"><c>true</c> = Fahrstrom ein; <c>false</c> = Fahrstrom aus</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    Task SetPowerAsync(bool isOn, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fragt den aktuellen Gleisspannungszustand ab.
    /// </summary>
    /// <returns><c>true</c> wenn Fahrstrom eingeschaltet ist, <c>false</c> sonst</returns>
    Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default);

    // -------------------------------------------------------------------------
    // Lokomotivsteuerung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Registriert AccessoryDecoder-Parameter, die fuer die gesamte Lebensdauer der Instanz unveraendert bleiben.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="protocol">AccessoryDecoder-Protokoll (z.B. DCC14, DCC28, DCC128, Motorola, M3, mfx)</param>
    /// <param name="effectiveSpeedSteps">Effektiv nutzbare Fahrstufen des Decoders (z.B. 126 bei DCC128).</param>
    void InitializeDecoder(int address, OTD.HardwareControl.Train.DecoderProtocol protocol, int effectiveSpeedSteps);

    /// <summary>
    ///     Setzt Geschwindigkeit und Fahrtrichtung einer Lokomotive.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="speedStep">
    ///     Fahrstufe (0 = Halt, je nach Protokoll 1–14, 1–28 oder 1–126).
    ///     Wert 0 bewirkt einen regulären Halt (kein Nothalt).
    /// </param>
    /// <param name="direction">Fahrtrichtung</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    Task SetLocoSpeedAsync(int address, int speedStep, VehicleDirection direction,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Schaltet eine Lokomotivfunktion ein oder aus.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="functionNumber">Funktionsnummer (0 = F0/Licht, 1–28 = F1–F28)</param>
    /// <param name="isOn"><c>true</c> = Funktion ein; <c>false</c> = Funktion aus</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    Task SetLocoFunctionAsync(int address, int functionNumber, bool isOn,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Führt einen Nothalt für eine bestimmte Lokomotive aus.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    Task EmergencyStopAsync(int address, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Führt einen Nothalt für alle Lokomotiven gleichzeitig aus.
    /// </summary>
    /// <param name="cancellationToken">Abbruchtoken</param>
    Task EmergencyStopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fragt die aktuellen Zustände (Geschwindigkeit und Funktionen) eines Decoders von der Zentrale ab
    ///     und triggert LocoStateChanged-Events für jeden abgerufenen Zustand.
    ///     Dies ist nützlich zur Initialisierung eines Decoders mit den realen Zuständen der Zentrale.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="functionList">Liste der Funktionsnummern, deren Zustand abgefragt werden soll.
    ///     Wenn leer, werden keine Funktionen abgefragt.</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    Task QueryLocoFunctionsStateAsync(int address, System.Collections.Generic.IReadOnlyList<int> functionList,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fragt die aktuelle Geschwindigkeit (Fahrstufe) und Fahrtrichtung einer Lokomotive von der Zentrale ab
    ///     und triggert ein LocoStateChanged-Event mit den abgerufenen Werten.
    ///     Dies ist nützlich zur Initialisierung eines Decoders mit dem realen Zustand der Zentrale.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    Task QueryLocoSpeedDirectionAsync(int address, CancellationToken cancellationToken = default);

    // -------------------------------------------------------------------------
    // Zubehördecodersteuerung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sendet den protokollspezifischen Datenwert an einen Zubehördecoder.
    /// </summary>
    /// <param name="address">DCC-Adresse des Zubehördecoders (1–2048)</param>
    /// <param name="value">Protokollspezifischer Datenwert (z.B. bei DCC basic: Ausgangsauswahl 0/1)</param>
    /// <param name="protocol">DCC-Protokoll des Zubehördecoders (Standard oder Extended)</param>
    /// <param name="state">Schaltzustand (aktiv/inaktiv)</param>
    /// <param name="activationTimeMs">
    ///     Optionaler Zeitwert aus &lt;activationtime&gt; in ms.
    ///     0 bedeutet: keine zeitgesteuerte Aktivierung.
    /// </param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    Task SetAccessoryValueAsync(int address, byte value,
        OTD.HardwareControl.Accessory.DecoderProtocol protocol,
        OTD.HardwareControl.Accessory.FunctionState state,
        int activationTimeMs = 0,
        CancellationToken cancellationToken = default);
}

