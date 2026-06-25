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
using OTD.HardwareControl.Drivers;

namespace OTD.HardwareControl;

/// <summary>
///     Orchestrates control of a command station.
///     This class encapsulates connection handling, track power validation,
///     and delegates commands to the concrete driver (for example LoDiRektor).
/// </summary>
public sealed class CommandStation : ICommandStation
{
    private const string DriverLoDiRector = "lodi-rector";
    private const string DriverMockCommandStation = "mock-commandstation";

    private readonly ICommandStation _driver;
    private readonly TimeSpan _powerStabilizationDelay = TimeSpan.FromSeconds(1);
    private readonly Dictionary<int, List<AccessoryDecoder>> _registeredAccessoryDecoders = new();
    private readonly Dictionary<int, LocoDecoder> _registeredDecoders = new();

    public CommandStation(Guid stationUid)
    {
        UniqueId = stationUid;
        _driver = CreateDriver(stationUid);

        // Zustands-Readbacks der Zentrale in registrierte Decoderinstanzen durchreichen.
        _driver.LocoStateChanged += (_, args) => OnDecoderStateChanged(args);
        _driver.AccessoryStateChanged += (_, args) => OnAccessoryDecoderStateChanged(args);
    }

    /// <summary>
    ///     Unique ID of the command station from commandstations.xml.
    /// </summary>
    public Guid UniqueId { get; }

    /// <summary>
    ///     Name des verwendeten Treibers (z.B. "LoDiRektor" oder "MockCommandStation").
    /// </summary>
    public string DriverName => _driver.GetType().Name;

    public bool IsConnected => _driver.IsConnected;

    public event EventHandler<LocoStateChangedEventArgs>? LocoStateChanged;

    public event EventHandler<AccessoryStateChangedEventArgs>? AccessoryStateChanged;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return _driver.ConnectAsync(cancellationToken);
    }

    public Task DisconnectAsync()
    {
        return _driver.DisconnectAsync();
    }

    public Task SetPowerAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        return _driver.SetPowerAsync(isOn, cancellationToken);
    }

    public Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default)
    {
        return _driver.GetPowerStateAsync(cancellationToken);
    }

    public void Dispose()
    {
        _driver.Dispose();
    }

    public void InitializeDecoder(int address, LocoDecoderProtocol protocol, int effectiveSpeedSteps)
    {
        _driver.InitializeDecoder(address, protocol, effectiveSpeedSteps);
    }

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
    {
        return _driver.EmergencyStopAsync(address, cancellationToken);
    }

    public Task EmergencyStopAllAsync(CancellationToken cancellationToken = default)
    {
        return _driver.EmergencyStopAllAsync(cancellationToken);
    }

    public Task QueryLocoFunctionsStateAsync(int address, IReadOnlyList<int> functionList,
        CancellationToken cancellationToken = default)
    {
        return _driver.QueryLocoFunctionsStateAsync(address, functionList, cancellationToken);
    }

    public Task QueryLocoSpeedDirectionAsync(int address, CancellationToken cancellationToken = default)
    {
        return _driver.QueryLocoSpeedDirectionAsync(address, cancellationToken);
    }

    public Task SetAccessoryValueAsync(int address, byte value, AccessoryDecoderProtocol protocol,
        AccessoryFunctionState state, int activationTimeMs = 0,
        CancellationToken cancellationToken = default)
    {
        return _driver.SetAccessoryValueAsync(address, value, protocol, state, activationTimeMs, cancellationToken);
    }

    /// <summary>
    ///     Ensures the command station is connected and track power is active.
    /// </summary>
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

    public async Task QueryDecoderFunctionsAsync(int address, IReadOnlyList<int> functionList,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureOperationalAsync(cancellationToken).ConfigureAwait(false))
            return;

        await _driver.QueryLocoFunctionsStateAsync(address, functionList, cancellationToken).ConfigureAwait(false);
    }

    public async Task QueryLocoSpeedAsync(int address, CancellationToken cancellationToken = default)
    {
        if (!await EnsureOperationalAsync(cancellationToken).ConfigureAwait(false))
            return;
        await _driver.QueryLocoSpeedDirectionAsync(address, cancellationToken).ConfigureAwait(false);
    }

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

    private static ICommandStation CreateDriver(Guid stationUid)
    {
        var commandStationElement = CommandStationUtils.LoadCommandStationElement(stationUid);
        var driverName = CommandStationUtils
            .RequireAttribute(commandStationElement, "driver", $"commandstation '{stationUid}'")
            .Trim()
            .ToLowerInvariant();

        return driverName switch
        {
            DriverLoDiRector => new LoDiRektor(commandStationElement),
            DriverMockCommandStation => new MockCommandStation(),
            _ => throw new InvalidOperationException(
                $"Command station '{stationUid}' has unsupported driver '{driverName}'.")
        };
    }


    private void OnDecoderStateChanged(LocoStateChangedEventArgs args)
    {
        LocoStateChanged?.Invoke(this, args);

        if (_registeredDecoders.TryGetValue(args.Address, out var decoder)) decoder.RaiseStateChanged(args);
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