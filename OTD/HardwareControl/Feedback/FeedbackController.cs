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
using OTD.Common;
using OTD.HardwareControl.Drivers;

namespace OTD.HardwareControl;

/// <summary>
///     Orchestrates a single feedback device.
///     This wrapper encapsulates connection setup, initial snapshot loading,
///     and consistent high-level access to sensor states.
/// </summary>
public sealed class FeedbackController : IFeedbackController, IDisposable
{
    // "Hartes" Treiber-Mapping aus commandstation.xml. ToDo: zukünftig flexibilisieren mittels zentraler Treiber-Liste 
    private const string DriverLoDiS88Commander = "lodi-s88-commander";
    private const string DriverMockKeyboardFeedback = "mock-keyboard-feedback";
    private readonly int _configuredInputCount;

    private readonly IFeedbackController _driver;
    private readonly Lock _syncRoot = new();
    private bool _disposed;
    private bool _hasInitialSnapshot;
    private bool _inputEventsSubscribed;
    private InputInfo[] _inputInfos = [];
    private EventHandler<InputStateChangedEventArgs>? _sensorStateChanged;

    public FeedbackController(Guid stationUid)
    {
        var commandStationElement = CommandStationUtils.LoadCommandStationElement(stationUid);
        _configuredInputCount = CommandStationUtils.ParseIntElement(commandStationElement, "sensorcount", 0, 1);
        _driver = CreateDriver(commandStationElement, stationUid);
    }

    /// <summary>
    ///     Operational status symmetric to the CommandStation facade.
    ///     True when connected and an initial snapshot has been loaded.
    /// </summary>
    public bool IsOperational => IsMonitoringReady;

    /// <summary>
    ///     True once the wrapper has a valid initial snapshot and can provide
    ///     consistent high-level sensor states.
    /// </summary>
    public bool IsMonitoringReady
    {
        get
        {
            lock (_syncRoot)
            {
                return IsConnected && _hasInitialSnapshot;
            }
        }
    }

    /// <summary>
    ///     Gets the name of the feedback driver implementation.
    /// </summary>
    public string DriverName => _driver.GetType().Name;

    /// <summary>
    ///     UTC timestamp of the most recent successfully loaded full snapshot.
    /// </summary>
    public DateTimeOffset? LastSnapshotUtc { get; private set; }

    /// <summary>
    ///     Releases all resources used by the Feedback instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeFromDriverEvents();

        if (_driver is IDisposable disposable)
            disposable.Dispose();

