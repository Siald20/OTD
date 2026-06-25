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

namespace OTD.HardwareControl.Drivers;

/// <summary>
///     Mock implementation of ICommandStation for unit tests and development.
///     Simulates a real command station without physical hardware.
/// </summary>
internal sealed class MockCommandStation : ICommandStation
{
    private readonly Dictionary<int, AccessoryFunctionState> _accessoryStates = new();
    private readonly Dictionary<int, byte> _accessoryValues = new();
    private readonly Dictionary<int, DecoderConfig> _decoderConfigs = new();
    private readonly Dictionary<int, bool> _functionStates = new();
    private readonly Dictionary<int, LocoState> _locoStates = new();

    public bool PowerEnabled { get; private set; }

    public IReadOnlyDictionary<int, LocoState> LocoStates => _locoStates;

    public IReadOnlyDictionary<int, bool> FunctionStates => _functionStates;
    public IReadOnlyDictionary<int, byte> AccessoryValues => _accessoryValues;
    public IReadOnlyDictionary<int, AccessoryFunctionState> AccessoryStates => _accessoryStates;
    public event EventHandler<LocoStateChangedEventArgs>? LocoStateChanged;
    public event EventHandler<AccessoryStateChangedEventArgs>? AccessoryStateChanged;

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return ConnectAsync("mock", 1, cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        await Task.Delay(50);
        IsConnected = false;
        PowerEnabled = false;
        _locoStates.Clear();
        _functionStates.Clear();
        _accessoryValues.Clear();
        _accessoryStates.Clear();
        Console.WriteLine("[MockCommandStation] Verbindung beendet");
    }

