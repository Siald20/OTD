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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl.Train;
using AccessoryDecoderProtocol = OTD.HardwareControl.Accessory.DecoderProtocol;
using AccessoryFunctionState = OTD.HardwareControl.Accessory.FunctionState;

namespace OTD.HardwareControl.CommandStation.LoDi;

/// <summary>
///     Schnittstelle für den LoDi-Rektor DCC-Steuergerät.
///     Ermöglicht die Steuerung von Lokomotiven, Zubehördecodern
///     sowie CV-Programmierung über eine Ethernet-Verbindung.
/// </summary>
/// <remarks>
///     Basiert auf der LoDi Geräte-API Dokumentation:
///     https://lokstoredigital.jimdoweb.com/service/geräte-api/lodi-rektor/
/// </remarks>
public sealed class LoDiRektor : ICommandStation
{
    // -------------------------------------------------------------------------
    // Felder
    // -------------------------------------------------------------------------

    private readonly LoDiConnection _connection;
    private readonly Dictionary<int, byte> _locoProtocols = new();
    private const byte DefaultLocoProtocol = LoDiDecoderProtocol.Dcc126;
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Wird ausgelöst, wenn sich der Verbindungszustand ändert.</summary>
    public event EventHandler<LoDiConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>
    ///     Wird ausgelöst, wenn vom LoDi-Rektor ein Lokzustands-Update empfangen wird
    ///     (Fahrstufe/Fahrtrichtung oder Funktion).
    /// </summary>
    public event EventHandler<LocoStateChangedEventArgs>? LocoStateChanged;

    /// <summary>
    ///     Wird ausgelöst, wenn vom LoDi-Rektor ein Zubehörzustands-Update empfangen wird
    ///     (Adresse + value + On/Off).
    /// </summary>
    public event EventHandler<OTD.HardwareControl.Accessory.AccessoryStateChangedEventArgs>? AccessoryStateChanged;

    // -------------------------------------------------------------------------
    // Eigenschaften
    // -------------------------------------------------------------------------

    /// <summary>Gibt an, ob eine aktive Verbindung zum LoDi-Rektor besteht.</summary>
    public bool IsConnected => _connection.IsConnected;

    /// <summary>
    ///     Fuehrt direkt nach erfolgreichem Connect eine BoosterStatus-Abfrage aus,
    ///     um den Rueckkanal fruehzeitig zu initialisieren.
    /// </summary>
    public bool EnableConnectWarmup { get; set; } = true;

    /// <summary>
    ///     Protokolliert den Abschluss des Connect-Warmups mit aktuellem Power-Status.
    /// </summary>
    public bool LogConnectWarmup { get; set; } = false;

    // -------------------------------------------------------------------------
    // Konstruktor
    // -------------------------------------------------------------------------

    public LoDiRektor()
    {
        _connection = new LoDiConnection(LoDiTransportMode.Udp);
        _connection.ConnectionChanged += (_, e) => ConnectionChanged?.Invoke(this, e);
        _connection.PacketReceived += OnPacketReceived;
    }

