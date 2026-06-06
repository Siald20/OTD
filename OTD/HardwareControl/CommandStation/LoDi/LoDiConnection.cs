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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl.CommandStation.LoDi;

internal enum LoDiTransportMode
{
    Tcp,
    Udp
}

/// <summary>
///     Verwaltet die TCP/UDP-Verbindung zu einem LoDi-Gerät und empfängt
///     eingehende Pakete asynchron in einem Hintergrund-Thread (API Allgemeines, S. 2-4).
/// </summary>
internal sealed class LoDiConnection : IDisposable
{
    // -------------------------------------------------------------------------
    // Felder
    // -------------------------------------------------------------------------

    private readonly LoDiTransportMode _transportMode;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private bool _disposed;
    private byte _nextPacketNumber = 0x00;

    // TCP-Fragmentierungspuffer für robuste Paket-Verarbeitung
    private readonly byte[] _tcpBuffer = new byte[4096];
    private int _tcpBufferIndex = 0;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Wird ausgelöst, wenn sich der Verbindungszustand ändert.</summary>
    public event EventHandler<LoDiConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>Wird ausgelöst, wenn ein vollständiges, gültiges Paket empfangen wurde.</summary>
    public event EventHandler<LoDiPacketReceivedEventArgs>? PacketReceived;

    // -------------------------------------------------------------------------
    // Eigenschaften
    // -------------------------------------------------------------------------

    /// <summary>Gibt an, ob eine aktive TCP-Verbindung besteht.</summary>
    public bool IsConnected => _transportMode == LoDiTransportMode.Tcp
        ? _tcpClient?.Connected ?? false
        : _udpClient != null;

    /// <summary>IP-Adresse des verbundenen Geräts</summary>
    public string? RemoteIpAddress { get; private set; }

    /// <summary>TCP-Port des verbundenen Geräts</summary>
    public int RemotePort { get; private set; }

    // -------------------------------------------------------------------------
    // Konstruktor / Verbindungsaufbau
    // -------------------------------------------------------------------------

    public LoDiConnection(LoDiTransportMode transportMode = LoDiTransportMode.Tcp)
    {
        _transportMode = transportMode;
    }

    /// <summary>
    ///     Stellt eine TCP-Verbindung zum angegebenen LoDi-Gerät her und
    ///     startet den asynchronen Empfangs-Loop (API Allgemeines, S. 4).
    /// </summary>
    /// <param name="ipAddress">IP-Adresse des Geräts</param>
    /// <param name="port">TCP-Port des Geräts (Standard: 11092)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task ConnectAsync(string ipAddress, int port = LoDiProtocol.DefaultTcpPort, 
        CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            await DisconnectAsync();

        RemoteIpAddress = ipAddress;
        RemotePort = port;

        if (_transportMode == LoDiTransportMode.Tcp)
        {
            await ConnectTcpAsync(ipAddress, port, cancellationToken);
            return;
        }

