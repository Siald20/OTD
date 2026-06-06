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

namespace OTD.HardwareControl.CommandStation.LoDi;

/// <summary>
///     Schnittstelle für den LoDi-S88-Commander Rückmeldeempfänger.
///     Liest den Zustand von S88-Rückmeldeabschnitten über eine Ethernet-Verbindung
///     und meldet Zustandsänderungen asynchron per Event.
/// </summary>
/// <remarks>
///     Basiert auf der LoDi Geräte-API Dokumentation:
///     https://lokstoredigital.jimdoweb.com/service/geräte-api/lodi-s88-commander/
/// </remarks>
public sealed class LoDiS88Commander : IDisposable
{
    // -------------------------------------------------------------------------
    // Felder
    // -------------------------------------------------------------------------

    private readonly LoDiConnection _connection;
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Wird ausgelöst, wenn sich der Verbindungszustand ändert.</summary>
    public event EventHandler<LoDiConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>
    ///     Wird ausgelöst, wenn sich der Zustand eines einzelnen S88-Kontakts ändert
    ///     (asynchrone Push-Meldung vom Gerät).
    /// </summary>
    public event EventHandler<S88StateChangedEventArgs>? ContactStateChanged;

    /// <summary>
    ///     Wird ausgelöst, wenn der vollständige Zustand eines S88-Moduls
    ///     als Antwort auf eine Abfrage oder als Push-Meldung empfangen wird.
    /// </summary>
    public event EventHandler<S88ModuleStateEventArgs>? ModuleStateReceived;

    // -------------------------------------------------------------------------
    // Eigenschaften
    // -------------------------------------------------------------------------

    /// <summary>Gibt an, ob eine aktive Verbindung zum S88-Commander besteht.</summary>
    public bool IsConnected => _connection.IsConnected;

    // -------------------------------------------------------------------------
    // Konstruktor
    // -------------------------------------------------------------------------

    public LoDiS88Commander()
    {
        _connection = new LoDiConnection();
        _connection.ConnectionChanged += (_, e) => ConnectionChanged?.Invoke(this, e);

        // Eingehende Pakete auf S88-Antworten prüfen
        _connection.PacketReceived += OnPacketReceived;
    }

    // -------------------------------------------------------------------------
    // Verbindung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Stellt eine Verbindung zum LoDi-S88-Commander her.
    /// </summary>
    /// <param name="ipAddress">IP-Adresse des S88-Commanders</param>
    /// <param name="port">TCP-Port (Standard: <see cref="LoDiProtocol.DefaultTcpPort"/>)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task ConnectAsync(string ipAddress, int port = LoDiProtocol.DefaultTcpPort,
        CancellationToken cancellationToken = default)
    {
        await _connection.ConnectAsync(ipAddress, port, cancellationToken);
    }

    /// <summary>
    ///     Trennt die Verbindung zum LoDi-S88-Commander.
    /// </summary>
    public async Task DisconnectAsync() => await _connection.DisconnectAsync();

    // -------------------------------------------------------------------------
    // S88-Abfragen
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Fragt den aktuellen Zustand eines einzelnen S88-Moduls ab.
    ///     Das Ergebnis wird asynchron über das <see cref="ModuleStateReceived"/>-Event gemeldet.
    /// </summary>
    /// <param name="moduleAddress">Adresse des S88-Moduls (1-basiert)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task QueryModuleAsync(int moduleAddress, CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(LoDiProtocol.Commands.S88Query, [(byte)moduleAddress], cancellationToken);
    }

    /// <summary>
    ///     Abonniert Push-Meldungen für Zustandsänderungen eines S88-Moduls.
    ///     Nach dem Abonnieren meldet das Gerät Änderungen automatisch über den
    ///     <see cref="ContactStateChanged"/>-Event.
    /// </summary>
    /// <param name="moduleAddress">Adresse des S88-Moduls (1-basiert)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task SubscribeModuleAsync(int moduleAddress, CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(LoDiProtocol.Commands.S88Subscribe, [(byte)moduleAddress], cancellationToken);
    }

    /// <summary>
    ///     Beendet das Abonnement für Push-Meldungen eines S88-Moduls.
    /// </summary>
    /// <param name="moduleAddress">Adresse des S88-Moduls (1-basiert)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task UnsubscribeModuleAsync(int moduleAddress, CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(LoDiProtocol.Commands.S88Unsubscribe, [(byte)moduleAddress], cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Paketverarbeitung (eingehende S88-Meldungen)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Verarbeitet empfangene Pakete und löst die entsprechenden S88-Events aus.
    /// </summary>
    private void OnPacketReceived(object? sender, LoDiPacketReceivedEventArgs e)
    {
        var packet = e.Packet;

        if (packet.Command != LoDiProtocol.Responses.S88State)
            return;

        // ACK auf Abfrage: kompakter Modulstatus
        if (packet.PacketType == LoDiProtocol.PacketTypeAck)
        {
            HandleS88StatePacket(packet);
            return;
        }

        // EVT: Liste geänderter Kontakte
        if (packet.PacketType == LoDiProtocol.PacketTypeEvent)
            HandleS88StateChangedPacket(packet);
    }

    /// <summary>
    ///     Wertet ein S88-Modulstatus-Paket aus und löst <see cref="ModuleStateReceived"/> aus.
    /// </summary>
    /// <remarks>
    ///     TODO: Exaktes Datenformat aus LoDi S88-Commander API-Dokumentation implementieren.
    ///     Angenommenes Format: [ModuleAddress, StateByte_High, StateByte_Low]
    /// </remarks>
    private void HandleS88StatePacket(LoDiPacket packet)
    {
        // TODO: Datenformat verifizieren (Annahme: 3 Datenbytes)
        if (packet.Payload.Length < 3)
            return;

        var moduleAddress = packet.Payload[0];
        // Zustandsbitmask: Byte 1 = High-Byte, Byte 2 = Low-Byte (Kontakte 9–16 / 1–8)
        var stateBitmask = (ushort)((packet.Payload[1] << 8) | packet.Payload[2]);

        ModuleStateReceived?.Invoke(this, new S88ModuleStateEventArgs(moduleAddress, stateBitmask));
    }

    /// <summary>
    ///     Wertet ein S88-Zustandsänderungs-Paket aus und löst <see cref="ContactStateChanged"/> aus.
    /// </summary>
    /// <remarks>
    ///     TODO: Exaktes Datenformat aus LoDi S88-Commander API-Dokumentation implementieren.
    ///     Angenommenes Format: [ModuleAddress, ContactNumber, NewState]
    /// </remarks>
    private void HandleS88StateChangedPacket(LoDiPacket packet)
    {
        // TODO: Datenformat verifizieren (Annahme: 3 Datenbytes)
        if (packet.Payload.Length < 1)
            return;

        // EVT-Beispiel laut Doku: [Anzahl][Adr][Input][State]...
        var count = packet.Payload[0];
        var expectedLength = 1 + count * 3;
        if (packet.Payload.Length < expectedLength)
            return;

        for (var i = 0; i < count; i++)
        {
            var idx = 1 + i * 3;
            var moduleAddress = packet.Payload[idx];
            var contactNumber = packet.Payload[idx + 1];
            var isOccupied = packet.Payload[idx + 2] != 0;

            ContactStateChanged?.Invoke(this,
                new S88StateChangedEventArgs(moduleAddress, contactNumber, isOccupied));
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.PacketReceived -= OnPacketReceived;
        _connection.Dispose();
    }
}

