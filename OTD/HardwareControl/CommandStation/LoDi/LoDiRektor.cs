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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OTD.HardwareControl.Drivers;

/// <summary>
///     Interface for the LoDi rector DCC controller.
///     Enables the control of locomotives, accessory decoders
///     as well as CV programming via an Ethernet connection.
/// </summary>
/// <remarks>
///     Based on the LoDi device API documentation:
///     https://lokstoredigital.jimdoweb.com/service/geräte-api/lodi-rektor/
/// </remarks>
internal sealed class LoDiRektor : ICommandStation
{
    private const string DriverLoDiRector = "lodi-rector";
    private const byte DefaultLocoProtocol = LoDiDecoderProtocol.Dcc126;
    private readonly string? _configuredIpAddress;
    private readonly int _configuredPort;

    // -------------------------------------------------------------------------
    // Felder
    // -------------------------------------------------------------------------

    private readonly LoDiConnection _connection;
    private readonly Dictionary<int, byte> _locoProtocols = new();
    private readonly int _networkTimeoutMs;
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Konstruktor
    // -------------------------------------------------------------------------

    public LoDiRektor()
    {
        _connection = new LoDiConnection(LoDiTransportMode.Udp);
        _configuredPort = LoDiProtocol.DefaultTcpPort;
        _networkTimeoutMs = LoDiProtocol.NetworkTimeoutMs;
        _connection.ConnectionChanged += (_, e) => ConnectionChanged?.Invoke(this, e);
        _connection.PacketReceived += OnPacketReceived;
    }

    /// <summary>
    ///     Initializes the driver with the complete &lt;commandstation&gt; node from commandstations.xml.
    /// </summary>
    public LoDiRektor(XElement commandStationElement)
        : this()
    {
        ArgumentNullException.ThrowIfNull(commandStationElement);

        var uid = CommandStationUtils.RequireGuidAttribute(commandStationElement, "<commandstation>");
        var driverName = CommandStationUtils
            .RequireAttribute(commandStationElement, "driver", $"commandstation '{uid}'")
            .ToLowerInvariant();

        if (!string.Equals(driverName, DriverLoDiRector, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"commandstation '{uid}' has driver '{driverName}', expected '{DriverLoDiRector}'.");

        var connectionElement = commandStationElement.Element("connection")
                                ?? throw new InvalidOperationException(
                                    $"commandstation '{uid}' is missing required <connection> element.");

        _configuredIpAddress = CommandStationUtils
            .RequireAttribute(connectionElement, "ip", $"commandstation '{uid}' / connection");
        _configuredPort = CommandStationUtils.ParseIntAttribute(
            connectionElement, "port", LoDiProtocol.DefaultTcpPort, 1, 65535);
        _networkTimeoutMs = CommandStationUtils.ParseIntAttribute(
            connectionElement, "timeoutMs", LoDiProtocol.NetworkTimeoutMs, 1, 60_000);

        var diagnosticsElement = commandStationElement.Element("diagnostics");
        DiagnosticLogging = CommandStationUtils.ParseBoolAttribute(diagnosticsElement, "enabled", false);
        LogConnectWarmup = CommandStationUtils.ParseBoolAttribute(
            diagnosticsElement, "logConnectWarmup", LogConnectWarmup);
        EnableConnectWarmup = CommandStationUtils.ParseBoolAttribute(
            diagnosticsElement, "enableConnectWarmup", EnableConnectWarmup);
    }

    /// <summary>
    ///     Immediately after a successful connect, performs a BoosterStatus query to
    ///     initialize the feedback bus early.
    /// </summary>
    public bool EnableConnectWarmup { get; set; } = true;

    /// <summary>
    ///     Logs the completion of the connect warm-up with the current power status.
    /// </summary>
    public bool LogConnectWarmup { get; set; }

    // -------------------------------------------------------------------------
    // Diagnose-Logging (ReadBack-Analyse)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Enables verbose diagnostic logging of all received LoDi packets.
    ///     Useful for analyzing when ACK vs. EVT readbacks arrive.
    /// </summary>
    public bool DiagnosticLogging { get; set; }

    /// <summary>
    ///     Triggered when a locomotive state update is received from the LoDi rector
    ///     (speed step/direction or function).
    /// </summary>
    public event EventHandler<LocoStateChangedEventArgs>? LocoStateChanged;

    /// <summary>
    ///     Triggered when an accessory state update is received from the LoDi rector
    ///     (address + value + On/Off).
    /// </summary>
    public event EventHandler<AccessoryStateChangedEventArgs>? AccessoryStateChanged;

    // -------------------------------------------------------------------------
    // Eigenschaften
    // -------------------------------------------------------------------------

    /// <summary>Indicates whether an active connection to the LoDi rector exists.</summary>
    public bool IsConnected => _connection.IsConnected;

    // -------------------------------------------------------------------------
    // Verbindung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Establishes a connection to the LoDi rector using driver configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return ConnectAsync(null, null, cancellationToken);
    }

