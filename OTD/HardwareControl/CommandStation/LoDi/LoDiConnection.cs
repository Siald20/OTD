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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl.Drivers;

internal enum LoDiTransportMode
{
    Tcp,
    Udp
}

/// <summary>
///     Manages the TCP/UDP connection to a LoDi device and asynchronously receives
///     incoming packets in a background thread (API General, pp. 2-4).
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
    private byte _nextPacketNumber;

    // TCP-Fragmentierungspuffer für robuste Paket-Verarbeitung
    private readonly byte[] _tcpBuffer = new byte[4096];
    private int _tcpBufferIndex;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Triggered when the connection state changes.</summary>
    public event EventHandler<LoDiConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>Triggered when a complete, valid packet has been received.</summary>
    public event EventHandler<LoDiPacketReceivedEventArgs>? PacketReceived;

    // -------------------------------------------------------------------------
    // Eigenschaften
    // -------------------------------------------------------------------------

    /// <summary>Indicates whether an active TCP connection exists.</summary>
    public bool IsConnected => _transportMode == LoDiTransportMode.Tcp
        ? _tcpClient?.Connected ?? false
        : _udpClient != null;

    // -------------------------------------------------------------------------
    // Konstruktor / Verbindungsaufbau
    // -------------------------------------------------------------------------

    public LoDiConnection(LoDiTransportMode transportMode = LoDiTransportMode.Tcp)
    {
        _transportMode = transportMode;
    }

    /// <summary>
    ///     Establishes a TCP connection to the specified LoDi device and
    ///     starts the asynchronous reception loop (API General, p. 4).
    /// </summary>
    /// <param name="ipAddress">IP address of the device</param>
    /// <param name="port">TCP port of the device (default: 11092)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ConnectAsync(string ipAddress, int port = LoDiProtocol.DefaultTcpPort, 
        CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            await DisconnectAsync();


        if (_transportMode == LoDiTransportMode.Tcp)
        {
            await ConnectTcpAsync(ipAddress, port, cancellationToken);
            return;
        }

        await ConnectUdpAsync(ipAddress, port, cancellationToken);
    }

    /// <summary>
    ///     Disconnects the TCP connection and stops the reception loop.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_receiveCts != null)
        {
            await _receiveCts.CancelAsync();

            if (_receiveTask != null)
            {
                try { await _receiveTask; }
                catch (OperationCanceledException) { /* expected */ }
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
    ///     Sends a REQ packet to the connected device.
    /// </summary>
    /// <param name="command">Command code (e.g. 0x0F for GetVersion)</param>
    /// <param name="payload">Optional payload data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SendAsync(byte command, byte[]? payload = null, CancellationToken cancellationToken = default)
    {
        var packet = new LoDiPacket(LoDiProtocol.PacketTypeRequest, command, GetNextPacketNumber(), payload ?? []);
        await SendPacketAsync(packet, cancellationToken);
    }

    /// <summary>
    ///     Sends a complete LoDi packet to the connected device (TCP).
    /// </summary>
    public async Task SendPacketAsync(LoDiPacket packet, CancellationToken cancellationToken = default)
    {
        if (_transportMode == LoDiTransportMode.Tcp)
        {
            if (_stream == null || !IsConnected)
                throw new InvalidOperationException("No active TCP connection to the LoDi device.");

            var tcpBytes = packet.ToTcpBytes();
            await _stream.WriteAsync(tcpBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
            return;
        }

        if (_udpClient == null)
            throw new InvalidOperationException("No active UDP connection to the LoDi device.");

        var udpBytes = packet.ToUdpBytes();
        await _udpClient.SendAsync(udpBytes, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Empfangs-Loop (Hintergrund-Task)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Continuously reads incoming data from the TCP stream and
    ///     triggers the <see cref="PacketReceived"/> event for each complete packet.
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
                    // Connection was closed by the device
                    ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(false, 
                        "Connection closed by the counterpart."));
                    break;
                }

                // Copy received data to the fragmentation buffer
                ProcessReceivedTcpData(buffer, bytesRead);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        catch (Exception ex)
        {
            ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(false, 
                $"Connection error: {ex.Message}"));
        }
    }

    private async Task ReceiveUdpLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _udpClient != null)
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);

                ProcessReceivedUdpData(result.Buffer);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        catch (Exception ex)
        {
            ConnectionChanged?.Invoke(this, new LoDiConnectionChangedEventArgs(false,
                $"UDP connection error: {ex.Message}"));
        }
    }

    /// <summary>
    ///     Processes received TCP data and extracts valid LoDi packets from it.
    ///     Handles fragmentation when packets are spread over multiple TCP segments.
    /// </summary>
    private void ProcessReceivedTcpData(byte[] buffer, int length)
    {
        // Append data to fragmentation buffer
        if (_tcpBufferIndex + length > _tcpBuffer.Length)
        {
            // Buffer overflow: likely garbage data, reset
            _tcpBufferIndex = 0;
        }

        Array.Copy(buffer, 0, _tcpBuffer, _tcpBufferIndex, length);
        _tcpBufferIndex += length;

        // Try to extract packets from the buffer
        int offset = 0;
        while (offset < _tcpBufferIndex)
        {
            // At least 2 bytes needed for the length prefix
            if (_tcpBufferIndex - offset < 2)
                break;

            // Read length prefix (big-endian)
            var packetLength = ((_tcpBuffer[offset] & 0xFF) << 8) | (_tcpBuffer[offset + 1] & 0xFF);
            var totalLength = 2 + packetLength;

            // Check if the complete packet is available
            if (_tcpBufferIndex - offset < totalLength)
                break;

            // Extract packet array
            var packetData = new byte[totalLength];
            Array.Copy(_tcpBuffer, offset, packetData, 0, totalLength);

            ProcessReceivedTcpPacket(packetData);

            offset += totalLength;
        }

        // Move remaining data to the front
        if (offset > 0)
        {
            if (offset < _tcpBufferIndex)
                Array.Copy(_tcpBuffer, offset, _tcpBuffer, 0, _tcpBufferIndex - offset);
            _tcpBufferIndex -= offset;
        }
    }

    private void ProcessReceivedUdpData(byte[] datagram)
    {
        try
        {
            if (LoDiPacket.TryParseUdp(datagram, out var packet) && packet != null)
                RaisePacketReceivedSafely(packet, "UDP");
        }
        catch (Exception ex)
        {
            LogPacketProcessingError("UDP", ex);
        }
    }

    private void ProcessReceivedTcpPacket(byte[] packetData)
    {
        try
        {
            if (LoDiPacket.TryParseTcp(packetData, out var packet, out _) && packet != null)
                RaisePacketReceivedSafely(packet, "TCP");
        }
        catch (Exception ex)
        {
            LogPacketProcessingError("TCP", ex);
        }
    }

    private void RaisePacketReceivedSafely(LoDiPacket packet, string transportName)
    {
        try
        {
            PacketReceived?.Invoke(this, new LoDiPacketReceivedEventArgs(packet));
        }
        catch (Exception ex)
        {
            LogPacketProcessingError(transportName, ex);
        }
    }

    private static void LogPacketProcessingError(string transportName, Exception ex)
        => Console.WriteLine($"[LoDiConnection] Error in {transportName} packet processing: {ex.Message}");

    // -------------------------------------------------------------------------
    // UDP-Discovery (statisch)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sends a UDP broadcast to discover all LoDi devices in the local network
    ///     (API General, p. 4: "To scan for devices, it is sufficient to send a REQ packet
    ///     with command 0x0F (query FW version) to the broadcast address").
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of all found LoDi devices</returns>
    public static async Task<System.Collections.Generic.List<LoDiDeviceInfo>> DiscoverDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var devices = new System.Collections.Generic.List<LoDiDeviceInfo>();

        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;

        // Discovery request: REQ packet with GetVersion (0x0F)
        var discoveryPacket = new LoDiPacket(LoDiProtocol.PacketTypeRequest, 
            LoDiProtocol.Commands.General.GetVersion, 0x00);
        var requestBytes = discoveryPacket.ToUdpBytes();

        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, LoDiProtocol.DiscoveryUdpPort);

        try
        {
            // Send broadcast
            await udpClient.SendAsync(requestBytes, broadcastEndpoint, cancellationToken);

            // Wait for responses (until timeout)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(LoDiProtocol.DiscoveryTimeoutMs);

            try
            {
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    var result = await udpClient.ReceiveAsync(timeoutCts.Token);
                    
                    if (LoDiPacket.TryParseUdp(result.Buffer, out var packet) && packet != null)
                    {
                        // Response to GetVersion (packet type ACK=0x21, command 0x0F)
                        if (packet.Command == LoDiProtocol.Commands.General.GetVersion && 
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
                // Timeout elapsed – normal
            }
        }
        catch
        {
            // Ignore discovery errors
        }

        return devices;
    }

    /// <summary>
    ///     Evaluates a UDP discovery response (payload: [device type, major, minor, patch]).
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

        // Signal to stop the receive loop
        _receiveCts?.Cancel();

        // Wait for the receive loop to complete, so no new events are triggered
        // while resources are being released.
        // This prevents race conditions.
        try
        {
            if (_receiveTask != null && !_receiveTask.IsCompleted)
            {
                // Wait with timeout to avoid hanging
                _receiveTask.Wait(TimeSpan.FromSeconds(5));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected – the task was canceled
        }
        catch
        {
            // Ignore errors while waiting
        }

        // Now it's safe that no new events are triggered
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
                e.Packet.Command == LoDiProtocol.Commands.General.GetVersion)
            {
                tcs.TrySetResult(true);
            }
        }

        PacketReceived += Handler;

        try
        {
            await SendAsync(LoDiProtocol.Commands.General.GetVersion, cancellationToken: cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(LoDiProtocol.NetworkTimeoutMs * 3, 600));

            await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("LoDi device did not respond to UDP handshake.");
        }
        finally
        {
            PacketReceived -= Handler;
        }
    }
}


