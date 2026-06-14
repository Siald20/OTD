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
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OTD.HardwareControl.Drivers;

/// <summary>
/// LoDi S88 implementation of <see cref="IFeedback"/>.
/// Queries device info (Bus1/Bus2 configurations) automatically and maps to flat sensor numbers.
/// S88 Shiftregister: Bus1 sensors 1..Bus1SensorCount, Bus2 sensors (Bus1SensorCount+1)..(Bus1SensorCount+Bus2SensorCount).
/// </summary>
internal sealed class LoDiFeedback : IFeedback, IDisposable
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

    private readonly Guid _moduleUid;
    private readonly string _ipAddress;
    private readonly int _port;
    private readonly bool _queryOnStartup;
    private readonly bool _subscribeOnStartup;
    private readonly LoDiS88Commander _commander;
    private readonly Dictionary<int, RailSensorState> _sensorStates = [];
    private TaskCompletionSource<bool>? _pendingModulesQuery;
    private readonly HashSet<int> _pendingModulesSeen = [];
    private int _pendingModulesExpectedCount;
    private readonly Lock _syncRoot = new();
    private readonly bool _diagnosticLogging;
    private bool _disposed;

    // Device Info - wird bei Connect abgerufen
    private int _bus1SensorCount;
    private int _bus2SensorCount;

    /// <summary>
    /// Creates an LoDi S88 feedback provider from configuration loaded via UID.
    /// Device info (Bus1/Bus2 lengths) is queried on ConnectAsync.
    /// </summary>
    public LoDiFeedback(
        XElement feedbackModuleElement,
        bool diagnosticLogging = false,
        bool suppressHeartbeatDiagnostics = false)
    {
        _diagnosticLogging = diagnosticLogging;
        
        // Parse module UID
        _moduleUid = CommandStationUtils.RequireGuidAttribute(feedbackModuleElement, "<feedbackmodule>");
        
        // Parse connection element
        var connectionElement = feedbackModuleElement.Element("connection")
            ?? throw new InvalidOperationException($"Feedback module '{_moduleUid}' is missing required <connection> element.");
        
        _ipAddress = CommandStationUtils.RequireAttribute(connectionElement, "ip", $"feedbackmodule '{_moduleUid}' / connection");
        _port = CommandStationUtils.ParseIntAttribute(connectionElement, "port", LoDiProtocol.DefaultTcpPort, 1, 65535);
        
        // Parse startup element
        var startupElement = feedbackModuleElement.Element("startup");
        _queryOnStartup = CommandStationUtils.ParseBoolAttribute(startupElement, "queryOnStartup", defaultValue: true);
        _subscribeOnStartup = CommandStationUtils.ParseBoolAttribute(startupElement, "subscribeOnStartup", defaultValue: true);
        
        _commander = new LoDiS88Commander();
        if (_diagnosticLogging)
            _commander.DiagnosticLogging = true;
        _commander.SuppressHeartbeatDiagnostics = suppressHeartbeatDiagnostics;

        _commander.ContactStateChanged += OnContactStateChanged;
        _commander.ModuleStateReceived += OnModuleStateReceived;
    }

    // -------------------------------------------------------------------------
    // IFeedback
    // -------------------------------------------------------------------------

    public Guid UniqueId => _moduleUid;

    public bool IsConnected => _commander.IsConnected;

    /// <summary>Total sensors = Bus1 + Bus2 (queried from device on connect).</summary>
    public int SensorCount => _bus1SensorCount + _bus2SensorCount;

    public event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_diagnosticLogging)
            Console.WriteLine($"[LoDiFeedback Diag] Verbinde Modúl {UniqueId}...");

        await _commander.ConnectAsync(_ipAddress, _port, _subscribeOnStartup, cancellationToken)
            .ConfigureAwait(false);

        if (_diagnosticLogging)
            Console.WriteLine($"[LoDiFeedback Diag] Modúl {UniqueId} verbunden: {IsConnected}");

        // Abfrage Device-Info vor allen anderen Operationen
        var deviceInfo = await _commander.QueryDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
        _bus1SensorCount = deviceInfo.Bus1SensorCount;
        _bus2SensorCount = deviceInfo.Bus2SensorCount;

        // Initialisiere Sensor-States
        lock (_syncRoot)
        {
            _sensorStates.Clear();
            for (int i = 1; i <= SensorCount; i++)
                _sensorStates[i] = RailSensorState.Inactive;
        }

        if (_diagnosticLogging)
            Console.WriteLine($"[LoDiFeedback Diag] Device-Info abgerufen: Bus1={_bus1SensorCount} Sensoren, Bus2={_bus2SensorCount} Sensoren, Summe={SensorCount}");

        // Initialzustand via globale Modulabfrage (0x20/0x30)
        if (_queryOnStartup)
        {
            await QueryModulesAndAwaitStateAsync(cancellationToken).ConfigureAwait(false);
        }


        if (_diagnosticLogging)
            Console.WriteLine($"[LoDiFeedback Diag] Modúl {UniqueId} vollständig initialisiert");
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Globale Event-Rückmeldungen beenden (optional, Verbindung wird danach getrennt).
        try { await _commander.UnsubscribeEventsAsync(cancellationToken).ConfigureAwait(false); }
        catch { /* Disconnect must continue. */ }

        await _commander.DisconnectAsync().ConfigureAwait(false);
    }

    public RailSensorState GetSensorState(int sensorNumber)
    {
        if (sensorNumber < 1 || sensorNumber > SensorCount)
            throw new ArgumentOutOfRangeException(nameof(sensorNumber), $"Sensor number must be between 1 and {SensorCount}.");

        lock (_syncRoot)
            return _sensorStates.GetValueOrDefault(sensorNumber, RailSensorState.Inactive);
    }

    public async Task<IReadOnlyDictionary<int, RailSensorState>> QueryAllSensorsAsync(
        CancellationToken cancellationToken = default)
    {
        // Globale Modulabfrage (0x20/0x30) und Initialisierung aus ACK 0x21/0x30
        await QueryModulesAndAwaitStateAsync(cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
            return new Dictionary<int, RailSensorState>(_sensorStates);
    }

    // -------------------------------------------------------------------------
    // Internes Bus/Sensor-Mapping
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps module address + contact number to flat IFeedback sensor number.
    /// Module 1 => 1..16, Module 2 => 17..32, ...
    /// </summary>
    private int ModuleContactToSensorNumber(int moduleAddress, int contactNumber)
    {
        if (moduleAddress < 1 || contactNumber < 1 || contactNumber > 16)
            return -1;

        var sensorNumber = ((moduleAddress - 1) * 16) + contactNumber;
        return sensorNumber <= SensorCount ? sensorNumber : -1;
    }

    // -------------------------------------------------------------------------
    // S88-Event-Handler
    // -------------------------------------------------------------------------

    private void OnContactStateChanged(object? sender, S88StateChangedEventArgs e)
    {
        var sensorNumber = ModuleContactToSensorNumber(e.ModuleAddress, e.ContactNumber);
        if (sensorNumber < 1) return;

        var state = e.IsOccupied ? RailSensorState.Active : RailSensorState.Inactive;

        lock (_syncRoot)
            _sensorStates[sensorNumber] = state;

        if (_diagnosticLogging)
            Console.WriteLine($"[LoDiFeedback Diag] Sensor {sensorNumber:D4} (Modul {e.ModuleAddress:D3}, Kontakt {e.ContactNumber:D2}) => {state}");

        SensorStateChanged?.Invoke(this, new SensorStateChangedEventArgs(UniqueId, sensorNumber, state));
    }

    private void OnModuleStateReceived(object? sender, S88ModuleStateEventArgs e)
    {
        var changed = new List<(int SensorNumber, RailSensorState State)>();

        lock (_syncRoot)
        {
            if (_pendingModulesQuery is not null && e.SnapshotCount > 0)
                _pendingModulesExpectedCount = e.SnapshotCount;
            
            // Ein S88-Modulstatus enthält immer 16 Kontakte (StatusHigh/StatusLow).
            for (var contact = 1; contact <= 16; contact++)
            {
                var sensorNumber = ModuleContactToSensorNumber(e.ModuleAddress, contact);
                if (sensorNumber < 1) continue;

                var newState = e.GetContactState(contact) ? RailSensorState.Active : RailSensorState.Inactive;

                if (_sensorStates.TryGetValue(sensorNumber, out var current) && current == newState)
                    continue;

                _sensorStates[sensorNumber] = newState;
                changed.Add((sensorNumber, newState));
            }

            if (_pendingModulesQuery is not null)
            {
                _pendingModulesSeen.Add(e.ModuleAddress);
                if (_pendingModulesExpectedCount > 0 && _pendingModulesSeen.Count >= _pendingModulesExpectedCount)
                    _pendingModulesQuery.TrySetResult(true);
            }
        }

        foreach (var (sensorNumber, state) in changed)
        {
            if (_diagnosticLogging)
                Console.WriteLine($"[LoDiFeedback Diag] Sensor {sensorNumber:D4} (Modul {e.ModuleAddress:D3}) => {state}");

            SensorStateChanged?.Invoke(this, new SensorStateChangedEventArgs(UniqueId, sensorNumber, state));
        }
    }

    // -------------------------------------------------------------------------
    // Hilfsmethoden
    // -------------------------------------------------------------------------

    /// <summary>Global module query (0x20/0x30) and wait for initial module states.</summary>
    private async Task QueryModulesAndAwaitStateAsync(CancellationToken cancellationToken)
    {
        if (SensorCount <= 0)
            return;
        
        TaskCompletionSource<bool> waitHandle;
        lock (_syncRoot)
        {
            waitHandle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingModulesQuery = waitHandle;
            _pendingModulesSeen.Clear();
            _pendingModulesExpectedCount = 0;
        }

        try
        {
            await _commander.QueryModulesAsync(cancellationToken).ConfigureAwait(false);
 
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(QueryTimeout);
            await waitHandle.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout: cached state used.
        }
        finally
        {
            lock (_syncRoot)
            {
                _pendingModulesQuery = null;
                _pendingModulesSeen.Clear();
                _pendingModulesExpectedCount = 0;
            }
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _commander.ContactStateChanged -= OnContactStateChanged;
        _commander.ModuleStateReceived -= OnModuleStateReceived;
        _commander.Dispose();

        lock (_syncRoot)
        {
            _pendingModulesQuery = null;
            _pendingModulesSeen.Clear();
            _pendingModulesExpectedCount = 0;
            _sensorStates.Clear();
        }
    }
}