    // -------------------------------------------------------------------------
    // Verbindung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Stellt eine Verbindung zum LoDi-Rektor her.
    /// </summary>
    /// <param name="ipAddress">IP-Adresse des LoDi-Rektors</param>
    /// <param name="port">TCP-Port (Standard: <see cref="LoDiProtocol.DefaultTcpPort"/>)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task ConnectAsync(string ipAddress, int port = LoDiProtocol.DefaultTcpPort,
        CancellationToken cancellationToken = default)
    {
        await _connection.ConnectAsync(ipAddress, port, cancellationToken);

        if (!EnableConnectWarmup)
            return;

        try
        {
            var currentPowerState = await GetPowerStateAsync(cancellationToken);

            if (LogConnectWarmup)
            {
                Console.WriteLine(
                    $"[LoDi INFO] {DateTimeOffset.Now:HH:mm:ss.fff} ConnectWarmup abgeschlossen, Gleisspannung={(currentPowerState ? "ein" : "aus")}."
                );
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warnung: LoDi-Connect-Warmup fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>
    ///     Trennt die Verbindung zum LoDi-Rektor.
    /// </summary>
    public async Task DisconnectAsync() => await _connection.DisconnectAsync();

    // -------------------------------------------------------------------------
    // Gleisversorgung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Schaltet die Gleisversorgung (Fahrstrom) ein oder aus.
    /// </summary>
    /// <param name="isOn"><c>true</c> = Fahrstrom ein; <c>false</c> = Fahrstrom aus</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task SetPowerAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(
            LoDiProtocol.Commands.BoosterOn,
            [0xFF, isOn ? (byte)0x01 : (byte)0x00, isOn ? (byte)0x01 : (byte)0x00],
            cancellationToken
        );
    }

    /// <summary>
    ///     Fragt den aktuellen Gleisspannungszustand beim LoDi-Rektor ab.
    /// </summary>
    public async Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(LoDiProtocol.Commands.BoosterStatus, [0xFF, 0x00], cancellationToken);

        var response = await WaitForPacketAsync(
            LoDiProtocol.Commands.BoosterStatus,
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

    public void InitializeDecoder(int address, OTD.HardwareControl.Train.DecoderProtocol protocol, int effectiveSpeedSteps)
    {
        if (address <= 0)
            return;

        _ = effectiveSpeedSteps;
        _locoProtocols[address] = MapLocoProtocol(protocol);
    }

    /// <summary>
    ///     Setzt Geschwindigkeit und Fahrtrichtung einer Lokomotive.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="speedStep">
    ///     Fahrstufe (0 = Halt, je nach initialisiertem Protokoll/Fahrstufenmodus).
    ///     Wert 0 bewirkt einen regulären Halt (kein Nothalt).
    /// </param>
    /// <param name="direction">Fahrtrichtung</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task SetLocoSpeedAsync(int address, int speedStep, VehicleDirection direction,
        CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(address);
        var locoProtocol = ResolveLocoProtocol(address);
        var mask = direction == VehicleDirection.Forward ? (byte)0x80 : (byte)0x00;
        var clampedSpeed = (byte)Math.Clamp(speedStep, 0, 126);

        var payload = new byte[]
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

        await _connection.SendAsync(LoDiProtocol.Commands.DecoderLocoSpeed, payload, cancellationToken);
    }

    /// <summary>
    ///     Schaltet eine Lokomotivfunktion ein oder aus (F0–F28 und höher).
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="functionNumber">Funktionsnummer (0 = F0/Licht, 1–28 = F1–F28)</param>
    /// <param name="isOn"><c>true</c> = Funktion ein; <c>false</c> = Funktion aus</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task SetLocoFunctionAsync(int address, int functionNumber, bool isOn,
        CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(address);
        var locoProtocol = ResolveLocoProtocol(address);
        var index = (byte)Math.Clamp(functionNumber, 0, 127);
        var state = isOn ? (byte)1 : (byte)0;

        var payload = new byte[]
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

        await _connection.SendAsync(LoDiProtocol.Commands.DecoderLocoFunction, payload, cancellationToken);
    }

    /// <summary>
    ///     Führt einen Nothalt für eine bestimmte Lokomotive aus.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task EmergencyStopAsync(int address, CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(address);
        var locoProtocol = ResolveLocoProtocol(address);
        var mask = (byte)(0x40 | 0x80); // Bit6 = Nothalt, Bit7 = Vorwärts

        var payload = new byte[]
        {
            locoProtocol,
            addrLow,
            addrHigh,
            mask,
            0x00
        };

        await _connection.SendAsync(LoDiProtocol.Commands.DecoderLocoSpeed, payload, cancellationToken);
    }

    /// <summary>
    ///     Führt einen Nothalt für alle Lokomotiven gleichzeitig aus.
    /// </summary>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task EmergencyStopAllAsync(CancellationToken cancellationToken = default)
    {
        await SetPowerAsync(false, cancellationToken);
    }

    /// <summary>
    ///     Fragt Funktionszustände eines Lokdecoders bei LoDi nicht-destruktiv ab.
    ///     Laut API kann DecoderLocoFunction (0xC2) als Leseabfrage ohne Data genutzt werden.
    ///     Für eine gezielte Abfrage wird hier pro Funktion C2 mit [Protocol, AddrL, AddrH, Index]
    ///     (ohne Data-Byte) gesendet.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="functionList">Liste der Funktionsnummern, deren Zustand abgefragt werden soll.</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
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

            var receivedPackets = new List<LoDiPacket>();
            var packetLock = new object();

            void Handler(object? _, LoDiPacketReceivedEventArgs e)
            {
                if (e.Packet.Command != LoDiProtocol.Commands.DecoderLocoFunction)
                    return;

                if (e.Packet.PacketType is not (LoDiProtocol.PacketTypeAck or LoDiProtocol.PacketTypeEvent))
                    return;

                if (!TryParseLocoFunctionPayload(e.Packet.Payload, out var responseAddress, out var functionNumber,
                        out var _functionState))
                    return;

                if (responseAddress != address)
                    return;

                if (requestedFunctions is not null && !requestedFunctions.Contains(functionNumber))
                    return;

                lock (packetLock)
                {
                    receivedPackets.Add(e.Packet);
                }

                if (pendingResponses is not null && pendingResponses.TryGetValue(functionNumber, out var pending))
                    pending.TrySetResult(true);
            }

            _connection.PacketReceived += Handler;
            try
            {
                if (queryFunctionNumbers is { Count: > 0 })
                {
                    foreach (var functionNumber in queryFunctionNumbers)
                    {
                        var functionIndex = (byte)Math.Clamp(functionNumber, 0, 127);
                        var queryPayload = new byte[]
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

                        await _connection.SendAsync(LoDiProtocol.Commands.DecoderLocoFunction, queryPayload,
                            cancellationToken);

                        // Kurzer Abstand, damit die Zentrale Antworten stabil liefern kann.
                        await Task.Delay(10, cancellationToken);
                    }
                }
                else
                {
                    // Fallback: globale Abfrage ohne Index.
                    var queryPayload = new byte[]
                    {
                        locoProtocol,
                        addrLow,
                        addrHigh
                    };

                    if (DiagnosticLogging)
                        Console.WriteLine(
                            $"[LoDi TX] {DateTimeOffset.Now:HH:mm:ss.fff} QueryDecoderState " +
                            $"Addr={address} Cmd=DecoderLocoFunction Payload=[Protocol,AddrL,AddrH]");

                    await _connection.SendAsync(LoDiProtocol.Commands.DecoderLocoFunction, queryPayload,
                        cancellationToken);
                }

                if (pendingResponses is { Count: > 0 })
                {
                    // Blockiert, bis pro angefragter Funktion mindestens eine ACK/EVT-Antwort eingetroffen ist.
                    using var receiveWindowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    receiveWindowCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(LoDiProtocol.NetworkTimeoutMs * 8, 1600)));

                    var waitTasks = pendingResponses.Values.Select(t => t.Task);
                    await Task.WhenAll(waitTasks).WaitAsync(receiveWindowCts.Token);
                }
                else
                {
                    // Bei globaler Abfrage ohne Index nur ein kurzes Sammelfenster verwenden.
                    using var receiveWindowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    receiveWindowCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(LoDiProtocol.NetworkTimeoutMs * 4, 800)));
                    await Task.Delay(200, receiveWindowCts.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Lokales Timeout ist erwartbar, falls nicht alle angefragten Funktionen antworten.
            }
            finally
            {
                _connection.PacketReceived -= Handler;
            }

            List<LoDiPacket> snapshot;
            lock (packetLock)
            {
                snapshot = new List<LoDiPacket>(receivedPackets);
            }

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
                    speedStep: null,
                    direction: VehicleDirection.Undefined,
                    functionNumber,
                    functionStateValue,
                    isEventPacket: packet.PacketType == LoDiProtocol.PacketTypeEvent));

                forwardedUpdates++;
            }

            if (DiagnosticLogging)
            {
                Console.WriteLine(
                    $"[LoDi INFO] {DateTimeOffset.Now:HH:mm:ss.fff} QueryDecoderState " +
                    $"Addr={address}: empfangen={snapshot.Count}, geparst={parsedPackets}, weitergeleitet={forwardedUpdates}.");

                if (perFunctionResponseCount is not null)
                {
                    foreach (var entry in perFunctionResponseCount.OrderBy(x => x.Key))
                    {
                        var status = entry.Value > 0 ? $"Antworten={entry.Value}" : "keine Antwort im Zeitfenster";
                        Console.WriteLine(
                            $"[LoDi INFO] {DateTimeOffset.Now:HH:mm:ss.fff} QueryDecoderState " +
                            $"Addr={address} Func={entry.Key}: {status}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler bei QueryDecoderStateAsync für Adresse {address}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Fragt die aktuelle Geschwindigkeit (Fahrstufe) und Fahrtrichtung einer Lokomotive
    ///     blockierend von LoDi ab.
    ///     Laut API kann DecoderLocoSpeed (0xC1) auch als Leseabfrage verwendet werden.
    ///     Es wird eine C1-Query mit [Protocol, AddrL, AddrH, Mask] (ohne Speed-Byte) gesendet.
    /// </summary>
    /// <param name="address">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task QueryLocoSpeedDirectionAsync(int address, CancellationToken cancellationToken = default)
    {
        try
        {
            var (addrLow, addrHigh) = EncodeDccAddress(address);
            var locoProtocol = ResolveLocoProtocol(address);

            var speedResponseReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var receivedPackets = new List<LoDiPacket>();
            var packetLock = new object();
            
            
            void Handler(object? _, LoDiPacketReceivedEventArgs e)
            {
                if (e.Packet.Command != LoDiProtocol.Commands.DecoderLocoSpeed)
                    return;

                if (e.Packet.PacketType is not (LoDiProtocol.PacketTypeAck or LoDiProtocol.PacketTypeEvent))
                    return;

                if (!TryParseLocoSpeedPayload(e.Packet.Payload, out var responseAddress, out var _speedStep,
                        out var _direction))
                    return;

                if (responseAddress != address)
                    return;

                lock (packetLock)
                {
                    receivedPackets.Add(e.Packet);
                }

                speedResponseReceived.TrySetResult(true);
            }
            
            _connection.PacketReceived += Handler;
            try
            {
                // Query: Sende C1 mit [Protocol, AddrL, AddrH] (ohne Mask und Speed-Byte)
                var queryPayload = new byte[]
                {
                    locoProtocol,
                    addrLow,
                    addrHigh
                };

                if (DiagnosticLogging)
                    Console.WriteLine(
                        $"[LoDi TX] {DateTimeOffset.Now:HH:mm:ss.fff} QueryLocoSpeed " +
                        $"Addr={address} Cmd=DecoderLocoSpeed Payload=[Protocol,AddrL,AddrH]");

                await _connection.SendAsync(LoDiProtocol.Commands.DecoderLocoSpeed, queryPayload, cancellationToken);

                // Blockiert, bis eine ACK/EVT-Antwort für die Geschwindigkeit eingetroffen ist
                using var receiveWindowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveWindowCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(LoDiProtocol.NetworkTimeoutMs * 8, 1600)));

                try
                {
                    await speedResponseReceived.Task.WaitAsync(receiveWindowCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Lokales Timeout ist erwartbar, falls keine Antwort kommt
                }

                List<LoDiPacket> snapshot;
                lock (packetLock)
                {
                    snapshot = new List<LoDiPacket>(receivedPackets);
                }

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
                        speedStep: speedStep,
                        direction: direction,
                        functionNumber: null,
                        functionStateValue: null,
                        isEventPacket: packet.PacketType == LoDiProtocol.PacketTypeEvent));

                    forwardedUpdates++;
                }

                if (DiagnosticLogging)
                {
                    Console.WriteLine(
                        $"[LoDi INFO] {DateTimeOffset.Now:HH:mm:ss.fff} QueryLocoSpeed " +
                        $"Addr={address}: empfangen={snapshot.Count}, geparst={parsedPackets}, weitergeleitet={forwardedUpdates}.");
                }
            }
            finally
            {
                _connection.PacketReceived -= Handler;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler bei QueryLocoSpeedAsync für Adresse {address}: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Zubehördecoder
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sendet den protokollspezifischen Datenwert an einen Zubehördecoder.
    /// </summary>
    /// <param name="address">DCC-Adresse des Zubehördecoders (1–2048)</param>
    /// <param name="value">Protokollspezifischer Datenwert (bei DCC basic: Ausgangsauswahl 0/1)</param>
    /// <param name="protocol">DCC-Protokoll des Zubehördecoders (Standard oder Extended)</param>
    /// <param name="state">Schaltzustand (aktiv/inaktiv)</param>
    /// <param name="activationTimeMs">
    ///     Optionaler Zeitwert aus &lt;activationtime&gt; in ms.
    ///     0 bedeutet: keine zeitgesteuerte Aktivierung.
    /// </param>
    /// <param name="cancellationToken">Abbruchtoken</param>
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

        var payload = new byte[]
        {
            DefaultLocoProtocol,
            addrLow,
            addrHigh,
            data
        };

        await _connection.SendAsync(LoDiProtocol.Commands.DecoderAccessoryState, payload, cancellationToken);
    }
    
    // -------------------------------------------------------------------------
    // CV-Programmierung
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Liest eine CV auf dem Programmiergleis (Service Mode).
    /// </summary>
    /// <param name="cvNumber">CV-Nummer (1–1024)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    /// <returns>Gelesener CV-Wert (0–255), oder -1 bei Fehler</returns>
    /// <remarks>
    ///     TODO: Antwortbehandlung implementieren (asynchrones Warten auf Antwortpaket).
    /// </remarks>
    public async Task<int> ReadCvServiceModeAsync(int cvNumber, CancellationToken cancellationToken = default)
    {
        var cvHigh = (byte)(cvNumber >> 8);
        var cvLow = (byte)(cvNumber & 0xFF);

        await _connection.SendAsync(LoDiProtocol.Commands.CvReadServiceMode, [cvHigh, cvLow], cancellationToken);

        // TODO: Auf Antwortpaket warten und CV-Wert zurückgeben
        return -1;
    }

    /// <summary>
    ///     Schreibt eine CV auf dem Programmiergleis (Service Mode).
    /// </summary>
    /// <param name="cvNumber">CV-Nummer (1–1024)</param>
    /// <param name="value">Zu schreibender Wert (0–255)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task WriteCvServiceModeAsync(int cvNumber, byte value,
        CancellationToken cancellationToken = default)
    {
        var cvHigh = (byte)(cvNumber >> 8);
        var cvLow = (byte)(cvNumber & 0xFF);

        await _connection.SendAsync(LoDiProtocol.Commands.CvWriteServiceMode, [cvHigh, cvLow, value], cancellationToken);
    }

    /// <summary>
    ///     Liest eine CV per POM (Programming on the Main) direkt auf dem Fahrbetriebsgleis.
    /// </summary>
    /// <param name="locoAddress">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="cvNumber">CV-Nummer (1–1024)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    /// <returns>Gelesener CV-Wert (0–255), oder -1 bei Fehler</returns>
    /// <remarks>
    ///     TODO: Antwortbehandlung implementieren.
    /// </remarks>
    public async Task<int> ReadCvPomAsync(int locoAddress, int cvNumber,
        CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(locoAddress);
        var cvHigh = (byte)(cvNumber >> 8);
        var cvLow = (byte)(cvNumber & 0xFF);

        await _connection.SendAsync(LoDiProtocol.Commands.CvReadPom, [addrHigh, addrLow, cvHigh, cvLow], cancellationToken);

        // TODO: Auf Antwortpaket warten und CV-Wert zurückgeben
        return -1;
    }

    /// <summary>
    ///     Schreibt eine CV per POM (Programming on the Main) direkt auf dem Fahrbetriebsgleis.
    /// </summary>
    /// <param name="locoAddress">DCC-Adresse der Lokomotive (1–9999)</param>
    /// <param name="cvNumber">CV-Nummer (1–1024)</param>
    /// <param name="value">Zu schreibender Wert (0–255)</param>
    /// <param name="cancellationToken">Abbruchtoken</param>
    public async Task WriteCvPomAsync(int locoAddress, int cvNumber, byte value,
        CancellationToken cancellationToken = default)
    {
        var (addrLow, addrHigh) = EncodeDccAddress(locoAddress);
        var cvHigh = (byte)(cvNumber >> 8);
        var cvLow = (byte)(cvNumber & 0xFF);

        await _connection.SendAsync(LoDiProtocol.Commands.CvWritePom, [addrHigh, addrLow, cvHigh, cvLow, value], cancellationToken);
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
                {
                    // LoDi-Einschränkung: Bei 0xC4 wird Bit7 intern zur Unterscheidung
                    // basic/extended verwendet. Daher ist die RCN-213-Timed-Pulse-Variante
                    // (Bit7 als Ausgangsauswahl) aktuell nicht vollständig umsetzbar.
                    throw new NotSupportedException(
                        "LoDi-Rektor unterstützt DCC-Extended Timed-Pulse (RCN-213 2.2) aktuell nicht vollständig. " +
                        "Bit7 des Datenbytes ist bei LoDi reserviert.");
                }

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

    private static bool IsBoosterChannelActive(byte status) => status != 0;

    private byte ResolveLocoProtocol(int address)
    {
        return _locoProtocols.TryGetValue(address, out var protocol)
            ? protocol
            : DefaultLocoProtocol;
    }

    private static byte MapLocoProtocol(DecoderProtocol protocol)
    {
        return protocol switch
        {
            DecoderProtocol.Dcc14 => LoDiDecoderProtocol.Dcc14,
            DecoderProtocol.Dcc28 => LoDiDecoderProtocol.Dcc28,
            DecoderProtocol.Dcc128 => LoDiDecoderProtocol.Dcc126,
            DecoderProtocol.Motorola => LoDiDecoderProtocol.Motorola14,
            DecoderProtocol.M3 => LoDiDecoderProtocol.M3,
            _ => LoDiDecoderProtocol.Dcc126
        };
    }

    // -------------------------------------------------------------------------
    // Diagnose-Logging (ReadBack-Analyse)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Aktiviert ausführliches Diagnose-Logging aller empfangenen LoDi-Pakete.
    /// Hilfreich zur Analyse, wann ACK- vs. EVT-ReadBacks eintreffen.
    /// </summary>
    public bool DiagnosticLogging { get; set; } = false;

    private static string PacketTypeName(byte packetType) => packetType switch
    {
        LoDiProtocol.PacketTypeAck   => "ACK",
        LoDiProtocol.PacketTypeNack  => "NACK",
        LoDiProtocol.PacketTypeEvent => "EVT",
        _                            => $"0x{packetType:X2}"
    };

    private static string CommandName(byte command) => command switch
    {
        LoDiProtocol.Commands.DecoderLocoSpeed    => "DecoderLocoSpeed",
        LoDiProtocol.Commands.DecoderLocoFunction => "DecoderLocoFunction",
        LoDiProtocol.Commands.DecoderAccessoryState => "DecoderAccessoryState",
        LoDiProtocol.Commands.BoosterOn           => "BoosterOn",
        LoDiProtocol.Commands.BoosterStatus       => "BoosterStatus",
        _                                         => $"0x{command:X2}"
    };

    private void LogDiagnostic(string direction, LoDiPacket packet, string? extra = null)
    {
        if (!DiagnosticLogging) return;
        var payload = BitConverter.ToString(packet.Payload ?? []);
        var extraStr = extra is not null ? $" | {extra}" : "";
        Console.WriteLine(
            $"[LoDi {direction}] {DateTimeOffset.Now:HH:mm:ss.fff} " +
            $"Seq=0x{packet.PacketNumber:X2} " +
            $"Type={PacketTypeName(packet.PacketType)} " +
            $"Cmd={CommandName(packet.Command)} " +
            $"Payload=[{payload}]{extraStr}");
    }

    private void OnPacketReceived(object? sender, LoDiPacketReceivedEventArgs e)
    {
        // Diagnose: alle empfangenen Pakete protokollieren (ACK, NACK, EVT)
        if (DiagnosticLogging)
        {
            string extra = "";
            if (e.Packet.PacketType == LoDiProtocol.PacketTypeEvent &&
                e.Packet.Command == LoDiProtocol.Commands.DecoderLocoSpeed &&
                TryParseLocoSpeedPayload(e.Packet.Payload, out var da, out var ds, out var dd))
                extra = $"Addr={da} SpeedStep={ds} Dir={dd}";
            else if (e.Packet.PacketType == LoDiProtocol.PacketTypeEvent &&
                     e.Packet.Command == LoDiProtocol.Commands.DecoderLocoFunction &&
                     TryParseLocoFunctionPayload(e.Packet.Payload, out var fa, out var fn, out var fs))
                extra = $"Addr={fa} Func={fn} State={fs}";
            else if (e.Packet.PacketType == LoDiProtocol.PacketTypeAck &&
                     e.Packet.Command == LoDiProtocol.Commands.DecoderLocoSpeed &&
                     TryParseLocoSpeedPayload(e.Packet.Payload, out var aa, out var asp, out var adir))
                extra = $"Addr={aa} SpeedStep={asp} Dir={adir}";
            else if (e.Packet.PacketType == LoDiProtocol.PacketTypeAck &&
                     e.Packet.Command == LoDiProtocol.Commands.DecoderLocoFunction &&
                     TryParseLocoFunctionPayload(e.Packet.Payload, out var afa, out var afn, out var afs))
                extra = $"Addr={afa} Func={afn} State={afs}";
            else if ((e.Packet.PacketType == LoDiProtocol.PacketTypeEvent || e.Packet.PacketType == LoDiProtocol.PacketTypeAck) &&
                     e.Packet.Command == LoDiProtocol.Commands.DecoderAccessoryState &&
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
            case LoDiProtocol.Commands.DecoderLocoSpeed:
            {
                if (TryParseLocoSpeedPayload(e.Packet.Payload, out var address, out var speedStep, out var direction))
                {
                    LocoStateChanged?.Invoke(this, new LocoStateChangedEventArgs(
                        address, speedStep, direction,
                        functionNumber: null, functionStateValue: null,
                        isEventPacket: true));
                }
                return;
            }
            case LoDiProtocol.Commands.DecoderLocoFunction:
            {
                if (TryParseLocoFunctionPayload(e.Packet.Payload, out var functionAddress, out var functionNumber,
                        out var functionStateValue))
                {
                    LocoStateChanged?.Invoke(this, new LocoStateChangedEventArgs(
                        functionAddress, speedStep: null, direction: VehicleDirection.Undefined,
                        functionNumber,
                        functionStateValue,
                        isEventPacket: true));
                }

                return;
            }
            case LoDiProtocol.Commands.DecoderAccessoryState:
            {
                if (TryParseAccessoryPayload(e.Packet.Payload, out var address, out var value, out var state))
                {
                    AccessoryStateChanged?.Invoke(this,
                        new Accessory.AccessoryStateChangedEventArgs(address, value, state));
                }

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
        out FunctionState functionStateValue)
    {
        address = 0;
        functionNumber = 0;
        functionStateValue = FunctionState.Off;

        if (payload.Length < 5)
            return false;

        address = payload[1] | (payload[2] << 8);
        functionNumber = payload[3];
        functionStateValue = payload[4] == 0 ? FunctionState.Off : FunctionState.On;
        return true;
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
            linkedCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(LoDiProtocol.NetworkTimeoutMs * 3, 600)));
            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Keine Antwort vom LoDi-Gerät erhalten.");
        }
        finally
        {
            _connection.PacketReceived -= Handler;
        }
    }
}
