// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl.CommandStation.LoDi;
using OTD.HardwareControl.Train;
using AccessoryDecoder = OTD.HardwareControl.Accessory.AccessoryDecoder;
using AccessoryDecoderProtocol = OTD.HardwareControl.Accessory.DecoderProtocol;
using AccessoryFunctionState = OTD.HardwareControl.Accessory.FunctionState;
using AccessoryStateChangedEventArgs = OTD.HardwareControl.Accessory.AccessoryStateChangedEventArgs;

namespace OTD.HardwareControl.CommandStation;

/// <summary>
///     Orchestriert die Ansteuerung einer Kommandozentrale.
///     Diese Klasse kapselt Verbindungsaufbau, Gleisspannungspruefung
///     und delegiert Befehle an den konkreten Treiber (z.B. LoDiRektor).
/// </summary>
public sealed class CommandStation : ICommandStation
{
    private readonly ICommandStation _driver;
    private readonly TimeSpan _powerStabilizationDelay = TimeSpan.FromSeconds(1);
    private readonly Dictionary<int, LocoDecoder> _registeredDecoders = new();
    private readonly Dictionary<int, List<AccessoryDecoder>> _registeredAccessoryDecoders = new();

    public CommandStation(ICommandStation driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;

        // Zustands-Readbacks der Zentrale in registrierte Decoderinstanzen durchreichen.
        _driver.LocoStateChanged += (_, args) => OnDecoderStateChanged(args);
        _driver.AccessoryStateChanged += (_, args) => OnAccessoryDecoderStateChanged(args);
    }

    public bool IsConnected => _driver.IsConnected;

    /// <summary>
    /// Name des verwendeten Treibers (z.B. "LoDiRektor" oder "MockCommandStation").
    /// </summary>
    public string DriverName => _driver.GetType().Name;

    public event EventHandler<LocoStateChangedEventArgs>? LocoStateChanged;

    public event EventHandler<AccessoryStateChangedEventArgs>? AccessoryStateChanged;

    public Task ConnectAsync(string address, int port, CancellationToken cancellationToken = default)
        => _driver.ConnectAsync(address, port, cancellationToken);

    public Task DisconnectAsync() => _driver.DisconnectAsync();

    public Task SetPowerAsync(bool isOn, CancellationToken cancellationToken = default)
        => _driver.SetPowerAsync(isOn, cancellationToken);

    public Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default)
        => _driver.GetPowerStateAsync(cancellationToken);

    public void Dispose() => _driver.Dispose();

    public async Task<bool> EnsureOperationalAsync(CancellationToken cancellationToken = default)
    {
        if (!_driver.IsConnected)
        {
            Console.WriteLine("Keine Verbindung zur Kommandozentrale.");
            return false;
        }

        bool currentPowerState;
        try
        {
            currentPowerState = await _driver.GetPowerStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Abfragen der Gleisspannung: {ex.Message}");
            return false;
        }

        if (currentPowerState)
            return true;

        Console.WriteLine("Gleisspannung ist ausgeschaltet. Aktiviere Booster...");
        try
        {
            await _driver.SetPowerAsync(true, cancellationToken).ConfigureAwait(false);
            Console.WriteLine("Gleisspannung eingeschaltet. Warte 1 s auf Stabilisierung...");
            await Task.Delay(_powerStabilizationDelay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Einschalten der Gleisspannung: {ex.Message}");
            return false;
        }
    }

    public void InitializeDecoder(int address, OTD.HardwareControl.Train.DecoderProtocol protocol, int effectiveSpeedSteps)
        => _driver.InitializeDecoder(address, protocol, effectiveSpeedSteps);

    public async Task SetLocoSpeedAsync(int address, int speedStep, VehicleDirection direction,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureOperationalAsync(cancellationToken).ConfigureAwait(false))
            return;

        await _driver.SetLocoSpeedAsync(address, speedStep, direction, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetLocoFunctionAsync(int address, int functionNumber, bool isOn,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureOperationalAsync(cancellationToken).ConfigureAwait(false))
            return;

        await _driver.SetLocoFunctionAsync(address, functionNumber, isOn, cancellationToken).ConfigureAwait(false);
    }

    public Task EmergencyStopAsync(int address, CancellationToken cancellationToken = default)
        => _driver.EmergencyStopAsync(address, cancellationToken);

    public Task EmergencyStopAllAsync(CancellationToken cancellationToken = default)
        => _driver.EmergencyStopAllAsync(cancellationToken);

    public async Task QueryDecoderFunctionsAsync(int address, IReadOnlyList<int> functionList,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureOperationalAsync(cancellationToken).ConfigureAwait(false))
            return;

        await _driver.QueryLocoFunctionsStateAsync(address, functionList, cancellationToken).ConfigureAwait(false);
    }

    public Task QueryLocoFunctionsStateAsync(int address, IReadOnlyList<int> functionList,
        CancellationToken cancellationToken = default)
        => _driver.QueryLocoFunctionsStateAsync(address, functionList, cancellationToken);

    public async Task QueryLocoSpeedAsync(int address, CancellationToken cancellationToken = default)
    {
        if (!await EnsureOperationalAsync(cancellationToken).ConfigureAwait(false))
            return;
        await _driver.QueryLocoSpeedDirectionAsync(address, cancellationToken).ConfigureAwait(false);
    }

    public Task QueryLocoSpeedDirectionAsync(int address, CancellationToken cancellationToken = default)
        => _driver.QueryLocoSpeedDirectionAsync(address, cancellationToken);

    public void RegisterDecoder(int address, LocoDecoder locoDecoder)
    {
        _registeredDecoders[address] = locoDecoder;
    }

    public void UnregisterDecoder(int address)
    {
        _registeredDecoders.Remove(address);
    }

    public void RegisterAccessoryDecoder(int address, AccessoryDecoder accessoryDecoder)
    {
        ArgumentNullException.ThrowIfNull(accessoryDecoder);

        if (!_registeredAccessoryDecoders.TryGetValue(address, out var decoders))
        {
            decoders = new List<AccessoryDecoder>();
            _registeredAccessoryDecoders[address] = decoders;
        }

        if (decoders.All(d => !ReferenceEquals(d, accessoryDecoder)))
            decoders.Add(accessoryDecoder);
    }

    public void UnregisterAccessoryDecoder(int address, AccessoryDecoder accessoryDecoder)
    {
        if (!_registeredAccessoryDecoders.TryGetValue(address, out var decoders))
            return;

        decoders.RemoveAll(d => ReferenceEquals(d, accessoryDecoder));
        if (decoders.Count == 0)
            _registeredAccessoryDecoders.Remove(address);
    }

    public Task SetAccessoryValueAsync(int address, byte value, AccessoryDecoderProtocol protocol,
        AccessoryFunctionState state, int activationTimeMs = 0,
        CancellationToken cancellationToken = default)
        => _driver.SetAccessoryValueAsync(address, value, protocol, state, activationTimeMs, cancellationToken);


    private void OnDecoderStateChanged(LocoStateChangedEventArgs args)
    {
        LocoStateChanged?.Invoke(this, args);

        if (_registeredDecoders.TryGetValue(args.Address, out var decoder))
        {
            decoder.RaiseStateChanged(args);
        }
    }

    private void OnAccessoryDecoderStateChanged(AccessoryStateChangedEventArgs args)
    {
        AccessoryStateChanged?.Invoke(this, args);

        if (!_registeredAccessoryDecoders.TryGetValue(args.Address, out var decoders))
            return;

        foreach (var decoder in decoders)
            decoder.RaiseStateChanged(args.OutputValue, args.State);
    }
}