    /// <summary>
    ///     Disconnects from the LoDi rector.
    /// </summary>
    public async Task DisconnectAsync()
    {
        await _connection.DisconnectAsync();
    }

    // -------------------------------------------------------------------------
    // Gleisversorgung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Turns the track power (operating current) on or off.
    /// </summary>
    /// <param name="isOn"><c>true</c> = power on; <c>false</c> = power off</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SetPowerAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(
            LoDiProtocol.Commands.Booster.On,
            [0xFF, isOn ? (byte)0x01 : (byte)0x00, isOn ? (byte)0x01 : (byte)0x00],
            cancellationToken
        );
    }

    /// <summary>
    ///     Queries the current track voltage state from the LoDi rector.
    /// </summary>
    public async Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(LoDiProtocol.Commands.Booster.Status, [0xFF, 0x00], cancellationToken);

        var response = await WaitForPacketAsync(
            LoDiProtocol.Commands.Booster.Status,
            packet => packet.PacketType == LoDiProtocol.PacketTypeAck,
            cancellationToken);

        if (response == null || response.Payload.Length < 3)
            return false;

        var boosterCount = response.Payload[0];
        var offset = 1;

        for (var i = 0; i < boosterCount && offset + 2 < response.Payload.Length; i++)
        {
            // Byte 0: Booster-Adresse, Byte 1: Status A, Byte 2: Status B
            var statusA = response.Payload[offset + 1];
            var statusB = response.Payload[offset + 2];

            if (IsBoosterChannelActive(statusA) || IsBoosterChannelActive(statusB))
                return true;

            offset += 3;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Lokomotivsteuerung
    // -------------------------------------------------------------------------

    public void InitializeDecoder(int address, LocoDecoderProtocol protocol, int effectiveSpeedSteps)
    {
        if (address <= 0)
            return;

        _ = effectiveSpeedSteps;
        _locoProtocols[address] = MapLocoProtocol(protocol);
    }

    /// <summary>
    ///     Sets the speed and direction of a locomotive.
    /// </summary>
    /// <param name="address">DCC address of the locomotive (1–9999)</param>
    /// <param name="speedStep">
    ///     Speed step (0 = stop, depending on initialized protocol/step mode).
    ///     Value 0 causes a regular stop (no emergency stop).
    /// </param>
    /// <param name="direction">Direction of travel</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SetLocoSpeedAsync(int address, int speedStep, VehicleDirection direction,
        CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(address);
        var locoProtocol = ResolveLocoProtocol(address);
        var mask = direction == VehicleDirection.Forward ? (byte)0x80 : (byte)0x00;
        var clampedSpeed = (byte)Math.Clamp(speedStep, 0, 126);

        var payload = new[]
        {
            locoProtocol,
            addrLow,
            addrHigh,
            mask,
            clampedSpeed
        };

        if (DiagnosticLogging)
            Console.WriteLine(
                $"[LoDi TX] {DateTimeOffset.Now:HH:mm:ss.fff} Cmd=DecoderLocoSpeed " +
                $"Addr={address} SpeedStep={clampedSpeed} Dir={direction}");

        await _connection.SendAsync(LoDiProtocol.Commands.Decoder.LocoSpeed, payload, cancellationToken);
    }

    /// <summary>
    ///     Turns a locomotive function on or off (F0–F28 and higher).
    /// </summary>
    /// <param name="address">DCC address of the locomotive (1–9999)</param>
    /// <param name="functionNumber">Function number (0 = F0/light, 1–28 = F1–F28)</param>
    /// <param name="isOn"><c>true</c> = function on; <c>false</c> = function off</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SetLocoFunctionAsync(int address, int functionNumber, bool isOn,
        CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(address);
        var locoProtocol = ResolveLocoProtocol(address);
        var index = (byte)Math.Clamp(functionNumber, 0, 127);
        var state = isOn ? (byte)1 : (byte)0;

        var payload = new[]
        {
            locoProtocol,
            addrLow,
            addrHigh,
            index,
            state
        };

        if (DiagnosticLogging)
            Console.WriteLine(
                $"[LoDi TX] {DateTimeOffset.Now:HH:mm:ss.fff} Cmd=DecoderLocoFunction " +
                $"Addr={address} Func={functionNumber} State={isOn}");

        await _connection.SendAsync(LoDiProtocol.Commands.Decoder.LocoFunction, payload, cancellationToken);
    }

    /// <summary>
    ///     Executes an emergency stop for a specific locomotive.
    /// </summary>
    /// <param name="address">DCC address of the locomotive (1–9999)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task EmergencyStopAsync(int address, CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(address);
        var locoProtocol = ResolveLocoProtocol(address);
        var mask = (byte)(0x40 | 0x80); // Bit6 = emergency stop, Bit7 = forward

        var payload = new byte[]
        {
            locoProtocol,
            addrLow,
            addrHigh,
            mask,
            0x00
        };

        await _connection.SendAsync(LoDiProtocol.Commands.Decoder.LocoSpeed, payload, cancellationToken);
    }

    /// <summary>
    ///     Executes an emergency stop for all locomotives simultaneously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task EmergencyStopAllAsync(CancellationToken cancellationToken = default)
    {
        await SetPowerAsync(false, cancellationToken);
    }

    /// <summary>
    ///     Queries the function states of a locomotive decoder non-destructively from LoDi.
    ///     According to the API, DecoderLocoFunction (0xC2) can be used as a read query without data.
    ///     For a targeted query, C2 is sent here per function with [Protocol, AddrL, AddrH, Index]
    ///     (without data byte).
    /// </summary>
    /// <param name="address">DCC address of the locomotive (1–9999)</param>
    /// <param name="functionList">List of function numbers whose state is to be queried.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task QueryLocoFunctionsStateAsync(int address, IReadOnlyList<int> functionList,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (addrLow, addrHigh) = EncodeDccAddress(address);
            var locoProtocol = ResolveLocoProtocol(address);
            var requestedFunctions = functionList.Count > 0 ? new HashSet<int>(functionList) : null;

            var queryFunctionNumbers = requestedFunctions is { Count: > 0 }
                ? requestedFunctions
                : null;
            var perFunctionResponseCount = queryFunctionNumbers is { Count: > 0 }
                ? queryFunctionNumbers.ToDictionary(fn => fn, _ => 0)
                : null;
            var pendingResponses = queryFunctionNumbers is { Count: > 0 }
                ? queryFunctionNumbers.ToDictionary(
                    fn => fn,
                    _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
                : null;

            var snapshot = await CollectDecoderResponsesAsync(
                LoDiProtocol.Commands.Decoder.LocoFunction,
                packet =>
                {
                    if (!TryParseLocoFunctionPayload(packet.Payload, out var responseAddress, out var functionNumber,
                            out _))
                        return false;

                    if (responseAddress != address)
                        return false;

                    if (requestedFunctions is not null && !requestedFunctions.Contains(functionNumber))
                        return false;

                    if (pendingResponses is not null && pendingResponses.TryGetValue(functionNumber, out var pending))
                        pending.TrySetResult(true);

                    return true;
                },
                async ct =>
                {
                    if (queryFunctionNumbers is { Count: > 0 })
                    {
                        foreach (var functionNumber in queryFunctionNumbers)
                        {
                            var functionIndex = (byte)Math.Clamp(functionNumber, 0, 127);
                            var queryPayload = new[]
                            {
                                locoProtocol,
                                addrLow,
                                addrHigh,
                                functionIndex
                            };

                            if (DiagnosticLogging)
                                Console.WriteLine(
                                    $"[LoDi TX] {DateTimeOffset.Now:HH:mm:ss.fff} QueryDecoderState " +
                                    $"Addr={address} Cmd=DecoderLocoFunction Index={functionIndex} Payload=[Protocol,AddrL,AddrH,Index]");

                            await _connection.SendAsync(LoDiProtocol.Commands.Decoder.LocoFunction, queryPayload, ct);

                            // Kurzer Abstand, damit die Zentrale Antworten stabil liefern kann.
                            await Task.Delay(10, ct);
                        }
                    }
                    else
                    {
                        // Fallback: globale Abfrage ohne Index.
                        var queryPayload = new[]
                        {
                            locoProtocol,
                            addrLow,
                            addrHigh
                        };

                        if (DiagnosticLogging)
                            Console.WriteLine(
                                $"[LoDi TX] {DateTimeOffset.Now:HH:mm:ss.fff} QueryDecoderState " +
                                $"Addr={address} Cmd=DecoderLocoFunction Payload=[Protocol,AddrL,AddrH]");

                        await _connection.SendAsync(LoDiProtocol.Commands.Decoder.LocoFunction, queryPayload, ct);
                    }
                },
                async ct =>
                {
                    if (pendingResponses is { Count: > 0 })
                    {
                        // Blockiert, bis pro angefragter Funktion mindestens eine ACK/EVT-Antwort eingetroffen ist.
                        using var receiveWindowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        receiveWindowCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(_networkTimeoutMs * 8, 1600)));

                        var waitTasks = pendingResponses.Values.Select(t => t.Task);
                        await Task.WhenAll(waitTasks).WaitAsync(receiveWindowCts.Token);
                    }
                    else
                    {
                        // Bei globaler Abfrage ohne Index nur ein kurzes Sammelfenster verwenden.
                        using var receiveWindowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        receiveWindowCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(_networkTimeoutMs * 4, 800)));
                        await Task.Delay(200, receiveWindowCts.Token);
                    }
                },
                cancellationToken);

            var parsedPackets = 0;
            var forwardedUpdates = 0;

            foreach (var packet in snapshot)
            {
                if (!TryParseLocoFunctionPayload(packet.Payload, out var responseAddress, out var functionNumber,
                        out var functionStateValue))
                    continue;

                parsedPackets++;

                if (responseAddress != address)
                    continue;

                if (requestedFunctions is not null && !requestedFunctions.Contains(functionNumber))
                    continue;

                if (perFunctionResponseCount is not null && perFunctionResponseCount.ContainsKey(functionNumber))
                    perFunctionResponseCount[functionNumber]++;

                if (DiagnosticLogging)
                {
                    var packetType = packet.PacketType == LoDiProtocol.PacketTypeEvent ? "EVT" : "ACK";
                    Console.WriteLine(
                        $"[LoDi RX] {DateTimeOffset.Now:HH:mm:ss.fff} QueryDecoderState " +
                        $"Addr={responseAddress} Type={packetType} Func={functionNumber} State={functionStateValue}");
                }

                LocoStateChanged?.Invoke(this, new LocoStateChangedEventArgs(
                    responseAddress,
                    null,
                    VehicleDirection.Undefined,
                    functionNumber,
                    functionStateValue,
                    packet.PacketType == LoDiProtocol.PacketTypeEvent));

                forwardedUpdates++;
            }

            if (DiagnosticLogging)
            {
                Console.WriteLine(
                    $"[LoDi INFO] {DateTimeOffset.Now:HH:mm:ss.fff} QueryDecoderState " +
                    $"Addr={address}: received={snapshot.Count}, parsed={parsedPackets}, forwarded={forwardedUpdates}.");

                if (perFunctionResponseCount is not null)
                    foreach (var entry in perFunctionResponseCount.OrderBy(x => x.Key))
                    {
                        var status = entry.Value > 0 ? $"Responses={entry.Value}" : "no response in time window";
                        Console.WriteLine(
                            $"[LoDi INFO] {DateTimeOffset.Now:HH:mm:ss.fff} QueryDecoderState " +
                            $"Addr={address} Func={entry.Key}: {status}");
                    }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in QueryDecoderStateAsync for address {address}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Queries the current speed (speed step) and direction of a locomotive
    ///     blocking from LoDi.
    ///     According to the API, DecoderLocoSpeed (0xC1) can also be used as a read query.
    ///     A C1 query with [Protocol, AddrL, AddrH, Mask] (without speed byte) is sent.
    /// </summary>
    /// <param name="address">DCC address of the locomotive (1–9999)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task QueryLocoSpeedDirectionAsync(int address, CancellationToken cancellationToken = default)
    {
        try
        {
            var (addrLow, addrHigh) = EncodeDccAddress(address);
            var locoProtocol = ResolveLocoProtocol(address);

            var speedResponseReceived =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var snapshot = await CollectDecoderResponsesAsync(
                LoDiProtocol.Commands.Decoder.LocoSpeed,
                packet =>
                {
                    if (!TryParseLocoSpeedPayload(packet.Payload, out var responseAddress, out _, out _))
                        return false;

                    if (responseAddress != address)
                        return false;

                    speedResponseReceived.TrySetResult(true);
                    return true;
                },
                async ct =>
                {
                    // Query: Sende C1 mit [Protocol, AddrL, AddrH] (ohne Mask und Speed-Byte)
                    var queryPayload = new[]
                    {
                        locoProtocol,
                        addrLow,
                        addrHigh
                    };

                    if (DiagnosticLogging)
                        Console.WriteLine(
                            $"[LoDi TX] {DateTimeOffset.Now:HH:mm:ss.fff} QueryLocoSpeed " +
                            $"Addr={address} Cmd=DecoderLocoSpeed Payload=[Protocol,AddrL,AddrH]");

                    await _connection.SendAsync(LoDiProtocol.Commands.Decoder.LocoSpeed, queryPayload, ct);
                },
                async ct =>
                {
                    // Blockiert, bis eine ACK/EVT-Antwort für die Geschwindigkeit eingetroffen ist
                    using var receiveWindowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    receiveWindowCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(_networkTimeoutMs * 8, 1600)));
                    await speedResponseReceived.Task.WaitAsync(receiveWindowCts.Token);
                },
                cancellationToken);

            var parsedPackets = 0;
            var forwardedUpdates = 0;

            foreach (var packet in snapshot)
            {
                if (!TryParseLocoSpeedPayload(packet.Payload, out var responseAddress, out var speedStep,
                        out var direction))
                    continue;

                parsedPackets++;

                if (responseAddress != address)
                    continue;

                if (DiagnosticLogging)
                {
                    var packetType = packet.PacketType == LoDiProtocol.PacketTypeEvent ? "EVT" : "ACK";
                    Console.WriteLine(
                        $"[LoDi RX] {DateTimeOffset.Now:HH:mm:ss.fff} QueryLocoSpeed " +
                        $"Addr={responseAddress} Type={packetType} SpeedStep={speedStep} Direction={direction}");
                }

                LocoStateChanged?.Invoke(this, new LocoStateChangedEventArgs(
                    responseAddress,
                    speedStep,
                    direction,
                    null,
                    null,
                    packet.PacketType == LoDiProtocol.PacketTypeEvent));

                forwardedUpdates++;
            }

            if (DiagnosticLogging)
                Console.WriteLine(
                    $"[LoDi INFO] {DateTimeOffset.Now:HH:mm:ss.fff} QueryLocoSpeed " +
                    $"Addr={address}: received={snapshot.Count}, parsed={parsedPackets}, forwarded={forwardedUpdates}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in QueryLocoSpeedAsync for address {address}: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Zubehördecoder
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sends the protocol-specific data value to an accessory decoder.
    /// </summary>
    /// <param name="address">DCC address of the accessory decoder (1–2048)</param>
    /// <param name="value">Protocol-specific data value (for DCC basic: output selection 0/1)</param>
    /// <param name="protocol">DCC protocol of the accessory decoder (Standard or Extended)</param>
    /// <param name="state">Switching state (active/inactive)</param>
    /// <param name="activationTimeMs">
    ///     Optional time value from &lt;activationtime&gt; in ms.
    ///     0 means: no time-controlled activation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SetAccessoryValueAsync(int address, byte value,
        AccessoryDecoderProtocol protocol, AccessoryFunctionState state,
        int activationTimeMs = 0,
        CancellationToken cancellationToken = default)
    {
        if (address < 1 || address > 2048)
            throw new ArgumentOutOfRangeException(nameof(address), address,
                "Accessory decoder address must be in range 1..2048.");

        if (state is not (AccessoryFunctionState.On or AccessoryFunctionState.Off))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Accessory state must be On or Off.");

        var (addrLow, addrHigh) = EncodeAccessoryAddress(address);
        var data = BuildAccessoryData(protocol, value, state, activationTimeMs);

        var payload = new[]
        {
            DefaultLocoProtocol,
            addrLow,
            addrHigh,
            data
        };

        await _connection.SendAsync(LoDiProtocol.Commands.Decoder.AccessoryState, payload, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Triggered when the connection state changes.</summary>
    public event EventHandler<LoDiConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>
    ///     Establishes a connection to the LoDi rector.
    ///     Primarily used internally; external callers should use
    ///     <see cref="ConnectAsync(System.Threading.CancellationToken)" />.
    /// </summary>
    /// <param name="ipAddress">Optional IP address override; <c>null</c> uses commandstations.xml.</param>
    /// <param name="port">Optional TCP port override; <c>null</c> uses commandstations.xml.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ConnectAsync(string? ipAddress, int? port,
        CancellationToken cancellationToken = default)
    {
        var targetAddress = string.IsNullOrWhiteSpace(ipAddress) ? _configuredIpAddress : ipAddress;
        if (string.IsNullOrWhiteSpace(targetAddress))
            throw new InvalidOperationException(
                "No IP address configured for LoDiRektor. Set it in commandstations.xml.");

        var configuredPort = _configuredPort > 0 ? _configuredPort : LoDiProtocol.DefaultTcpPort;
        var targetPort = port.HasValue && port.Value > 0 ? port.Value : configuredPort;

        await _connection.ConnectAsync(targetAddress, targetPort, cancellationToken);

        if (!EnableConnectWarmup)
            return;

        try
        {
            var currentPowerState = await GetPowerStateAsync(cancellationToken);

            if (LogConnectWarmup)
                Console.WriteLine(
                    $"[LoDi INFO] {DateTimeOffset.Now:HH:mm:ss.fff} ConnectWarmup completed, track voltage={(currentPowerState ? "on" : "off")}."
                );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: LoDi connect warm-up failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // CV-Programmierung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Reads a CV on the programming track (Service Mode).
    /// </summary>
    /// <param name="cvNumber">CV number (1–1024)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Read CV value (0–255), or -1 on error</returns>
    /// <remarks>
    ///     TODO: Implement response handling (asynchronous waiting for response packet).
    /// </remarks>
    public async Task<int> ReadCvServiceModeAsync(int cvNumber, CancellationToken cancellationToken = default)
    {
        var cvHigh = (byte)(cvNumber >> 8);
        var cvLow = (byte)(cvNumber & 0xFF);

        await _connection.SendAsync(LoDiProtocol.Commands.Cv.ReadServiceMode, [cvHigh, cvLow], cancellationToken);

        // TODO: Auf Antwortpaket warten und CV-Wert zurückgeben
        return -1;
    }

    /// <summary>
    ///     Writes a CV on the programming track (Service Mode).
    /// </summary>
    /// <param name="cvNumber">CV number (1–1024)</param>
    /// <param name="value">Value to be written (0–255)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task WriteCvServiceModeAsync(int cvNumber, byte value,
        CancellationToken cancellationToken = default)
    {
        var cvHigh = (byte)(cvNumber >> 8);
        var cvLow = (byte)(cvNumber & 0xFF);

        await _connection.SendAsync(LoDiProtocol.Commands.Cv.WriteServiceMode, [cvHigh, cvLow, value],
            cancellationToken);
    }

    /// <summary>
    ///     Reads a CV via POM (Programming on the Main) directly on the operating track.
    /// </summary>
    /// <param name="locoAddress">DCC address of the locomotive (1–9999)</param>
    /// <param name="cvNumber">CV number (1–1024)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Read CV value (0–255), or -1 on error</returns>
    /// <remarks>
    ///     TODO: Implement response handling.
    /// </remarks>
    public async Task<int> ReadCvPomAsync(int locoAddress, int cvNumber,
        CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(locoAddress);
        var cvHigh = (byte)(cvNumber >> 8);
        var cvLow = (byte)(cvNumber & 0xFF);

        await _connection.SendAsync(LoDiProtocol.Commands.Cv.ReadPom, [addrHigh, addrLow, cvHigh, cvLow],
            cancellationToken);

        // TODO: Auf Antwortpaket warten und CV-Wert zurückgeben
        return -1;
    }

    /// <summary>
    ///     Writes a CV via POM (Programming on the Main) directly on the operating track.
    /// </summary>
    /// <param name="locoAddress">DCC address of the locomotive (1–9999)</param>
    /// <param name="cvNumber">CV number (1–1024)</param>
    /// <param name="value">Value to be written (0–255)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task WriteCvPomAsync(int locoAddress, int cvNumber, byte value,
        CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(locoAddress);
        var cvHigh = (byte)(cvNumber >> 8);
        var cvLow = (byte)(cvNumber & 0xFF);

        await _connection.SendAsync(LoDiProtocol.Commands.Cv.WritePom, [addrHigh, addrLow, cvHigh, cvLow, value],
            cancellationToken);
    }

    private static (byte Low, byte High) EncodeDccAddress(int address)
    {
        var clamped = Math.Clamp(address, 1, 9999);
        var low = (byte)(clamped & 0xFF);
        var high = (byte)((clamped >> 8) & 0xFF);
        return (low, high);
    }

    private static (byte Low, byte High) EncodeAccessoryAddress(int address)
    {
        var clamped = Math.Clamp(address, 0, 0xFFFF);
        return ((byte)(clamped & 0xFF), (byte)((clamped >> 8) & 0xFF));
    }

    private static byte BuildAccessoryData(AccessoryDecoderProtocol protocol, byte value, AccessoryFunctionState state,
        int activationTimeMs)
    {
        switch (protocol)
        {
            case AccessoryDecoderProtocol.Dcc:
                // RCN-213, Abschnitt 2.1: einfaches Zubehördecoder-Paketformat.
                // value (0/1) wählt den Ausgang des adressierten Ausgangspaars (Bit0).
                // Schaltzustand wird separat über Bit3 gesetzt (On=1, Off=0).
                if (value is not (0 or 1))
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "DCC basic accessory mode requires value 0 or 1.");

                byte data = 0x80;
                data |= value;
                if (state == AccessoryFunctionState.On)
                    data |= 0x08;
                return data;

            case AccessoryDecoderProtocol.DccExtended:
                // RCN-213, Abschnitt 2.2:
                // - Signal/Lampen: Datenbyte direkt als Aspektwert (kein Timeout)
                // - Magnetartikel mit Schaltzeit: Datenbyte kodiert Schaltzeit+Ausgang
                //   (vollständig nur bei Zentralen möglich, die Bit7 nicht als Protokollmarker missbrauchen).
                if (activationTimeMs > 0)
                    // LoDi-Einschränkung: Bei 0xC4 wird Bit7 intern zur Unterscheidung
                    // basic/extended verwendet. Daher ist die RCN-213-Timed-Pulse-Variante
                    // (Bit7 als Ausgangsauswahl) aktuell nicht vollständig umsetzbar.
                    throw new NotSupportedException(
                        "LoDi-Rektor unterstützt DCC-Extended Timed-Pulse (RCN-213 2.2) aktuell nicht vollständig. " +
                        "Bit7 des Datenbytes ist bei LoDi reserviert.");

                if (value > 126)
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "LoDi-Rektor unterstützt bei DCC-Extended derzeit nur Werte 0..126 (Bit7 reserviert). ");

                return state == AccessoryFunctionState.On ? value : (byte)0x00;

            default:
                throw new NotSupportedException(
                    $"Accessory decoder protocol '{protocol}' is not supported by LoDiRektor. " +
                    "Only Accessory DecoderProtocol.Dcc and DecoderProtocol.DccExtended are supported.");
        }
    }

    private static bool IsBoosterChannelActive(byte status)
    {
        return status != 0;
    }

    private byte ResolveLocoProtocol(int address)
    {
        return _locoProtocols.TryGetValue(address, out var protocol)
            ? protocol
            : DefaultLocoProtocol;
    }

    private static byte MapLocoProtocol(LocoDecoderProtocol protocol)
    {
        return protocol switch
        {
            // ToDo: Mapping auflösen: LoDi soll die DCC konformen Fahrstufen-Modi verwenden
            LocoDecoderProtocol.Dcc14 => LoDiDecoderProtocol.Dcc14,
            LocoDecoderProtocol.Dcc28 => LoDiDecoderProtocol.Dcc28,
            LocoDecoderProtocol.Dcc128 => LoDiDecoderProtocol.Dcc126,
            LocoDecoderProtocol.Motorola => LoDiDecoderProtocol.Motorola14,
            LocoDecoderProtocol.M3 => LoDiDecoderProtocol.M3,
            _ => LoDiDecoderProtocol.Dcc126
        };
    }

    private void LogDiagnostic(string direction, LoDiPacket packet, string? extra = null)
    {
        if (!DiagnosticLogging) return;
        var payload = BitConverter.ToString(packet.Payload);
        var extraStr = extra is not null ? $" | {extra}" : "";
        Console.WriteLine(
            $"[LoDi {direction}] {DateTimeOffset.Now:HH:mm:ss.fff} " +
            $"Seq=0x{packet.PacketNumber:X2} " +
            $"Type={LoDiProtocol.GetPacketTypeName(packet.PacketType)} " +
            $"Cmd={LoDiProtocol.GetCommandName(packet.Command)} " +
            $"Payload=[{payload}]{extraStr}");
    }

    private void OnPacketReceived(object? sender, LoDiPacketReceivedEventArgs e)
    {
        // Diagnose: alle empfangenen Pakete protokollieren (ACK, NACK, EVT)
        if (DiagnosticLogging)
        {
            var extra = "";
            if (e.Packet.PacketType == LoDiProtocol.PacketTypeEvent &&
                e.Packet.Command == LoDiProtocol.Commands.Decoder.LocoSpeed &&
                TryParseLocoSpeedPayload(e.Packet.Payload, out var da, out var ds, out var dd))
                extra = $"Addr={da} SpeedStep={ds} Dir={dd}";
            else if (e.Packet.PacketType == LoDiProtocol.PacketTypeEvent &&
                     e.Packet.Command == LoDiProtocol.Commands.Decoder.LocoFunction &&
                     TryParseLocoFunctionPayload(e.Packet.Payload, out var fa, out var fn, out var fs))
                extra = $"Addr={fa} Func={fn} State={fs}";
            else if (e.Packet.PacketType == LoDiProtocol.PacketTypeAck &&
                     e.Packet.Command == LoDiProtocol.Commands.Decoder.LocoSpeed &&
                     TryParseLocoSpeedPayload(e.Packet.Payload, out var aa, out var asp, out var adir))
                extra = $"Addr={aa} SpeedStep={asp} Dir={adir}";
            else if (e.Packet.PacketType == LoDiProtocol.PacketTypeAck &&
                     e.Packet.Command == LoDiProtocol.Commands.Decoder.LocoFunction &&
                     TryParseLocoFunctionPayload(e.Packet.Payload, out var afa, out var afn, out var afs))
                extra = $"Addr={afa} Func={afn} State={afs}";
            else if ((e.Packet.PacketType == LoDiProtocol.PacketTypeEvent ||
                      e.Packet.PacketType == LoDiProtocol.PacketTypeAck) &&
                     e.Packet.Command == LoDiProtocol.Commands.Decoder.AccessoryState &&
                     TryParseAccessoryPayload(e.Packet.Payload, out var aaAddr, out var aaValue, out var aaState))
                extra = $"Addr={aaAddr} Value={aaValue} State={aaState}";
            LogDiagnostic("RX", e.Packet, extra);
        }

        // Nur EVT-Pakete werden als ReadBack-Events weitergeleitet.
        // ACK-Pakete bestätigen lediglich eigene Befehle — der Zustand ist lokal bereits bekannt.
        if (e.Packet.PacketType != LoDiProtocol.PacketTypeEvent)
        {
            if (e.Packet.PacketType == LoDiProtocol.PacketTypeNack)
                Console.WriteLine($"LoDi NACK (Seq 0x{e.Packet.PacketNumber:X2}, Cmd 0x{e.Packet.Command:X2})");
            return;
        }

        switch (e.Packet.Command)
        {
            case LoDiProtocol.Commands.Decoder.LocoSpeed:
            {
                if (TryParseLocoSpeedPayload(e.Packet.Payload, out var address, out var speedStep, out var direction))
                    LocoStateChanged?.Invoke(this, new LocoStateChangedEventArgs(
                        address, speedStep, direction,
                        null, null,
                        true));
                return;
            }
            case LoDiProtocol.Commands.Decoder.LocoFunction:
            {
                if (TryParseLocoFunctionPayload(e.Packet.Payload, out var functionAddress, out var functionNumber,
                        out var functionStateValue))
                    LocoStateChanged?.Invoke(this, new LocoStateChangedEventArgs(
                        functionAddress, null, VehicleDirection.Undefined,
                        functionNumber,
                        functionStateValue,
                        true));

                return;
            }
            case LoDiProtocol.Commands.Decoder.AccessoryState:
            {
                if (TryParseAccessoryPayload(e.Packet.Payload, out var address, out var value, out var state))
                    AccessoryStateChanged?.Invoke(this,
                        new AccessoryStateChangedEventArgs(address, value, state));

                break;
            }
        }
    }

    private static bool TryParseAccessoryPayload(byte[] payload, out int address, out int value,
        out AccessoryFunctionState state)
    {
        address = 0;
        value = 0;
        state = AccessoryFunctionState.Undefined;

        if (payload.Length < 4)
            return false;

        address = payload[1] | (payload[2] << 8);
        var data = payload[3];

        if ((data & 0x80) != 0)
        {
            // DCC basic accessory format: Bit0 = value, Bit3 = state, Bit7 = marker.
            value = data & 0x01;
            state = (data & 0x08) != 0 ? AccessoryFunctionState.On : AccessoryFunctionState.Off;
            return true;
        }

        // DCC extended accessory format: data byte contains the raw value bit pattern.
        value = data;
        state = data == 0 ? AccessoryFunctionState.Off : AccessoryFunctionState.On;
        return true;
    }

    private static bool TryParseLocoSpeedPayload(byte[] payload, out int address, out int speedStep,
        out VehicleDirection direction)
    {
        address = 0;
        speedStep = 0;
        direction = VehicleDirection.Undefined;

        if (payload.Length < 5)
            return false;

        address = payload[1] | (payload[2] << 8);
        direction = (payload[3] & 0x80) != 0 ? VehicleDirection.Forward : VehicleDirection.Backward;
        speedStep = payload[4];
        return true;
    }

    private static bool TryParseLocoFunctionPayload(byte[] payload, out int address, out int functionNumber,
        out LocoDecoderFunctionState functionStateValue)
    {
        address = 0;
        functionNumber = 0;
        functionStateValue = LocoDecoderFunctionState.Off;

        if (payload.Length < 5)
            return false;

        address = payload[1] | (payload[2] << 8);
        functionNumber = payload[3];
        functionStateValue = payload[4] == 0 ? LocoDecoderFunctionState.Off : LocoDecoderFunctionState.On;
        return true;
    }

    private async Task<List<LoDiPacket>> CollectDecoderResponsesAsync(
        byte command,
        Func<LoDiPacket, bool> packetPredicate,
        Func<CancellationToken, Task> sendRequestAsync,
        Func<CancellationToken, Task> waitForResponsesAsync,
        CancellationToken cancellationToken)
    {
        var receivedPackets = new List<LoDiPacket>();
        var packetLock = new object();

        void Handler(object? sender, LoDiPacketReceivedEventArgs e)
        {
            if (e.Packet.Command != command)
                return;

            if (e.Packet.PacketType is not (LoDiProtocol.PacketTypeAck or LoDiProtocol.PacketTypeEvent))
                return;

            if (!packetPredicate(e.Packet))
                return;

            lock (packetLock)
            {
                receivedPackets.Add(e.Packet);
            }
        }

        _connection.PacketReceived += Handler;
        try
        {
            await sendRequestAsync(cancellationToken);
            await waitForResponsesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Lokales Timeout ist erwartbar; bereits empfangene Pakete werden trotzdem ausgewertet.
        }
        finally
        {
            _connection.PacketReceived -= Handler;
        }

        lock (packetLock)
        {
            return new List<LoDiPacket>(receivedPackets);
        }
    }

    private async Task<LoDiPacket?> WaitForPacketAsync(byte command, Func<LoDiPacket, bool>? filter,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<LoDiPacket?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? _, LoDiPacketReceivedEventArgs e)
        {
            if (e.Packet.Command != command)
                return;

            if (filter != null && !filter(e.Packet))
                return;

            tcs.TrySetResult(e.Packet);
        }

        _connection.PacketReceived += Handler;

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(_networkTimeoutMs * 3, 600)));
            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("No response received from the LoDi device.");
        }
        finally
        {
            _connection.PacketReceived -= Handler;
        }
    }
}