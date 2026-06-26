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
public sealed class Feedback : IFeedback, IDisposable
{
    // "Hartes" Treiber-Mapping aus commandstation.xml. ToDo: zukünftig flexibilisieren mittels zentraler Treiber-Liste 
    private const string DriverLoDiS88Commander = "lodi-s88-commander";
    private const string DriverMockKeyboardFeedback = "mock-keyboard-feedback";
    private readonly int _configuredSensorCount;

    private readonly IFeedback _driver;
    private readonly Lock _syncRoot = new();
    private bool _disposed;
    private bool _hasInitialSnapshot;
    private bool _sensorEventsSubscribed;
    private SensorInfo[] _sensorInfos = [];
    private EventHandler<SensorStateChangedEventArgs>? _sensorStateChanged;

    public Feedback(Guid stationUid)
    {
        var commandStationElement = CommandStationUtils.LoadCommandStationElement(stationUid);
        _configuredSensorCount = CommandStationUtils.ParseIntElement(commandStationElement, "sensorcount", 0, 1);
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
    public int SensorCount => GetEffectiveSensorCount();

    /// <summary>
    ///     Occurs when a sensor state changes.
    /// </summary>
    public event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged
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
                    $"Feedback device '{UniqueId}' has no valid sensor count. Configure <sensorcount> in commandstations.xml or use a driver that reports SensorCount > 0.");

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
    /// <param name="sensorNumber">The sensor number (1-based).</param>
    /// <returns>The current state of the sensor.</returns>
    public RailSensorState GetSensorState(int sensorNumber)
    {
        lock (_syncRoot)
        {
            if (_hasInitialSnapshot && sensorNumber >= 1 && sensorNumber <= _sensorInfos.Length)
                return _sensorInfos[sensorNumber - 1].State;
        }

        return _driver.GetSensorState(sensorNumber);
    }

    /// <summary>
    ///     Asynchronously refreshes and returns the sensor state snapshot from the feedback device.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result is a read-only dictionary of sensor numbers
    ///     to their states.
    /// </returns>
    public Task<IReadOnlyDictionary<int, RailSensorState>> QueryAllSensorsAsync(
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

        Logging.Info(LogCategory.Feedback, "Keine Verbindung zum Rückmeldemodul. Verbinde...");

        try
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Logging.Error(LogCategory.Feedback, $"Fehler beim Verbinden des Rückmeldemoduls: {ex.Message}", ex);
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

        Logging.Info(LogCategory.Feedback, "Rückmeldemodul ist verbunden, initialisiere Sensor-Snapshot...");

        try
        {
            _ = await RefreshSensorSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Logging.Error(LogCategory.Feedback, $"Fehler beim Initialisieren des Rückmeldemoduls: {ex.Message}", ex);
            return false;
        }
    }

    // Liest die aktuellen Sensor-Zustände von der Zentrale ein
    private async Task<IReadOnlyDictionary<int, RailSensorState>> RefreshSensorSnapshotAsync(
        CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Rueckmeldemodul konnte nicht verbunden werden.");

        var snapshot = await _driver.QueryAllSensorsAsync(cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            if (_sensorInfos.Length == 0)
                if (!TryInitializeSensorCacheLocked(GetEffectiveSensorCount()))
                    throw new InvalidOperationException(
                        $"Feedback device '{UniqueId}' did not provide a valid sensor count and no <sensorcount> was configured in commandstations.xml.");

            SubscribeToDriverEventsLocked();
        }

        CacheSnapshot(snapshot);
        return snapshot;
    }

    // Übernimmt die aktuellen Sensor-Zustände in den internen Cache. 
    private void CacheSnapshot(IReadOnlyDictionary<int, RailSensorState> snapshot)
    {
        lock (_syncRoot)
        {
            if (_sensorInfos.Length == 0)
                throw new InvalidOperationException($"Feedback device '{UniqueId}' has no initialized sensor cache.");

            // Echte Kopie des bisherigen Zustands, damit die SensorNames bei einem
            // Refresh erhalten bleiben – previousInfos darf NICHT dieselbe Referenz
            // wie _sensorInfos sein, sonst liest man nach dem ersten Schreibzugriff
            // bereits den neuen Wert statt des alten SensorName.
            var previousInfos = (SensorInfo[])_sensorInfos.Clone();

            foreach (var (sensorNumber, state) in snapshot)
            {
                if (sensorNumber < 1 || sensorNumber > _sensorInfos.Length)
                    throw new InvalidOperationException(
                        $"Rueckmeldemodul '{UniqueId}' lieferte ungueltige Sensor-Nummer {sensorNumber} " +
                        $"im Snapshot (erwartet: 1..{_sensorInfos.Length}).");

                var sensorName = sensorNumber <= previousInfos.Length
                    ? previousInfos[sensorNumber - 1].SensorName
                    : sensorNumber.ToString();

                _sensorInfos[sensorNumber - 1] = new SensorInfo(sensorNumber, sensorName, state);
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
            _sensorInfos = [];
            _hasInitialSnapshot = false;
            LastSnapshotUtc = null;
            _sensorEventsSubscribed = false;
        }
    }

    private void OnDriverSensorStateChanged(object? sender, SensorStateChangedEventArgs args)
    {
        var shouldRaise = true;

        lock (_syncRoot)
        {
            if (_sensorInfos.Length == 0)
                throw new InvalidOperationException(
                    $"Feedback device '{UniqueId}' has no initialized sensor cache for incoming events.");

            if (args.SensorNumber < 1 || args.SensorNumber > _sensorInfos.Length)
                throw new InvalidOperationException(
                    $"Feedback device '{UniqueId}' has an invalid sensor number {args.SensorNumber} " +
                    $"from event (expected: 1..{_sensorInfos.Length}).");

            var current = _sensorInfos[args.SensorNumber - 1];
            if (current.SensorNumber == args.SensorNumber && current.State == args.State)
                shouldRaise = false;

            var sensorName = string.IsNullOrWhiteSpace(args.SensorName)
                ? current.SensorNumber == args.SensorNumber ? current.SensorName : args.SensorNumber.ToString()
                : args.SensorName;

            _sensorInfos[args.SensorNumber - 1] = new SensorInfo(args.SensorNumber, sensorName, args.State);
        }

        if (shouldRaise)
            _sensorStateChanged?.Invoke(this, args);
    }

    private bool TryInitializeSensorCacheLocked(int sensorCount)
    {
        if (sensorCount < 1)
            return false;

        _sensorInfos = new SensorInfo[sensorCount];
        for (var sensorNumber = 1; sensorNumber <= _sensorInfos.Length; sensorNumber++)
            _sensorInfos[sensorNumber - 1] =
                new SensorInfo(sensorNumber, sensorNumber.ToString(), RailSensorState.Inactive);

        return true;
    }

    private void SubscribeToDriverEventsLocked()
    {
        if (_sensorEventsSubscribed)
            return;

        _driver.SensorStateChanged += OnDriverSensorStateChanged;
        _sensorEventsSubscribed = true;
    }

    private void UnsubscribeFromDriverEvents()
    {
        if (!_sensorEventsSubscribed)
            return;

        _driver.SensorStateChanged -= OnDriverSensorStateChanged;
        _sensorEventsSubscribed = false;
    }

    private int GetEffectiveSensorCount()
    {
        return _configuredSensorCount > 0 ? _configuredSensorCount : _driver.SensorCount;
    }

    private static IFeedback CreateDriver(XElement commandStationElement, Guid stationUid)
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