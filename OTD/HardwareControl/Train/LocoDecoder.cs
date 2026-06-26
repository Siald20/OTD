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

namespace OTD.HardwareControl;

/// <summary>
///     Provides low-level locomotive decoder control.
/// </summary>
public class LocoDecoder : ILocoDecoder
{
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly Dictionary<int, LocoDecoderFunctionState> _functionStates = [];
    private readonly List<ICommandStation> _subscribedCommandStations = [];

    /// <summary>
    ///     Creates a decoder instance from the given decoder XML configuration element.
    ///     To complete setup, subscribe one or more command stations via <see cref="SubscribeCommandStationAsync" />.
    /// </summary>
    /// <param name="decoderConfiguration">The <c>&lt;decoder&gt;</c> XML element from the vehicle configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="decoderConfiguration" /> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if required configuration elements are missing or invalid.</exception>
    public LocoDecoder(XElement decoderConfiguration)
    {
        ArgumentNullException.ThrowIfNull(decoderConfiguration);

        try
        {
            var protocolValue = decoderConfiguration.Element("protocol")?.Value;
            if (string.IsNullOrWhiteSpace(protocolValue))
                throw new InvalidOperationException("Missing required <protocol> value in decoder configuration.");

            var speedStepsValue = decoderConfiguration.Element("speedsteps")?.Value;
            if (string.IsNullOrWhiteSpace(speedStepsValue))
                throw new InvalidOperationException("Missing required <speedsteps> value in decoder configuration.");

            var addressValue = decoderConfiguration.Element("address")?.Value;
            if (string.IsNullOrWhiteSpace(addressValue))
                throw new InvalidOperationException("Missing required <address> value in decoder configuration.");

            Protocol = LocoDecoderUtils.GetProtocol(protocolValue);
            TotalSpeedSteps = LocoDecoderUtils.GetSpeedSteps(speedStepsValue);
            Address = LocoDecoderUtils.GetAddress(addressValue);
            // ToDo: Prüfen, ob Betrieb ohne konfigurierte Funktionen geht.
            Functions = LocoDecoderUtils.GetFunctions(decoderConfiguration.Element("functiontable") ??
                                                      new XElement("functiontable"));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            Logging.Error(LogCategory.Train, $"Fehler beim Laden der LocoDecoder-Konfiguration: {ex.Message}", ex);
            throw new InvalidOperationException("LocoDecoder configuration could not be loaded.", ex);
        }
    }

    /// <inheritdoc />
    public int Address { get; }

    /// <inheritdoc />
    public LocoDecoderProtocol Protocol { get; }

    /// <inheritdoc />
    public VehicleDirection Direction { get; private set; } = VehicleDirection.Undefined;

    /// <inheritdoc />
    public int TotalSpeedSteps { get; }

