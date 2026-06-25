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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl.Drivers;

/// <summary>
///     Interface for the LoDi-S88-Commander feedback receiver.
///     Reads the status of S88 feedback sections via an Ethernet connection
///     and reports status changes asynchronously via events.
/// </summary>
/// <remarks>
///     Based on the LoDi device API documentation:
///     https://lokstoredigital.jimdoweb.com/service/geräte-api/lodi-s88-commander/
/// </remarks>
internal sealed class LoDiS88Commander : IDisposable
{
    private const byte InvalidModulePlaceholder = 0x7F;

    // -------------------------------------------------------------------------
    // Felder
    // -------------------------------------------------------------------------

    private readonly LoDiConnection _connection;
    private readonly SemaphoreSlim _deviceInfoRequestLock = new(1, 1);
    private bool _disposed;
    private TaskCompletionSource<S88DeviceInfo>? _pendingDeviceInfoRequest;

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
    // Eigenschaften
    // -------------------------------------------------------------------------

    /// <summary>Indicates whether an active connection to the S88 commander exists.</summary>
    public bool IsConnected => _connection.IsConnected;

    /// <summary>Enables diagnostic logging for packet events and S88 commands.</summary>
    public bool DiagnosticLogging { get; set; }

    /// <summary>
    ///     Suppresses diagnostic log entries for S88 heartbeat packets
    ///     (EVT, Cmd 0x31, Payload [0x00]).
    /// </summary>
    public bool SuppressHeartbeatDiagnostics { get; set; }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _deviceInfoRequestLock.Dispose();

        // WICHTIG: Zuerst die Verbindung zum Commander trennen und den Receive-Loop stoppen,
        // BEVOR der Event-Handler entfernt wird. Dies verhindert Race Conditions,
        // bei denen Pakete empfangen werden, nachdem der Handler entfernt wurde.
        _connection.Dispose();

        // Jetzt, da die Receive-Loop garantiert gestoppt ist, können beim Entfernen
        // des Handlers keine neuen Events mehr auslösen.
        _connection.PacketReceived -= OnPacketReceived;
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Triggered when the connection state changes.</summary>
    public event EventHandler<LoDiConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>
    ///     Triggered when the state of an individual sensor on an S88 module changes
    ///     (asynchronous push notification from the commander).
    /// </summary>
    public event EventHandler<S88StateChangedEventArgs>? ContactStateChanged;

    /// <summary>
    ///     Triggered when the complete state of an S88 module
    ///     is received in response to a query or as a push notification from the commander.
    /// </summary>
    public event EventHandler<S88ModuleStateEventArgs>? ModuleStateReceived;

