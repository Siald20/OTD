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
///     Mock feedback provider for console keyboard simulation.
///     Sensors are mapped to keyboard keys and exposed as 1-based sensor numbers.
/// </summary>
internal sealed class KeyboardMockFeedback : IFeedbackController
{
    // Simulated key-release timeout for console input (no real KeyUp available).
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromMilliseconds(300);
    private readonly HashSet<int> _heldInputs = [];
    private readonly Dictionary<ConsoleKey, int> _keyToInput = BuildKeyMap();
    private readonly Dictionary<int, DateTimeOffset> _lastKeyPressUtc = [];

    private readonly Dictionary<int, InputState> _states = [];

    public KeyboardMockFeedback(Guid uniqueId)
    {
        UniqueId = uniqueId;
        for (var i = 1; i <= 40; i++)
            _states[i] = InputState.Inactive;
    }

    public Guid UniqueId { get; }

    public bool IsConnected { get; private set; }

    public int InputCount => 40;

    public event EventHandler<InputStateChangedEventArgs>? InputStateChanged;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = false;
        _heldInputs.Clear();
        _lastKeyPressUtc.Clear();

        // Reset all sensors to inactive on disconnect.
        foreach (var sensor in _states.Keys.ToArray())
            SetState(sensor, InputState.Inactive);

        return Task.CompletedTask;
    }

    public InputState GetInputState(int inputNumber)
    {
        if (inputNumber < 1 || inputNumber > InputCount)
            throw new ArgumentOutOfRangeException(nameof(inputNumber), "Sensor number must be in range 1..40.");

        return _states[inputNumber];
    }

    public Task<IReadOnlyDictionary<int, InputState>> QueryFeedbackAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<int, InputState> snapshot = new Dictionary<int, InputState>(_states);
        return Task.FromResult(snapshot);
    }

    public bool TryHandleKey(ConsoleKey key)
    {
        if (!IsConnected)
            return false;

        if (!_keyToInput.TryGetValue(key, out var inputNumber))
            return false;

        var nowUtc = DateTimeOffset.UtcNow;
        _lastKeyPressUtc[inputNumber] = nowUtc;

        // Ignore auto-repeat while key is considered held.
        if (_heldInputs.Contains(inputNumber))
            return true;

        _heldInputs.Add(inputNumber);
        var nextState = _states[inputNumber] == InputState.Active
            ? InputState.Inactive
            : InputState.Active;
        SetState(inputNumber, nextState);
        return true;
    }

    public void UpdateKeyReleases(DateTimeOffset nowUtc)
    {
        if (!IsConnected)
            return;

        foreach (var (inputNumber, lastPressUtc) in _lastKeyPressUtc.ToArray())
            if (nowUtc - lastPressUtc >= ReleaseTimeout)
            {
                _heldInputs.Remove(inputNumber);
                _lastKeyPressUtc.Remove(inputNumber);
            }
    }

    private void SetState(int inputNumber, InputState newState)
    {
        if (_states[inputNumber] == newState)
            return;

        _states[inputNumber] = newState;
        var inputName = $"1.{inputNumber}";
        InputStateChanged?.Invoke(this, new InputStateChangedEventArgs(
            UniqueId,
            new InputInfo(inputNumber, inputName, newState)));
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