    /// <inheritdoc />
    public int SpeedStep { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<VehicleFunctions> Functions { get; }

    /// <inheritdoc />
    public async Task SubscribeCommandStationAsync(ICommandStation commandStation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandStation);

        if (!_subscribedCommandStations.Contains(commandStation))
        {
            _subscribedCommandStations.Add(commandStation);
            // LocoDecoder initialisieren (Adresse, Protokoll, Anzahl Fahrstufen)
            commandStation.InitializeDecoder(Address, Protocol, TotalSpeedSteps);
            // Callback für Statusmeldungen (Fahrbefehle und Funktionen) von der Zentrale registrieren
            if (commandStation is CommandStation concreteStation)
                concreteStation.RegisterDecoder(Address, this);
            Logging.Info(LogCategory.Train,
                $"Zentrale '{commandStation.GetType().Name}' abonniert. Insgesamt {_subscribedCommandStations.Count} abonniert.");

            try
            {
                // // Geschwindigkeit und Fahrtrichtung abfragen (Antworten werden über Callback verarbeitet)
                Logging.Debug(LogCategory.Train,
                    $"Frage aktuelle Geschwindigkeit von der Zentrale ab (Adresse {Address})...");
                await commandStation.QueryLocoSpeedDirectionAsync(Address, cancellationToken).ConfigureAwait(false);

                // // alle für das Fahrzeug konfigurierten Funktionen abfragen (Antworten werden über Callback verarbeitet)
                // var functionNumbers = Functions
                //     .Select(f => f.Number)
                //     .Distinct()
                //     .ToList();

                // if (functionNumbers.Count > 0)
                //     await commandStation.QueryDecoderFunctionsAsync(Address, functionNumbers).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Error(LogCategory.Train,
                    $"Fehler beim Abfragen des LocoDecoder-Status von der Zentrale: {ex.Message}", ex);
            }
        }
    }

    /// <inheritdoc />
    public Task UnsubscribeCommandStationAsync(ICommandStation? commandStation,
        CancellationToken cancellationToken = default)
    {
        if (commandStation is null)
            return Task.CompletedTask;

        if (_subscribedCommandStations.Remove(commandStation))
        {
            // Registrierung der Status-Callbacks beenden
            if (commandStation is CommandStation concreteStation)
                concreteStation.UnregisterDecoder(Address);
            Logging.Info(LogCategory.Train,
                $"Zentrale '{commandStation.GetType().Name}' abgemeldet. Noch {_subscribedCommandStations.Count} abonniert.");
        }

        return Task.CompletedTask;
    }


    /// <inheritdoc />
    public IReadOnlyList<ICommandStation> SubscribedCommandStations => _subscribedCommandStations;

    /// <inheritdoc />
    public async Task SetFunctionStateAsync(
        int function,
        LocoDecoderFunctionState state,
        CancellationToken cancellationToken = default)
    {
        if (state is LocoDecoderFunctionState.Undefined)
            throw new ArgumentOutOfRangeException(nameof(state), state,
                "FunctionState.Undefined: kein gültiger Wert zum Schalten.");

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Send function command to all subscribed command stations
            foreach (var station in _subscribedCommandStations)
                await station.SetLocoFunctionAsync(Address, function, state == LocoDecoderFunctionState.On,
                        cancellationToken)
                    .ConfigureAwait(false);
            _functionStates[function] = state;
            Logging.Debug(LogCategory.Train,
                $"Funktion {function} {(state == LocoDecoderFunctionState.On ? "AN" : "AUS")} an Adresse {Address} gesendet.");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <inheritdoc />
    public LocoDecoderFunctionState GetFunctionState(int functionNumber)
    {
        return _functionStates.GetValueOrDefault(functionNumber, LocoDecoderFunctionState.Undefined);
    }

    /// <inheritdoc />
    public async Task ActivateFunctionAsync(
        int function,
        int timeout,
        CancellationToken cancellationToken = default)
    {
        await SetFunctionStateAsync(function, LocoDecoderFunctionState.On, cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(timeout), cancellationToken).ConfigureAwait(false);
        await SetFunctionStateAsync(function, LocoDecoderFunctionState.Off, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public event EventHandler<LocoStateChangedEventArgs>? StateChanged;

    /// <summary>
    ///     Sends the specified speed step and direction to all subscribed command stations.
    /// </summary>
    /// <param name="direction">Target decoder direction.</param>
    /// <param name="speedStep">Target speed step (0 = halt).</param>
    /// <param name="forceSend">Reserved for future use; currently has no effect.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    protected internal async Task SetSpeedStepAsync(
        VehicleDirection direction,
        int speedStep,
        bool forceSend = false,
        CancellationToken cancellationToken = default)
    {
        _ = forceSend; // für spätere Verwendung, z.B. um auch bei unverändertem SpeedStep erneut zu senden

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Send drive command to all subscribed command stations.
            foreach (var station in _subscribedCommandStations)
                await station.SetLocoSpeedAsync(Address, speedStep, direction, cancellationToken).ConfigureAwait(false);
            Logging.Debug(LogCategory.Train,
                $"Fahrbefehl {direction} mit SpeedStep {speedStep} an Adresse {Address} gesendet.");

            Direction = direction;
            SpeedStep = speedStep;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    ///     Sends emergency stop to all subscribed command stations.
    /// </summary>
    protected internal async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Send an emergency stop to all subscribed command stations
            foreach (var station in _subscribedCommandStations)
                await station.EmergencyStopAsync(Address, cancellationToken).ConfigureAwait(false);

            SpeedStep = 0;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    ///     Called by the command station to report drive and function command updates.
    /// </summary>
    internal void RaiseStateChanged(LocoStateChangedEventArgs args)
    {
        if (args.SpeedStep.HasValue)
        {
            SpeedStep = args.SpeedStep.Value;
            Direction = args.Direction;
        }

        if (args is { FunctionNumber: not null, FunctionStateValue: not null })
            _functionStates[args.FunctionNumber.Value] = args.FunctionStateValue.Value;

        StateChanged?.Invoke(this, args);
    }
}