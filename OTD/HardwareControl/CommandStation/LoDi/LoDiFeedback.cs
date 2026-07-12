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
///     LoDi S88 implementation of <see cref="IFeedbackController" />.
///     Queries device info (Bus1/Bus2 configurations) automatically and maps to flat sensor numbers.
///     S88 Shiftregister: Bus1 sensors 1..Bus1SensorCount, Bus2 sensors
///     (Bus1SensorCount+1)..(Bus1SensorCount+Bus2SensorCount).
/// </summary>
internal sealed class LoDiFeedback : IFeedbackController, IDisposable
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);
    private const int InvalidModulePlaceholder = 0x7F;

    private readonly LoDiS88Commander _commander;
    private readonly bool _diagnosticLogging;
    private readonly string _ipAddress;

    private readonly HashSet<int> _pendingModulesSeen = [];
    private readonly int _port;
    private readonly bool _queryOnStartup;
    private readonly Dictionary<int, InputState> _inputStates = [];
    private readonly bool _subscribeOnStartup;
    private readonly Lock _syncRoot = new();

    // Device Info - wird bei Connect abgerufen
    private int _bus1SensorCount;
    private int _bus2SensorCount;
    private int _configuredModuleCount;
    private bool _disposed;
    private readonly HashSet<int> _pendingInvalidModulesSeen = [];
    private readonly Dictionary<int, (byte StatusHigh, byte StatusLow)> _pendingModuleSnapshots = [];
    private int _pendingModulesExpectedCount;
    private TaskCompletionSource<bool>? _pendingModulesQuery;

    /// <summary>
    ///     Creates an LoDi S88 feedback provider from configuration loaded via UID.
    ///     Device info (Bus1/Bus2 lengths) is queried on ConnectAsync.
    /// </summary>
    public LoDiFeedback(
        XElement commandStationElement,
        bool diagnosticLogging = false,
        bool suppressHeartbeatDiagnostics = false)
    {
        _diagnosticLogging = diagnosticLogging;

        // Parse command station UID
        UniqueId = CommandStationUtils.RequireGuidAttribute(commandStationElement, "<commandstation>");

        // Parse connection element
        var connectionElement = commandStationElement.Element("connection")
                                ?? throw new InvalidOperationException(
                                    $"Command station '{UniqueId}' is missing required <connection> element.");

        _ipAddress =
            CommandStationUtils.RequireAttribute(connectionElement, "ip", $"commandstation '{UniqueId}' / connection");
        _port = CommandStationUtils.ParseIntAttribute(connectionElement, "port", LoDiProtocol.DefaultTcpPort, 1, 65535);

        // Parse startup element
        var startupElement = commandStationElement.Element("startup");
        _queryOnStartup = CommandStationUtils.ParseBoolAttribute(startupElement, "queryOnStartup", true);
        _subscribeOnStartup = CommandStationUtils.ParseBoolAttribute(startupElement, "subscribeOnStartup", true);

        _commander = new LoDiS88Commander();
        if (_diagnosticLogging)
            _commander.DiagnosticLogging = true;
        _commander.SuppressHeartbeatDiagnostics = suppressHeartbeatDiagnostics;

        _commander.ContactStateChanged += OnContactStateChanged;
        _commander.ModuleStateReceived += OnModuleStateReceived;
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
            _inputStates.Clear();
        }
    }

    // -------------------------------------------------------------------------
    // IFeedbackController
    // -------------------------------------------------------------------------

    public Guid UniqueId { get; }

    public bool IsConnected => _commander.IsConnected;

    /// <summary>Total sensors = Bus1 + Bus2 (queried from device on connect).</summary>
    public int InputCount => _bus1SensorCount + _bus2SensorCount;

    public event EventHandler<InputStateChangedEventArgs>? InputStateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_diagnosticLogging)
            LoDiLog.FeedbackDebug($"Verbinde Modúl {UniqueId}...");

        await _commander.ConnectAsync(_ipAddress, _port, _subscribeOnStartup, cancellationToken)
            .ConfigureAwait(false);

        if (_diagnosticLogging)
            LoDiLog.FeedbackDebug($"Modúl {UniqueId} verbunden: {IsConnected}");

        // Abfrage Device-Info vor allen anderen Operationen
        var deviceInfo = await _commander.QueryDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
        _bus1SensorCount = deviceInfo.Bus1SensorCount;
        _bus2SensorCount = deviceInfo.Bus2SensorCount;
        _configuredModuleCount = InputCount > 0 ? InputCount / 16 : 0;

        // Initialisiere Sensor-States
        lock (_syncRoot)
        {
            _inputStates.Clear();
            for (var i = 1; i <= InputCount; i++)
                _inputStates[i] = InputState.Inactive;
        }

        if (_diagnosticLogging)
            LoDiLog.FeedbackDebug(
                $"Device-Info abgerufen: Bus1={_bus1SensorCount} Sensoren, Bus2={_bus2SensorCount} Sensoren, Summe={InputCount}");

        // Initialzustand via globale Modulabfrage (0x20/0x30)
        if (_queryOnStartup) await QueryModulesAndAwaitStateAsync(cancellationToken).ConfigureAwait(false);


        if (_diagnosticLogging)
            LoDiLog.FeedbackDebug($"Modúl {UniqueId} vollständig initialisiert");
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Globale Event-Rückmeldungen beenden (optional, Verbindung wird danach getrennt).
        try
        {
            await _commander.UnsubscribeEventsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            /* Disconnect must continue. */
        }

        await _commander.DisconnectAsync().ConfigureAwait(false);
    }

    public InputState GetInputState(int inputNumber)
    {
        if (inputNumber < 1 || inputNumber > InputCount)
            throw new ArgumentOutOfRangeException(nameof(inputNumber),
                $"Sensor number must be between 1 and {InputCount}.");

        lock (_syncRoot)
        {
            return _inputStates.GetValueOrDefault(inputNumber, InputState.Inactive);
        }
    }

    public async Task<IReadOnlyDictionary<int, InputState>> QueryFeedbackAsync(
        CancellationToken cancellationToken = default)
    {
        // Globale Modulabfrage (0x20/0x30) und Initialisierung aus ACK 0x21/0x30
        await QueryModulesAndAwaitStateAsync(cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            return new Dictionary<int, InputState>(_inputStates);
        }
    }

    // -------------------------------------------------------------------------
    // Internes Bus/Sensor-Mapping
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Maps module address + contact number to flat IFeedbackController sensor number.
    ///     Module 1 => 1..16, Module 2 => 17..32, ...
    /// </summary>
    private int ModuleContactToInputNumber(int moduleAddress, int contactNumber)
    {
        if (moduleAddress < 1 || contactNumber < 1 || contactNumber > 16)
            return -1;

        var inputNumber = (moduleAddress - 1) * 16 + contactNumber;
        return inputNumber <= InputCount ? inputNumber : -1;
    }

    private string InputNumberToInputName(int inputNumber)
    {
        if (inputNumber < 1)
            return "1.1";

        if (inputNumber <= _bus1SensorCount)
            return $"1.{inputNumber}";

        return $"2.{inputNumber - _bus1SensorCount}";
    }

    // -------------------------------------------------------------------------
    // S88-Event-Handler
    // -------------------------------------------------------------------------

    private void OnContactStateChanged(object? sender, S88StateChangedEventArgs e)
    {
        var inputNumber = ModuleContactToInputNumber(e.ModuleAddress, e.ContactNumber);
        if (inputNumber < 1) return;
        var inputName = InputNumberToInputName(inputNumber);

        var state = e.IsOccupied ? InputState.Active : InputState.Inactive;

        lock (_syncRoot)
        {
            _inputStates[inputNumber] = state;
        }

        if (_diagnosticLogging)
            LoDiLog.FeedbackDebug($"Sensor {inputNumber:D4} ({inputName}) => {state}");

        InputStateChanged?.Invoke(this, new InputStateChangedEventArgs(
            UniqueId,
            new InputInfo(inputNumber, inputName, state)));
    }

    private void OnModuleStateReceived(object? sender, S88ModuleStateEventArgs e)
    {
        var changed = new List<(int InputNumber, string InputName, InputState State)>();
        string? moduleSnapshotDiagnostic = null;
        string? invalidModuleDiagnostic = null;

        lock (_syncRoot)
        {
            if (_pendingModulesQuery is not null)
            {
                if (_pendingModulesExpectedCount == 0)
                    _pendingModulesExpectedCount = DetermineExpectedModuleCount(e.SnapshotCount);

                // During initial snapshot queries, ignore placeholder/non-existent module ids.
                if (!IsValidModuleAddressForSnapshot(e.ModuleAddress))
                {
                    _pendingInvalidModulesSeen.Add(e.ModuleAddress);
                    if (_diagnosticLogging)
                        invalidModuleDiagnostic =
                            $"Ignoriere ungueltige Moduladresse {e.ModuleAddress:D3} waehrend Snapshot-Warmup";
                }
                else
                {
                    _pendingModulesSeen.Add(e.ModuleAddress);
                    _pendingModuleSnapshots[e.ModuleAddress] = (e.StatusHigh, e.StatusLow);

                    if (ShouldCompletePendingSnapshotLocked())
                    {
                        ApplyBufferedSnapshotLocked(changed);
                        _pendingModulesQuery.TrySetResult(true);
                    }
                }
            }
            else
            {
                UpdateModuleStateLocked(e.ModuleAddress, e.StatusHigh, e.StatusLow, changed);
            }

            if (_diagnosticLogging)
                moduleSnapshotDiagnostic = BuildModuleSnapshotDiagnostic(e);
        }

        if (_diagnosticLogging && invalidModuleDiagnostic is not null)
            LoDiLog.FeedbackDebug(invalidModuleDiagnostic);

        if (_diagnosticLogging && moduleSnapshotDiagnostic is not null)
            LoDiLog.FeedbackDebug(moduleSnapshotDiagnostic);

        foreach (var (inputNumber, inputName, state) in changed)
        {
            if (_diagnosticLogging)
                LoDiLog.FeedbackDebug($"Sensor {inputNumber:D4} ({inputName}) => {state}");

            InputStateChanged?.Invoke(this, new InputStateChangedEventArgs(
                UniqueId,
                new InputInfo(inputNumber, inputName, state)));
        }
    }

    // -------------------------------------------------------------------------
    // Hilfsmethoden
    // -------------------------------------------------------------------------

    private static string BuildModuleSnapshotDiagnostic(S88ModuleStateEventArgs snapshot)
    {
        var activeContacts = snapshot.GetActiveContacts();
        return
            $"Modul {snapshot.ModuleAddress:D3} Snapshot: High=0x{snapshot.StatusHigh:X2}, Low=0x{snapshot.StatusLow:X2}, Active=[{FormatContacts(activeContacts)}]";
    }

    private static string FormatContacts(IEnumerable<int> contacts)
    {
        return string.Join(", ", contacts);
    }

    /// <summary>Global module query (0x20/0x30) and wait for initial module states.</summary>
    private async Task QueryModulesAndAwaitStateAsync(CancellationToken cancellationToken)
    {
        if (InputCount <= 0)
            return;

        TaskCompletionSource<bool> waitHandle;
        lock (_syncRoot)
        {
            waitHandle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingModulesQuery = waitHandle;
            _pendingModulesSeen.Clear();
            _pendingInvalidModulesSeen.Clear();
            _pendingModuleSnapshots.Clear();
            _pendingModulesExpectedCount = _configuredModuleCount > 0 ? _configuredModuleCount : 0;
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
                _pendingInvalidModulesSeen.Clear();
                _pendingModuleSnapshots.Clear();
                _pendingModulesExpectedCount = 0;
            }
        }
    }

    private int DetermineExpectedModuleCount(int snapshotCount)
    {
        if (_configuredModuleCount > 0 && snapshotCount > 0)
            return Math.Min(_configuredModuleCount, snapshotCount);

        if (_configuredModuleCount > 0)
            return _configuredModuleCount;

        return snapshotCount > 0 ? snapshotCount : 0;
    }

    private bool IsValidModuleAddressForSnapshot(int moduleAddress)
    {
        if (moduleAddress == InvalidModulePlaceholder)
            return false;

        if (moduleAddress < 1)
            return false;

        if (_configuredModuleCount > 0)
            return moduleAddress <= _configuredModuleCount;

        return true;
    }

    private bool ShouldCompletePendingSnapshotLocked()
    {
        return _pendingModulesExpectedCount > 0
               && _pendingModulesSeen.Count >= _pendingModulesExpectedCount
               && _pendingInvalidModulesSeen.Count == 0;
    }

    private void ApplyBufferedSnapshotLocked(List<(int InputNumber, string InputName, InputState State)> changed)
    {
        foreach (var (moduleAddress, state) in _pendingModuleSnapshots)
            UpdateModuleStateLocked(moduleAddress, state.StatusHigh, state.StatusLow, changed);
    }

    private void UpdateModuleStateLocked(int moduleAddress, byte statusHigh, byte statusLow,
        List<(int InputNumber, string InputName, InputState State)> changed)
    {
        var stateBitmask = (ushort)((statusHigh << 8) | statusLow);

        for (var contact = 1; contact <= 16; contact++)
        {
            var inputNumber = ModuleContactToInputNumber(moduleAddress, contact);
            if (inputNumber < 1) continue;

            var isActive = (stateBitmask & (1 << (contact - 1))) != 0;
            var newState = isActive ? InputState.Active : InputState.Inactive;

            if (_inputStates.TryGetValue(inputNumber, out var current) && current == newState)
                continue;

            _inputStates[inputNumber] = newState;
            changed.Add((inputNumber, InputNumberToInputName(inputNumber), newState));
        }
    }
}