    // -------------------------------------------------------------------------
    // Verbindung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Establishes a connection to the LoDi S88 commander.
    /// </summary>
    /// <param name="ipAddress">IP address of the commander</param>
    /// <param name="port">TCP port (default: <see cref="LoDiProtocol.DefaultTcpPort" />)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task ConnectAsync(string ipAddress, int port = LoDiProtocol.DefaultTcpPort,
        CancellationToken cancellationToken = default)
    {
        return ConnectAsync(ipAddress, port, true, cancellationToken);
    }

    /// <summary>
    ///     Establishes a connection to the LoDi S88 commander and optionally activates global S88 updates immediately.
    /// </summary>
    /// <param name="ipAddress">IP address of the commander</param>
    /// <param name="port">TCP port (default: <see cref="LoDiProtocol.DefaultTcpPort" />)</param>
    /// <param name="enableFeedbackUpdatesOnConnect">
    ///     <c>true</c> activates global S88 updates immediately after connect, <c>false</c> leaves the status unchanged.
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ConnectAsync(string ipAddress, int port, bool enableFeedbackUpdatesOnConnect,
        CancellationToken cancellationToken = default)
    {
        LogDiagnostic($"Connecting to {ipAddress}:{port}");

        await _connection.ConnectAsync(ipAddress, port, cancellationToken);

        if (enableFeedbackUpdatesOnConnect)
            await SetFeedbackUpdatesActiveAsync(true, cancellationToken);

        LogDiagnostic($"Connection established: {IsConnected}");
    }

    /// <summary>
    ///     Disconnects from the LoDi S88 commander.
    /// </summary>
    public async Task DisconnectAsync()
    {
        await _connection.DisconnectAsync();
    }

    /// <summary>
    ///     Activates or deactivates global S88 feedback updates on the commander.
    ///     Sends a REQ packet with Cmd 0x01 and Payload 0x01/0x00.
    /// </summary>
    /// <param name="isActive"><c>true</c> activates feedback updates, <c>false</c> deactivates them.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SetFeedbackUpdatesActiveAsync(bool isActive, CancellationToken cancellationToken = default)
    {
        var payload = isActive ? (byte)0x01 : (byte)0x00;
        LogDiagnostic(
            $"Sending activation packet: Type=0x{LoDiProtocol.PacketTypeRequest:X2} (REQ) " +
            $"Cmd=0x{LoDiProtocol.Commands.S88.SetFeedbackUpdatesActive:X2} " +
            $"Payload=[0x{payload:X2}] (Active={(isActive ? 1 : 0)})");

        await _connection.SendAsync(LoDiProtocol.Commands.S88.SetFeedbackUpdatesActive, [payload], cancellationToken);
    }

    /// <summary>
    ///     Activates global S88 event feedback for the current connection to the commander.
    /// </summary>
    public Task SubscribeEventsAsync(CancellationToken cancellationToken = default)
    {
        return SetFeedbackUpdatesActiveAsync(true, cancellationToken);
    }

    /// <summary>
    ///     Deactivates global S88 event feedback for the current connection to the commander.
    /// </summary>
    public Task UnsubscribeEventsAsync(CancellationToken cancellationToken = default)
    {
        return SetFeedbackUpdatesActiveAsync(false, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // S88-Abfragen
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Queries the device information of the LoDi S88 commander (module configuration per bus, firmware version, etc.).
    ///     Sends command 0x35 and waits for the response.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Device info with module and sensor count for Bus 1 and Bus 2.</returns>
    public async Task<S88DeviceInfo> QueryDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        LogDiagnostic($"Sending device info query (Cmd 0x{LoDiProtocol.Commands.S88.DeviceConfigGet:X2})");

        await _deviceInfoRequestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pendingDeviceInfoRequest =
                new TaskCompletionSource<S88DeviceInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                await _connection.SendAsync(LoDiProtocol.Commands.S88.DeviceConfigGet, [], cancellationToken);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

                return await _pendingDeviceInfoRequest.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _pendingDeviceInfoRequest = null;
            }
        }
        finally
        {
            _deviceInfoRequestLock.Release();
        }
    }

    /// <summary>
    ///     Queries the current status of all S88 modules (global query over both buses).
    ///     Sends S88MelderGet (0x30) without payload.
    ///     The commander responds with packet type 0x21 (ACK), command 0x30.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task QueryModulesAsync(CancellationToken cancellationToken = default)
    {
        LogDiagnostic($"Sending S88MelderGet query (Cmd 0x{LoDiProtocol.Commands.S88.QueryModules:X2}): all modules");
        await _connection.SendAsync(LoDiProtocol.Commands.S88.QueryModules, [], cancellationToken);
    }

    /// <summary>
    ///     Subscribes to push notifications for state changes of an S88 module.
    ///     After subscribing, the device automatically reports changes via the
    ///     <see cref="ContactStateChanged" /> event.
    /// </summary>
    /// <param name="moduleAddress">Address of the S88 module (1-based)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Obsolete("LoDi does not support module-specific subscription via command. " +
              "Use SubscribeEventsAsync().", false)]
    public async Task SubscribeModuleAsync(int moduleAddress, CancellationToken cancellationToken = default)
    {
        LogDiagnostic($"SubscribeModuleAsync({moduleAddress:D3}) is obsolete; activating global events.");
        await SubscribeEventsAsync(cancellationToken);
    }

    /// <summary>
    ///     Unsubscribes from push notifications of an S88 module.
    /// </summary>
    /// <param name="moduleAddress">Address of the S88 module (1-based)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Obsolete("LoDi does not support module-specific unsubscription via command. " +
              "Use UnsubscribeEventsAsync().", false)]
    public async Task UnsubscribeModuleAsync(int moduleAddress, CancellationToken cancellationToken = default)
    {
        LogDiagnostic($"UnsubscribeModuleAsync({moduleAddress:D3}) is obsolete; deactivating global events.");
        await UnsubscribeEventsAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Paketverarbeitung (eingehende S88-Meldungen)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Processes received packets and triggers the corresponding S88 events.
    /// </summary>
    private void OnPacketReceived(object? sender, LoDiPacketReceivedEventArgs e)
    {
        try
        {
            if (_disposed)
                return;

            var packet = e.Packet;

            if (SuppressHeartbeatDiagnostics && IsHeartbeatPacket(packet))
                return;

            LogDiagnostic(
                $"Packet received: Cmd=0x{packet.Command:X2} ({LoDiProtocol.GetCommandName(packet.Command)}) " +
                $"Type=0x{packet.PacketType:X2} ({LoDiProtocol.GetPacketTypeName(packet.PacketType)}) PayloadLen={packet.Payload.Length}");

            var isS88Command = IsS88Command(packet.Command);

            if (!isS88Command)
            {
                LogDiagnostic(
                    $"  -> Ignored (Cmd 0x{packet.Command:X2} is not S88-relevant), " +
                    $"Payload=[{ToHex(packet.Payload)}]");
                return;
            }

            switch (packet.Command)
            {
                case LoDiProtocol.Commands.S88.SetFeedbackUpdatesActive:
                    LogDiagnostic($"  -> ACK/NACK to S88 activation, Payload=[{ToHex(packet.Payload)}]");
                    return;
                case LoDiProtocol.Commands.S88.DeviceConfigGet:
                    LogDiagnostic($"  -> ACK to device info query, Payload=[{ToHex(packet.Payload)}]");
                    HandleDeviceInfoPacket(packet);
                    return;
                default:
                    switch (packet.PacketType)
                    {
                        case LoDiProtocol.PacketTypeAck or LoDiProtocol.PacketTypeNack:
                            LogDiagnostic(
                                $"  -> {LoDiProtocol.GetPacketTypeName(packet.PacketType)}: Module status or subscribe confirmation");
                            HandleS88StatePacket(packet);
                            return;
                        case LoDiProtocol.PacketTypeEvent:
                            LogDiagnostic("  -> EVT: State changes");
                            HandleS88StateChangedPacket(packet);
                            return;
                        default:
                            LogDiagnostic(
                                $"  -> Unexpected S88 packet type {LoDiProtocol.GetPacketTypeName(packet.PacketType)}, " +
                                $"Payload=[{ToHex(packet.Payload)}]");
                            break;
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"ERROR processing packet: {ex.Message}");
        }
    }

    /// <summary>
    ///     Evaluates an S88 module status packet and triggers <see cref="ModuleStateReceived" /> event.
    /// </summary>
    /// <remarks>
    ///     S88MelderGet response (Cmd 0x30): [Count][ModuleAddress][StatusHigh][StatusLow]...
    /// </remarks>
    private void HandleS88StatePacket(LoDiPacket packet)
    {
        if (packet.Command != LoDiProtocol.Commands.S88.QueryModules)
            return;

        if (packet.Payload.Length < 1)
        {
            LogDiagnostic($"    Query response invalid: Payload empty, Raw=[{ToHex(packet.Payload)}]");
            return;
        }

        var moduleCount = packet.Payload[0];
        var expectedLength = 1 + moduleCount * 3;
        if (packet.Payload.Length < expectedLength)
        {
            LogDiagnostic(
                $"    Query response incomplete: Count={moduleCount}, PayloadLen={packet.Payload.Length}, expected>={expectedLength}, Raw=[{ToHex(packet.Payload)}]");
            return;
        }

        LogS88StatePacketRawDump(packet.Payload, moduleCount);
        LogDiagnostic($"    Query response: Count={moduleCount}");

        var offset = 1;
        for (var i = 0; i < moduleCount; i++)
        {
            var moduleAddress = packet.Payload[offset];
            var statusHigh = packet.Payload[offset + 1];
            var statusLow = packet.Payload[offset + 2];
            var stateBitmask = (ushort)((statusHigh << 8) | statusLow);
            offset += 3;

            var bitmaskBinary = Convert.ToString(stateBitmask, 2).PadLeft(16, '0');
            LogDiagnostic(
                $"    Module {moduleAddress:D3}: StatusHigh=0x{statusHigh:X2}, StatusLow=0x{statusLow:X2}, Bitmask=0x{stateBitmask:X4} [{bitmaskBinary}]");

            ModuleStateReceived?.Invoke(this,
                new S88ModuleStateEventArgs(moduleAddress, statusHigh, statusLow, i + 1, moduleCount));
        }
    }

    private void LogS88StatePacketRawDump(byte[] payload, int moduleCount)
    {
        if (!DiagnosticLogging)
            return;

        LogDiagnostic($"    Query raw payload: [{ToHex(payload)}]");

        var tupleCount = Math.Max(0, (payload.Length - 1) / 3);
        if (tupleCount != moduleCount)
            LogDiagnostic(
                $"    Raw tuple count mismatch: CountField={moduleCount}, ParsedTuples={tupleCount}, PayloadLen={payload.Length}");

        var placeholderCount = 0;
        var validCount = 0;
        for (var i = 0; i < tupleCount; i++)
        {
            var offset = 1 + i * 3;
            var moduleAddress = payload[offset];
            var statusHigh = payload[offset + 1];
            var statusLow = payload[offset + 2];
            var marker = moduleAddress == InvalidModulePlaceholder ? "placeholder" : "module";

            if (moduleAddress == InvalidModulePlaceholder)
                placeholderCount++;
            else
                validCount++;

            LogDiagnostic(
                $"      Raw[{i + 1:D2}] @+{offset:D2}: Addr=0x{moduleAddress:X2} ({moduleAddress:D3}), High=0x{statusHigh:X2}, Low=0x{statusLow:X2} ({marker})");
        }

        LogDiagnostic(
            $"    Raw summary: Valid={validCount}, Placeholder(0x{InvalidModulePlaceholder:X2})={placeholderCount}, CountField={moduleCount}");
    }

    /// <summary>
    ///     Evaluates an S88 state change packet and triggers <see cref="ContactStateChanged" /> event.
    /// </summary>
    /// <remarks>
    ///     Format (according to specification): [Count][ModuleAddress][ContactNumber][State]... repeated
    /// </remarks>
    private void HandleS88StateChangedPacket(LoDiPacket packet)
    {
        if (!S88EventPayloadParser.TryParse(packet.Payload, out var parsed, out var error))
        {
            LogDiagnostic($"    Invalid EVT format: {error} Raw=[{ToHex(packet.Payload)}]");
            return;
        }

        if (parsed == null)
        {
            LogDiagnostic($"    Invalid EVT format: Parser result missing. Raw=[{ToHex(packet.Payload)}]");
            return;
        }

        if (parsed.IsHeartbeat)
        {
            LogDiagnostic("    Heartbeat detected: Count=0, no state changes.");
            return;
        }

        if (parsed.Format == S88EventPayloadFormat.ModuleOverview)
        {
            LogDiagnostic(
                $"    EVT details (module overview): Count={parsed.Count}, " +
                $"ModuleAddress1={FormatByte(parsed.ModuleAddress1)}, ModuleType={FormatByte(parsed.ModuleType)}, " +
                $"ModuleAddress2={FormatByte(parsed.ModuleAddress2)}, ModuleAddress3={FormatByte(parsed.ModuleAddress3)}, " +
                $"TrailingBytes={parsed.TrailingBytes}");
            return;
        }

        var moduleAddresses =
            string.Join(", ", parsed.ModuleAddresses.Select(moduleAddress => moduleAddress.ToString("D3")));
        var changeTypeCounts = parsed.Changes
            .GroupBy(change => change.ChangeType)
            .Select(group => $"{ToDisplayType(group.Key)}:{group.Count()}")
            .ToArray();
        var changeTypeSummary = changeTypeCounts.Length == 0 ? "-" : string.Join(", ", changeTypeCounts);

        LogDiagnostic(
            $"    EVT details: Count={parsed.Count}, Module=[{moduleAddresses}], Types=[{changeTypeSummary}], " +
            $"TrailingBytes={parsed.TrailingBytes}");

        for (var i = 0; i < parsed.Changes.Count; i++)
        {
            var change = parsed.Changes[i];
            LogDiagnostic(
                $"      Change {i + 1:D2}: Module {change.ModuleAddress:D3}, Contact {change.ContactNumber:D2}, " +
                $"Type={ToDisplayType(change.ChangeType)}, Raw=0x{change.RawState:X2}");

            ContactStateChanged?.Invoke(this,
                new S88StateChangedEventArgs(change.ModuleAddress, change.ContactNumber, change.IsOccupied));
        }
    }

    /// <summary>
    ///     Processes a device info response (Cmd 0x35).
    ///     Format: [Bus1ModuleCount][Bus2ModuleCount][further info...]
    ///     LoDi-specific: each S88 module has exactly 16 sensor inputs per bus.
    /// </summary>
    private void HandleDeviceInfoPacket(LoDiPacket packet)
    {
        if (packet.Payload.Length < 2)
        {
            LogDiagnostic($"    Device info: Payload too short, expected >=2 bytes, received {packet.Payload.Length}");
            _pendingDeviceInfoRequest?.TrySetException(new InvalidOperationException("Device info payload too short."));
            return;
        }

        var bus1ModuleCount = packet.Payload[0]; // Number of S88 modules on Bus 1
        var bus2ModuleCount = packet.Payload[1]; // Number of S88 modules on Bus 2

        var bus1SensorCount = bus1ModuleCount * 16; // exactly 16 sensor inputs per S88 module
        var bus2SensorCount = bus2ModuleCount * 16; // exactly 16 sensor inputs per S88 module

        LogDiagnostic(
            $"    Device info: Bus 1: {bus1ModuleCount} modules ({bus1SensorCount} sensors), " +
            $"Bus 2: {bus2ModuleCount} modules ({bus2SensorCount} sensors)");

        var deviceInfo = new S88DeviceInfo(bus1SensorCount, bus2SensorCount, packet.Payload.ToArray());
        _pendingDeviceInfoRequest?.TrySetResult(deviceInfo);
    }

    private static bool IsS88Command(byte command)
    {
        return command is LoDiProtocol.Commands.S88.SetFeedbackUpdatesActive or LoDiProtocol.Commands.S88.QueryModules
            or LoDiProtocol.Commands.S88.GetContactState or LoDiProtocol.Commands.S88.DeviceConfigGet;
    }

    private static string ToDisplayType(S88ChangeType changeType)
    {
        return changeType == S88ChangeType.Occupied ? "OCCUPIED" : "FREE";
    }

    private static string FormatByte(byte? value)
    {
        return value.HasValue ? $"{value.Value} (0x{value.Value:X2})" : "-";
    }

    // ToDo: Heartbeat-Pakete auswerten und Exception generieren, falls nach Timeout keine Pakete mehr ankommen -> Unterbruch Kommunikation
    private static bool IsHeartbeatPacket(LoDiPacket packet)
    {
        return packet is
        {
            PacketType: LoDiProtocol.PacketTypeEvent, Command: LoDiProtocol.Commands.S88.GetContactState,
            Payload: [0x00]
        };
    }


    private void LogDiagnostic(string message)
    {
        if (DiagnosticLogging)
            Console.WriteLine($"[S88 Diag] {DateTimeOffset.Now:HH:mm:ss.fff} {message}");
    }

    private static string ToHex(byte[] payload)
    {
        return payload.Length == 0 ? "" : BitConverter.ToString(payload);
    }
}