        ResetCachedState();
    }

    /// <summary>
    ///     Gets the unique identifier of the feedback device.
    /// </summary>
    public Guid UniqueId => _driver.UniqueId;

    /// <summary>
    ///     Gets a value indicating whether the feedback device is currently connected.
    /// </summary>
    public bool IsConnected => _driver.IsConnected;

    /// <summary>
    ///     Total number of sensors supported by the feedback device.
    ///     Provided either by the feedback device or by the configuration in commandstations.xml.
    /// </summary>
    /// <summary>
    ///     Gets the total number of sensors supported by the feedback device.
    ///     Provided either by the feedback device or by the configuration in commandstations.xml.
    /// </summary>
    public int InputCount => GetEffectiveSensorCount();

    /// <summary>
    ///     Occurs when a sensor state changes.
    /// </summary>
    public event EventHandler<InputStateChangedEventArgs>? InputStateChanged
    {
        add => _sensorStateChanged += value;
        remove => _sensorStateChanged -= value;
    }

    /// <summary>
    ///     Asynchronously connects the feedback device.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return;

        ResetCachedState();
        await _driver.ConnectAsync(cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            if (!TryInitializeSensorCacheLocked(GetEffectiveSensorCount()))
                throw new InvalidOperationException(
                    $"Feedback device '{UniqueId}' has no valid sensor count. Configure <sensorcount> in commandstations.xml or use a driver that reports InputCount > 0.");

            SubscribeToDriverEventsLocked();
        }
    }

    /// <summary>
    ///     Asynchronously disconnects the feedback device.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            await _driver.DisconnectAsync(cancellationToken).ConfigureAwait(false);

        ResetCachedState();
    }

    /// <summary>
    ///     Gets the state of the specified sensor from the cached snapshot.
    /// </summary>
    /// <param name="inputNumber">The sensor number (1-based).</param>
    /// <returns>The current state of the sensor.</returns>
    public InputState GetInputState(int inputNumber)
    {
        lock (_syncRoot)
        {
            if (_hasInitialSnapshot && inputNumber >= 1 && inputNumber <= _inputInfos.Length)
                return _inputInfos[inputNumber - 1].State;
        }

        return _driver.GetInputState(inputNumber);
    }

    /// <summary>
    ///     Asynchronously refreshes and returns the sensor state snapshot from the feedback device.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result is a read-only dictionary of sensor numbers
    ///     to their states.
    /// </returns>
    public Task<IReadOnlyDictionary<int, InputState>> QueryFeedbackAsync(
        CancellationToken cancellationToken = default)
    {
        return RefreshSensorSnapshotAsync(cancellationToken);
    }

    /// <summary>
    ///     Asynchronously ensures the feedback device is connected.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result is true if the device is connected;
    ///     otherwise, false.
    /// </returns>
    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return true;

        Logging.Info<FeedbackController>("Keine Verbindung zum Rückmeldemodul. Verbinde...");

        try
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Logging.Error<FeedbackController>($"Fehler beim Verbinden des Rückmeldemoduls: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    ///     Alias for EnsureOperationalAsync; kept for backward compatibility.
    /// </summary>
    public Task<bool> EnsureMonitoringAsync(CancellationToken cancellationToken = default)
    {
        return EnsureOperationalAsync(cancellationToken);
    }

    /// <summary>
    ///     Ensures the feedback module is connected and an initial
    ///     sensor state snapshot has been loaded successfully.
    /// </summary>
    public async Task<bool> EnsureOperationalAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false))
            return false;

        if (IsMonitoringReady)
            return true;

        Logging.Info<FeedbackController>("Rückmeldemodul ist verbunden, initialisiere Sensor-Snapshot...");

        try
        {
            _ = await RefreshSensorSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Logging.Error<FeedbackController>($"Fehler beim Initialisieren des Rückmeldemoduls: {ex.Message}", ex);
            return false;
        }
    }

    // Liest die aktuellen Sensor-Zustände von der Zentrale ein
    private async Task<IReadOnlyDictionary<int, InputState>> RefreshSensorSnapshotAsync(
        CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Rueckmeldemodul konnte nicht verbunden werden.");

        var snapshot = await _driver.QueryFeedbackAsync(cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            if (_inputInfos.Length == 0)
                if (!TryInitializeSensorCacheLocked(GetEffectiveSensorCount()))
                    throw new InvalidOperationException(
                        $"Feedback device '{UniqueId}' did not provide a valid sensor count and no <sensorcount> was configured in commandstations.xml.");

            SubscribeToDriverEventsLocked();
        }

        CacheSnapshot(snapshot);
        return snapshot;
    }

    // Übernimmt die aktuellen Sensor-Zustände in den internen Cache. 
    private void CacheSnapshot(IReadOnlyDictionary<int, InputState> snapshot)
    {
        lock (_syncRoot)
        {
            if (_inputInfos.Length == 0)
                throw new InvalidOperationException($"Feedback device '{UniqueId}' has no initialized sensor cache.");

            // Echte Kopie des bisherigen Zustands, damit die SensorNames bei einem
            // Refresh erhalten bleiben – previousInfos darf NICHT dieselbe Referenz
            // wie _inputInfos sein, sonst liest man nach dem ersten Schreibzugriff
            // bereits den neuen Wert statt des alten InputName.
            var previousInfos = (InputInfo[])_inputInfos.Clone();

            foreach (var (inputNumber, state) in snapshot)
            {
                if (inputNumber < 1 || inputNumber > _inputInfos.Length)
                    throw new InvalidOperationException(
                        $"Rueckmeldemodul '{UniqueId}' lieferte ungueltige Sensor-Nummer {inputNumber} " +
                        $"im Snapshot (erwartet: 1..{_inputInfos.Length}).");

                var inputName = inputNumber <= previousInfos.Length
                    ? previousInfos[inputNumber - 1].InputName
                    : inputNumber.ToString();

                _inputInfos[inputNumber - 1] = new InputInfo(inputNumber, inputName, state);
            }

            _hasInitialSnapshot = true;
            LastSnapshotUtc = DateTimeOffset.UtcNow;
        }
    }

    private void ResetCachedState()
    {
        UnsubscribeFromDriverEvents();

        lock (_syncRoot)
        {
            _inputInfos = [];
            _hasInitialSnapshot = false;
            LastSnapshotUtc = null;
            _inputEventsSubscribed = false;
        }
    }

    private void OnDriverSensorStateChanged(object? sender, InputStateChangedEventArgs args)
    {
        var shouldRaise = true;

        lock (_syncRoot)
        {
            if (_inputInfos.Length == 0)
                throw new InvalidOperationException(
                    $"Feedback device '{UniqueId}' has no initialized sensor cache for incoming events.");

            if (args.InputNumber < 1 || args.InputNumber > _inputInfos.Length)
                throw new InvalidOperationException(
                    $"Feedback device '{UniqueId}' has an invalid sensor number {args.InputNumber} " +
                    $"from event (expected: 1..{_inputInfos.Length}).");

            var current = _inputInfos[args.InputNumber - 1];
            if (current.InputNumber == args.InputNumber && current.State == args.State)
                shouldRaise = false;

            var inputName = string.IsNullOrWhiteSpace(args.InputName)
                ? current.InputNumber == args.InputNumber ? current.InputName : args.InputNumber.ToString()
                : args.InputName;

            _inputInfos[args.InputNumber - 1] = new InputInfo(args.InputNumber, inputName, args.State);
        }

        if (shouldRaise)
            _sensorStateChanged?.Invoke(this, args);
    }

    private bool TryInitializeSensorCacheLocked(int inputCount)
    {
        if (inputCount < 1)
            return false;

        _inputInfos = new InputInfo[inputCount];
        for (var inputNumber = 1; inputNumber <= _inputInfos.Length; inputNumber++)
            _inputInfos[inputNumber - 1] =
                new InputInfo(inputNumber, inputNumber.ToString(), InputState.Inactive);

        return true;
    }

    private void SubscribeToDriverEventsLocked()
    {
        if (_inputEventsSubscribed)
            return;

        _driver.InputStateChanged += OnDriverSensorStateChanged;
        _inputEventsSubscribed = true;
    }

    private void UnsubscribeFromDriverEvents()
    {
        if (!_inputEventsSubscribed)
            return;

        _driver.InputStateChanged -= OnDriverSensorStateChanged;
        _inputEventsSubscribed = false;
    }

    private int GetEffectiveSensorCount()
    {
        return _configuredInputCount > 0 ? _configuredInputCount : _driver.InputCount;
    }

    private static IFeedbackController CreateDriver(XElement commandStationElement, Guid stationUid)
    {
        var driverName = CommandStationUtils
            .RequireAttribute(commandStationElement, "driver", $"commandstation '{stationUid}'")
            .Trim()
            .ToLowerInvariant();
        var diagnosticsElement = commandStationElement.Element("diagnostics");
        var diagnosticLogging = CommandStationUtils.ParseBoolAttribute(diagnosticsElement, "enabled", false);
        var suppressHeartbeatDiagnostics = CommandStationUtils.ParseBoolAttribute(
            diagnosticsElement,
            "suppressHeartbeatDiagnostics",
            false);

        return driverName switch
        {
            DriverLoDiS88Commander => new LoDiFeedback(
                commandStationElement,
                diagnosticLogging,
                suppressHeartbeatDiagnostics),
            DriverMockKeyboardFeedback => new KeyboardMockFeedback(stationUid),
            _ => throw new InvalidOperationException(
                $"Command station '{stationUid}' has unsupported feedback driver '{driverName}'.")
        };
    }
}