        await ConnectUdpAsync(ipAddress, port, cancellationToken);
    }

    /// <summary>
    ///     Trennt die TCP-Verbindung und stoppt den Empfangs-Loop.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_receiveCts != null)
        {
            await _receiveCts.CancelAsync();

            if (_receiveTask != null)
            {
                try { await _receiveTask; }
                catch (OperationCanceledException) { /* erwartet */ }
            }
        }

        _stream?.Close();
        _tcpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
        _stream = null;
        _tcpClient = null;
        _tcpBufferIndex = 0;

        ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(false));
    }

    // -------------------------------------------------------------------------
    // Senden
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sendet ein REQ-Paket an das verbundene Gerät.
    /// </summary>
    /// <param name="command">Befehlscode (z.B. 0x0F für GetVersion)</param>
    /// <param name="payload">Optionale Nutzdaten</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task SendAsync(byte command, byte[]? payload = null, CancellationToken cancellationToken = default)
    {
        var packet = new LoDiPacket(LoDiProtocol.PacketTypeRequest, command, GetNextPacketNumber(), payload ?? []);
        await SendPacketAsync(packet, cancellationToken);
    }

    /// <summary>
    ///     Sendet ein vollständiges LoDi-Paket an das verbundene Gerät (TCP).
    /// </summary>
    public async Task SendPacketAsync(LoDiPacket packet, CancellationToken cancellationToken = default)
    {
        if (_transportMode == LoDiTransportMode.Tcp)
        {
            if (_stream == null || !IsConnected)
                throw new InvalidOperationException("Keine aktive TCP-Verbindung zum LoDi-Gerät.");

            var tcpBytes = packet.ToTcpBytes();
            await _stream.WriteAsync(tcpBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
            return;
        }

        if (_udpClient == null)
            throw new InvalidOperationException("Keine aktive UDP-Verbindung zum LoDi-Gerät.");

        var udpBytes = packet.ToUdpBytes();
        await _udpClient.SendAsync(udpBytes, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Empfangs-Loop (Hintergrund-Task)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Liest kontinuierlich eingehende Daten vom TCP-Stream und
    ///     löst für jedes vollständige Paket das <see cref="PacketReceived"/>-Event aus.
    /// </summary>
    private Task RunReceiveLoopAsync(CancellationToken cancellationToken)
        => _transportMode == LoDiTransportMode.Tcp
            ? ReceiveTcpLoopAsync(cancellationToken)
            : ReceiveUdpLoopAsync(cancellationToken);

    private async Task ReceiveTcpLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream != null)
            {
                var bytesRead = await _stream.ReadAsync(buffer, cancellationToken);

                if (bytesRead == 0)
                {
                    // Verbindung wurde vom Gerät getrennt
                    ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(false, 
                        "Verbindung durch Gegenstelle getrennt."));
                    break;
                }

                // Empfangene Daten in den Fragmentierungspuffer kopieren
                ProcessReceivedTcpData(buffer, bytesRead);
            }
        }
        catch (OperationCanceledException)
        {
            // Normaler Abbruch
        }
        catch (Exception ex)
        {
            ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(false, 
                $"Verbindungsfehler: {ex.Message}"));
        }
    }

    private async Task ReceiveUdpLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _udpClient != null)
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);

                if (LoDiPacket.TryParseUdp(result.Buffer, out var packet) && packet != null)
                    PacketReceived?.Invoke(this, new LoDiPacketReceivedEventArgs(packet));
            }
        }
        catch (OperationCanceledException)
        {
            // Normaler Abbruch
        }
        catch (Exception ex)
        {
            ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(false,
                $"UDP-Verbindungsfehler: {ex.Message}"));
        }
    }

    /// <summary>
    ///     Verarbeitet empfangene TCP-Daten und extrahiert daraus gültige LoDi-Pakete.
    ///     Handhabt Fragmentierung, wenn Pakete über mehrere TCP-Segmente verteilt sind.
    /// </summary>
    private void ProcessReceivedTcpData(byte[] buffer, int length)
    {
        // Daten an Fragmentierungspuffer anhängen
        if (_tcpBufferIndex + length > _tcpBuffer.Length)
        {
            // Buffer-Overflow: vermutlich Datenmüll, zurücksetzen
            _tcpBufferIndex = 0;
        }

        Array.Copy(buffer, 0, _tcpBuffer, _tcpBufferIndex, length);
        _tcpBufferIndex += length;

        // Versuche, Pakete aus dem Puffer zu extrahieren
        int offset = 0;
        while (offset < _tcpBufferIndex)
        {
            // Mindestens 2 Bytes für das Längenpräfix nötig
            if (_tcpBufferIndex - offset < 2)
                break;

            // Längenpräfix auslesen (Big-Endian)
            var packetLength = ((_tcpBuffer[offset] & 0xFF) << 8) | (_tcpBuffer[offset + 1] & 0xFF);
            var totalLength = 2 + packetLength;

            // Prüfen, ob das komplette Paket vorhanden ist
            if (_tcpBufferIndex - offset < totalLength)
                break;

            // Paket-Array extrahieren
            var packetData = new byte[totalLength];
            Array.Copy(_tcpBuffer, offset, packetData, 0, totalLength);

            // Paket parsen und Event auslösen
            if (LoDiPacket.TryParseTcp(packetData, out var packet, out _) && packet != null)
            {
                PacketReceived?.Invoke(this, new LoDiPacketReceivedEventArgs(packet));
            }

            offset += totalLength;
        }

        // Verbleibende Daten nach vorne verschieben
        if (offset > 0)
        {
            if (offset < _tcpBufferIndex)
                Array.Copy(_tcpBuffer, offset, _tcpBuffer, 0, _tcpBufferIndex - offset);
            _tcpBufferIndex -= offset;
        }
    }

    // -------------------------------------------------------------------------
    // UDP-Discovery (statisch)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sendet einen UDP-Broadcast zur Erkennung aller LoDi-Geräte im lokalen Netzwerk
    ///     (API Allgemeines, S. 4: "Zum Scannen nach Geräten genügt es... ein REQ-Paket 
    ///     mit dem Kommando 0x0F (Abfrage FW-Version) an die Broadcast-Adresse").
    /// </summary>
    /// <param name="cancellationToken">Abbruchtoken</param>
    /// <returns>Liste aller gefundenen LoDi-Geräte</returns>
    public static async Task<System.Collections.Generic.List<LoDiDeviceInfo>> DiscoverDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var devices = new System.Collections.Generic.List<LoDiDeviceInfo>();

        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;

        // Discovery-Request: REQ-Paket mit GetVersion (0x0F)
        var discoveryPacket = new LoDiPacket(LoDiProtocol.PacketTypeRequest, 
            LoDiProtocol.Commands.GetVersion, 0x00);
        var requestBytes = discoveryPacket.ToUdpBytes();

        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, LoDiProtocol.DiscoveryUdpPort);

        try
        {
            // Broadcast senden
            await udpClient.SendAsync(requestBytes, broadcastEndpoint, cancellationToken);

            // Auf Antworten warten (bis Timeout)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(LoDiProtocol.DiscoveryTimeoutMs);

            try
            {
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    var result = await udpClient.ReceiveAsync(timeoutCts.Token);
                    
                    if (LoDiPacket.TryParseUdp(result.Buffer, out var packet) && packet != null)
                    {
                        // Antwort auf GetVersion (Pakettyp ACK=0x21, Kommando 0x0F)
                        if (packet.Command == LoDiProtocol.Commands.GetVersion && 
                            packet.PacketType == LoDiProtocol.PacketTypeAck &&
                            packet.Payload.Length >= 4)
                        {
                            var deviceInfo = ParseDiscoveryResponse(packet, result.RemoteEndPoint.Address.ToString());
                            if (deviceInfo != null)
                                devices.Add(deviceInfo);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout abgelaufen – normal
            }
        }
        catch
        {
            // Discovery-Fehler ignorieren
        }

        return devices;
    }

    /// <summary>
    ///     Wertet eine UDP-Discovery-Antwort aus (Payload: [Gerätetyp, Major, Minor, Patch]).
    /// </summary>
    private static LoDiDeviceInfo? ParseDiscoveryResponse(LoDiPacket packet, string ipAddress)
    {
        if (packet.Payload.Length < 4)
            return null;

        var deviceType = packet.Payload[0] switch
        {
            0x03 => "LoDi-Rektor",
            0x0A => "LoDi-S88-Commander LX",
            0x09 => "LoDi-Shift-Commander",
            _ => $"LoDi-Gerät (0x{packet.Payload[0]:X2})"
        };

        var firmwareVersion = $"v{packet.Payload[1]:D2}.{packet.Payload[2]:D2}.{packet.Payload[3]:D2}";

        return new LoDiDeviceInfo(
            ipAddress: ipAddress,
            tcpPort: LoDiProtocol.DefaultTcpPort,
            deviceType: deviceType,
            deviceName: deviceType,
            serialNumber: "N/A",
            firmwareVersion: firmwareVersion
        );
    }

    // -------------------------------------------------------------------------
    // Hilfsmethoden
    // -------------------------------------------------------------------------

    private byte GetNextPacketNumber() => _nextPacketNumber++;

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _receiveCts?.Cancel();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _udpClient?.Dispose();
        _receiveCts?.Dispose();
    }

    private async Task ConnectTcpAsync(string ipAddress, int port, CancellationToken cancellationToken)
    {
        _tcpClient = new TcpClient
        {
            SendTimeout = 0,
            ReceiveTimeout = 0
        };

        try
        {
            await _tcpClient.ConnectAsync(ipAddress, port, cancellationToken);
            _stream = _tcpClient.GetStream();
            _tcpBufferIndex = 0;

            _receiveCts = new CancellationTokenSource();
            _receiveTask = RunReceiveLoopAsync(_receiveCts.Token);

            ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(true));
        }
        catch (Exception ex)
        {
            ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(false, ex.Message));
            throw;
        }
    }

    private async Task ConnectUdpAsync(string ipAddress, int port, CancellationToken cancellationToken)
    {
        _udpClient = new UdpClient();
        _udpClient.Connect(ipAddress, port);

        _receiveCts = new CancellationTokenSource();
        _receiveTask = RunReceiveLoopAsync(_receiveCts.Token);

        try
        {
            await EnsureUdpHandshakeAsync(cancellationToken);
            ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(true));
        }
        catch (Exception ex)
        {
            ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(false, ex.Message));
            await DisconnectAsync();
            throw;
        }
    }

    private async Task EnsureUdpHandshakeAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? _, LoDiPacketReceivedEventArgs e)
        {
            if (e.Packet.PacketType == LoDiProtocol.PacketTypeAck &&
                e.Packet.Command == LoDiProtocol.Commands.GetVersion)
            {
                tcs.TrySetResult(true);
            }
        }

        PacketReceived += Handler;

        try
        {
            await SendAsync(LoDiProtocol.Commands.GetVersion, cancellationToken: cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(LoDiProtocol.NetworkTimeoutMs * 3, 600));

            await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("LoDi-Gerät antwortet nicht auf UDP-Handshake.");
        }
        finally
        {
            PacketReceived -= Handler;
        }
    }
}