    public async Task SetPowerAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await Task.Delay(50, cancellationToken);
        PowerEnabled = isOn;
        Console.WriteLine($"[MockCommandStation] Gleisspannung: {(isOn ? "EIN" : "AUS")}");
        OnCommandExecuted(new MockCommandEventArgs("SetPower", isOn));
    }

    public async Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await Task.Delay(30, cancellationToken);
        Console.WriteLine($"[MockCommandStation] Gleisspannungsstatus abgefragt: {PowerEnabled}");
        OnCommandExecuted(new MockCommandEventArgs("GetPowerState", PowerEnabled));
        return PowerEnabled;
    }

    public void InitializeDecoder(int address, LocoDecoderProtocol protocol, int effectiveSpeedSteps)
    {
        if (address < 1 || address > 9999)
            throw new ArgumentException("Lokadresse muss zwischen 1 und 9999 liegen.", nameof(address));

        _decoderConfigs[address] = new DecoderConfig(protocol, effectiveSpeedSteps);
        Console.WriteLine(
            $"[MockCommandStation] AccessoryDecoder init: Lok {address}, Protocol {protocol}, EffectiveSpeedSteps {effectiveSpeedSteps}");
    }

    public async Task SetLocoSpeedAsync(int address, int speedStep, VehicleDirection direction,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (address < 1 || address > 9999)
            throw new ArgumentException("Lokadresse muss zwischen 1 und 9999 liegen.", nameof(address));

        var maxSpeedStep = _decoderConfigs.TryGetValue(address, out var config)
            ? Math.Max(config.EffectiveSpeedSteps, 1)
            : 126;

        if (speedStep < 0 || speedStep > maxSpeedStep)
            throw new ArgumentException($"Fahrstufe muss zwischen 0 und {maxSpeedStep} liegen.", nameof(speedStep));

        if (!PowerEnabled)
            throw new InvalidOperationException("Gleisspannung ist ausgeschaltet.");

        await Task.Delay(50, cancellationToken);

        var state = new LocoState { Address = address, SpeedStep = speedStep, Direction = direction };
        _locoStates[address] = state;

        Console.WriteLine(
            $"[MockCommandStation] Lok {address}: Protocol {config.Protocol}, Fahrstufe {speedStep}, Richtung {direction}");
        OnCommandExecuted(new MockCommandEventArgs("SetLocoSpeed", state));
        OnLocoStateChanged(new LocoStateChangedEventArgs(
            address,
            speedStep,
            direction,
            null,
            null,
            false));
    }

    public async Task SetLocoFunctionAsync(int address, int functionNumber, bool isOn,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (address < 1 || address > 9999)
            throw new ArgumentException("Lokadresse muss zwischen 1 und 9999 liegen.", nameof(address));

        if (functionNumber < 0 || functionNumber > 127)
            throw new ArgumentException("Funktionsnummer muss zwischen 0 und 127 liegen.", nameof(functionNumber));

        if (!PowerEnabled)
            throw new InvalidOperationException("Gleisspannung ist ausgeschaltet.");

        await Task.Delay(40, cancellationToken);

        var key = (address << 8) | functionNumber;
        _functionStates[key] = isOn;

        Console.WriteLine($"[MockCommandStation] Lok {address}, Funktion {functionNumber}: {(isOn ? "ON" : "OFF")}");
        OnCommandExecuted(new MockCommandEventArgs("SetLocoFunction",
            new { Address = address, Function = functionNumber, State = isOn }));
        OnLocoStateChanged(new LocoStateChangedEventArgs(
            address,
            null,
            VehicleDirection.Undefined,
            functionNumber,
            isOn ? LocoDecoderFunctionState.On : LocoDecoderFunctionState.Off,
            false));
    }

    public async Task EmergencyStopAsync(int address, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (address < 1 || address > 9999)
            throw new ArgumentException("Lokadresse muss zwischen 1 und 9999 liegen.", nameof(address));

        await Task.Delay(50, cancellationToken);

        if (_locoStates.TryGetValue(address, out var state)) state.SpeedStep = 0;

        Console.WriteLine($"[MockCommandStation] Notbremse für Lok {address}");
        OnCommandExecuted(new MockCommandEventArgs("EmergencyStop", address));
    }

    public async Task EmergencyStopAllAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await Task.Delay(100, cancellationToken);

        foreach (var state in _locoStates.Values) state.SpeedStep = 0;

        PowerEnabled = false;

        Console.WriteLine("[MockCommandStation] Gesamtnothalt aktiviert - Gleisspannung aus");
        OnCommandExecuted(new MockCommandEventArgs("EmergencyStopAll", null));
    }

    public async Task QueryLocoFunctionsStateAsync(int address, IReadOnlyList<int> functionList,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await Task.Delay(50, cancellationToken);

        // Mock: simuliere Zustandsabfrage, indem aktuelle Zustände als Events gesendet werden
        if (_locoStates.TryGetValue(address, out var state))
            OnLocoStateChanged(new LocoStateChangedEventArgs(
                address,
                state.SpeedStep,
                state.Direction,
                null,
                null,
                false));

        // Sende auch Funktionszustände
        foreach (var funcNumber in functionList)
        {
            var key = (address << 8) | funcNumber;
            if (_functionStates.TryGetValue(key, out var isOn))
                OnLocoStateChanged(new LocoStateChangedEventArgs(
                    address,
                    null,
                    VehicleDirection.Undefined,
                    funcNumber,
                    isOn ? LocoDecoderFunctionState.On : LocoDecoderFunctionState.Off,
                    false));
        }

        Console.WriteLine(
            $"[MockCommandStation] QueryDecoderState für Lok {address} mit {functionList.Count} Funktionen");
        OnCommandExecuted(new MockCommandEventArgs("QueryDecoderState",
            new { Address = address, FunctionCount = functionList.Count }));
    }

    public async Task QueryLocoSpeedDirectionAsync(int address, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await Task.Delay(50, cancellationToken);

        // Mock: simuliere Geschwindigkeitsabfrage, indem aktueller Zustand als Event gesendet wird
        if (_locoStates.TryGetValue(address, out var state))
            OnLocoStateChanged(new LocoStateChangedEventArgs(
                address,
                state.SpeedStep,
                state.Direction,
                null,
                null,
                false));

        Console.WriteLine($"[MockCommandStation] QueryLocoSpeed für Lok {address}");
        OnCommandExecuted(new MockCommandEventArgs("QueryLocoSpeed", new { Address = address }));
    }

    public async Task SetAccessoryValueAsync(int address, byte value,
        AccessoryDecoderProtocol protocol, AccessoryFunctionState state,
        int activationTimeMs = 0,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (address < 1 || address > 2048)
            throw new ArgumentException("Zubehördecoder-Adresse muss zwischen 1 und 2048 liegen.", nameof(address));

        if (state is not (AccessoryFunctionState.On or AccessoryFunctionState.Off))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Accessory state must be On or Off.");

        if (!PowerEnabled)
            throw new InvalidOperationException("Gleisspannung ist ausgeschaltet.");

        await Task.Delay(40, cancellationToken);

        _accessoryValues[address] = value;
        _accessoryStates[address] = state;

        Console.WriteLine(
            $"[MockCommandStation] Zubehördecoder {address}, Value=0x{value:X2}, State={state}, Protocol={protocol}, " +
            $"ActivationTimeMs={activationTimeMs}");
        OnCommandExecuted(new MockCommandEventArgs("SetAccessoryValue",
            new
            {
                Address = address,
                Value = value,
                State = state.ToString(),
                Protocol = protocol.ToString(),
                ActivationTimeMs = activationTimeMs
            }));
        AccessoryStateChanged?.Invoke(this,
            new AccessoryStateChangedEventArgs(address, value, state));
    }

    public void Dispose()
    {
        if (IsConnected)
            DisconnectAsync().Wait();
    }

    public event EventHandler<MockCommandEventArgs>? CommandExecuted;

    public async Task ConnectAsync(string address, int port, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Adresse darf nicht leer sein.", nameof(address));

        if (port < 1 || port > 65535)
            throw new ArgumentException("Port muss zwischen 1 und 65535 liegen.", nameof(port));

        await Task.Delay(100, cancellationToken); // Simuliere Verbindungsverzögerung
        IsConnected = true;
        Console.WriteLine($"[MockCommandStation] Verbunden mit {address}:{port}");
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Keine Verbindung zur Kommandozentrale.");
    }

    private void OnCommandExecuted(MockCommandEventArgs args)
    {
        CommandExecuted?.Invoke(this, args);
    }

    private void OnLocoStateChanged(LocoStateChangedEventArgs args)
    {
        LocoStateChanged?.Invoke(this, args);
    }

    private readonly record struct DecoderConfig(LocoDecoderProtocol Protocol, int EffectiveSpeedSteps);

    internal class LocoState
    {
        public int Address { get; set; }
        public int SpeedStep { get; set; }
        public VehicleDirection Direction { get; set; }

        public override string ToString()
        {
            return $"Lok {Address}: Step={SpeedStep}, Dir={Direction}";
        }
    }
}

internal sealed class MockCommandEventArgs : EventArgs
{
    public MockCommandEventArgs(string commandName, object? parameter)
    {
        CommandName = commandName;
        Parameter = parameter;
        Timestamp = DateTime.Now;
    }

    public string CommandName { get; }
    public object? Parameter { get; }
    public DateTime Timestamp { get; }
}