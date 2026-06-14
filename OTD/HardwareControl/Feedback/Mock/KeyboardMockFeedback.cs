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

namespace OTD.HardwareControl.Drivers;

/// <summary>
/// Mock feedback provider for console keyboard simulation.
/// Sensors are mapped to keyboard keys and exposed as 1-based sensor numbers.
/// </summary>
internal sealed class KeyboardMockFeedback : IFeedback
{
    // Simulated key-release timeout for console input (no real KeyUp available).
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromMilliseconds(300);

    private readonly Dictionary<int, RailSensorState> _states = [];
    private readonly Dictionary<int, DateTimeOffset> _lastKeyPressUtc = [];
    private readonly HashSet<int> _heldSensors = [];
    private readonly Dictionary<ConsoleKey, int> _keyToSensor = BuildKeyMap();
    private bool _connected;

    public KeyboardMockFeedback(Guid uniqueId)
    {
        UniqueId = uniqueId;
        for (var i = 1; i <= 40; i++)
            _states[i] = RailSensorState.Inactive;
    }

    public Guid UniqueId { get; }

    public bool IsConnected => _connected;

    public int SensorCount => 40;

    public event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _connected = false;
        _heldSensors.Clear();
        _lastKeyPressUtc.Clear();

        // Reset all sensors to inactive on disconnect.
        foreach (var sensor in _states.Keys.ToArray())
            SetState(sensor, RailSensorState.Inactive);

        return Task.CompletedTask;
    }

    public RailSensorState GetSensorState(int sensorNumber)
    {
        if (sensorNumber < 1 || sensorNumber > SensorCount)
            throw new ArgumentOutOfRangeException(nameof(sensorNumber), "Sensor number must be in range 1..40.");

        return _states[sensorNumber];
    }

    public Task<IReadOnlyDictionary<int, RailSensorState>> QueryAllSensorsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<int, RailSensorState> snapshot = new Dictionary<int, RailSensorState>(_states);
        return Task.FromResult(snapshot);
    }

    public bool TryHandleKey(ConsoleKey key)
    {
        if (!_connected)
            return false;

        if (!_keyToSensor.TryGetValue(key, out var sensorNumber))
            return false;

        var nowUtc = DateTimeOffset.UtcNow;
        _lastKeyPressUtc[sensorNumber] = nowUtc;

        // Ignore auto-repeat while key is considered held.
        if (_heldSensors.Contains(sensorNumber))
            return true;

        _heldSensors.Add(sensorNumber);
        var nextState = _states[sensorNumber] == RailSensorState.Active
            ? RailSensorState.Inactive
            : RailSensorState.Active;
        SetState(sensorNumber, nextState);
        return true;
    }

    public void UpdateKeyReleases(DateTimeOffset nowUtc)
    {
        if (!_connected)
            return;

        foreach (var (sensorNumber, lastPressUtc) in _lastKeyPressUtc.ToArray())
        {
            if (nowUtc - lastPressUtc >= ReleaseTimeout)
            {
                _heldSensors.Remove(sensorNumber);
                _lastKeyPressUtc.Remove(sensorNumber);
            }
        }
    }

    private void SetState(int sensorNumber, RailSensorState newState)
    {
        if (_states[sensorNumber] == newState)
            return;

        _states[sensorNumber] = newState;
        SensorStateChanged?.Invoke(this, new SensorStateChangedEventArgs(UniqueId, sensorNumber, newState));
    }

    private static Dictionary<ConsoleKey, int> BuildKeyMap()
    {
        return new Dictionary<ConsoleKey, int>
        {
            [ConsoleKey.D1] = 1,
            [ConsoleKey.D2] = 2,
            [ConsoleKey.D3] = 3,
            [ConsoleKey.D4] = 4,
            [ConsoleKey.D5] = 5,
            [ConsoleKey.D6] = 6,
            [ConsoleKey.D7] = 7,
            [ConsoleKey.D8] = 8,
            [ConsoleKey.D9] = 9,
            [ConsoleKey.D0] = 10,

            [ConsoleKey.Q] = 11,
            [ConsoleKey.W] = 12,
            [ConsoleKey.E] = 13,
            [ConsoleKey.R] = 14,
            [ConsoleKey.T] = 15,
            [ConsoleKey.Z] = 16,
            [ConsoleKey.U] = 17,
            [ConsoleKey.I] = 18,
            [ConsoleKey.O] = 19,
            [ConsoleKey.P] = 20,

            [ConsoleKey.A] = 21,
            [ConsoleKey.S] = 22,
            [ConsoleKey.D] = 23,
            [ConsoleKey.F] = 24,
            [ConsoleKey.G] = 25,
            [ConsoleKey.H] = 26,
            [ConsoleKey.J] = 27,
            [ConsoleKey.K] = 28,
            [ConsoleKey.L] = 29,
            [ConsoleKey.Oem1] = 30, // German keyboard: key right of L (oe)

            [ConsoleKey.Y] = 31,
            [ConsoleKey.X] = 32,
            [ConsoleKey.C] = 33,
            [ConsoleKey.V] = 34,
            [ConsoleKey.B] = 35,
            [ConsoleKey.N] = 36,
            [ConsoleKey.M] = 37,
            [ConsoleKey.OemComma] = 38,
            [ConsoleKey.OemPeriod] = 39,
            [ConsoleKey.OemMinus] = 40
        };
    }
}

