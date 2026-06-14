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
using OTD.HardwareControl.Drivers;

namespace OTD.HardwareControl;

/// <summary>
/// Orchestrates a single feedback module.
/// This wrapper encapsulates connection setup, initial snapshot loading,
/// and consistent high-level access to sensor states.
/// </summary>
public sealed class Feedback : IFeedback, IDisposable
{
    private const string DriverLoDiS88Commander = "lodi-s88-commander";
    private const string DriverMockKeyboardFeedback = "mock-keyboard-feedback";

    private readonly IFeedback _driver;
    private readonly Dictionary<int, RailSensorState> _sensorStates = [];
    private readonly Lock _syncRoot = new();
    private EventHandler<SensorStateChangedEventArgs>? _sensorStateChanged;
    private bool _disposed;
    private bool _hasInitialSnapshot;

    public Feedback(Guid moduleUid)
    {
        _driver = CreateDriver(moduleUid);
        _driver.SensorStateChanged += OnDriverSensorStateChanged;
    }

    public Guid UniqueId => _driver.UniqueId;

    public bool IsConnected => _driver.IsConnected;

    /// <summary>
    /// Operational status symmetric to the CommandStation facade.
    /// True when connected and an initial snapshot has been loaded.
    /// </summary>
    public bool IsOperational => IsMonitoringReady;

    /// <summary>
    /// True once the wrapper has a valid initial snapshot and can provide
    /// consistent high-level sensor states.
    /// </summary>
    public bool IsMonitoringReady
    {
        get
        {
            lock (_syncRoot)
                return IsConnected && _hasInitialSnapshot;
        }
    }

    public int SensorCount => _driver.SensorCount;

    public string DriverName => _driver.GetType().Name;

    /// <summary>
    /// UTC timestamp of the most recent successfully loaded full snapshot.
    /// </summary>
    public DateTimeOffset? LastSnapshotUtc { get; private set; }

    public event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged
    {
        add => _sensorStateChanged += value;
        remove => _sensorStateChanged -= value;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return;

        ResetCachedState();
        await _driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            await _driver.DisconnectAsync(cancellationToken).ConfigureAwait(false);

        ResetCachedState();
    }

    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return true;

        Console.WriteLine("Keine Verbindung zum Rueckmeldemodul. Verbinde...");

        try
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Verbinden des Rueckmeldemoduls: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Alias for EnsureOperationalAsync; kept for backward compatibility.
    /// </summary>
    public Task<bool> EnsureMonitoringAsync(CancellationToken cancellationToken = default)
        => EnsureOperationalAsync(cancellationToken);

    /// <summary>
    /// Ensures the feedback module is connected and an initial
    /// sensor state snapshot has been loaded successfully.
    /// </summary>
    public async Task<bool> EnsureOperationalAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false))
            return false;

        if (IsMonitoringReady)
            return true;

        Console.WriteLine("Rueckmeldemodul ist verbunden, initialisiere Sensor-Snapshot...");

        try
        {
            _ = await RefreshSensorSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Initialisieren des Rueckmeldemoduls: {ex.Message}");
            return false;
        }
    }

    public RailSensorState GetSensorState(int sensorNumber)
    {
        lock (_syncRoot)
        {
            if (_sensorStates.TryGetValue(sensorNumber, out var cachedState))
                return cachedState;
        }

        return _driver.GetSensorState(sensorNumber);
    }

    public Task<IReadOnlyDictionary<int, RailSensorState>> QueryAllSensorsAsync(
        CancellationToken cancellationToken = default)
        => RefreshSensorSnapshotAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _driver.SensorStateChanged -= OnDriverSensorStateChanged;

        if (_driver is IDisposable disposable)
            disposable.Dispose();

        ResetCachedState();
    }

    private async Task<IReadOnlyDictionary<int, RailSensorState>> RefreshSensorSnapshotAsync(
        CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Rueckmeldemodul konnte nicht verbunden werden.");

        var snapshot = await _driver.QueryAllSensorsAsync(cancellationToken).ConfigureAwait(false);
        CacheSnapshot(snapshot);
        return snapshot;
    }

    private void CacheSnapshot(IReadOnlyDictionary<int, RailSensorState> snapshot)
    {
        lock (_syncRoot)
        {
            _sensorStates.Clear();
            foreach (var (sensorNumber, state) in snapshot)
                _sensorStates[sensorNumber] = state;

            _hasInitialSnapshot = true;
            LastSnapshotUtc = DateTimeOffset.UtcNow;
        }
    }

    private void ResetCachedState()
    {
        lock (_syncRoot)
        {
            _sensorStates.Clear();
            _hasInitialSnapshot = false;
            LastSnapshotUtc = null;
        }
    }

    private void OnDriverSensorStateChanged(object? sender, SensorStateChangedEventArgs args)
    {
        var shouldRaise = true;

        lock (_syncRoot)
        {
            if (_sensorStates.TryGetValue(args.SensorNumber, out var currentState) && currentState == args.State)
                shouldRaise = false;

            _sensorStates[args.SensorNumber] = args.State;
        }

        if (shouldRaise)
            _sensorStateChanged?.Invoke(this, args);
    }

    private static IFeedback CreateDriver(Guid moduleUid)
    {
        var feedbackModuleElement = CommandStationUtils.LoadFeedbackModuleElement(moduleUid);
        var driverName = CommandStationUtils
            .RequireAttribute(feedbackModuleElement, "driver", $"feedbackmodule '{moduleUid}'")
            .Trim()
            .ToLowerInvariant();
        var diagnosticsElement = feedbackModuleElement.Element("diagnostics");
        var diagnosticLogging = CommandStationUtils.ParseBoolAttribute(diagnosticsElement, "enabled", false);
        var suppressHeartbeatDiagnostics = CommandStationUtils.ParseBoolAttribute(
            diagnosticsElement,
            "suppressHeartbeatDiagnostics",
            false);

        return driverName switch
        {
            DriverLoDiS88Commander => new LoDiFeedback(
                feedbackModuleElement,
                diagnosticLogging,
                suppressHeartbeatDiagnostics),
            DriverMockKeyboardFeedback => new KeyboardMockFeedback(moduleUid),
            _ => throw new InvalidOperationException(
                $"Feedback module '{moduleUid}' has unsupported driver '{driverName}'.")
        };
    }
}


