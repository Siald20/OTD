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
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OTD.Common;

namespace OTD.HardwareControl;

/// <summary>
///     Low-level control for one accessory decoder address (turnouts, signals, etc.).
///     The command station is assigned explicitly via subscribe/unsubscribe on the instance.
///     In contrast to locomotive decoders, exactly one station can be subscribed at a time
///     because accessories are stationary and fixed to one control domain.
/// </summary>
public class AccessoryDecoder : IAccessoryDecoder
{
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    /// <summary>
    ///     Creates an accessory decoder instance from XML configuration.
    ///     Use <see cref="SubscribeCommandStationAsync" /> to connect a command station.
    /// </summary>
    public AccessoryDecoder(XElement decoderConfiguration)
    {
        ArgumentNullException.ThrowIfNull(decoderConfiguration);

        try
        {
            var protocolValue = decoderConfiguration.Element("protocol")?.Value;
            if (string.IsNullOrWhiteSpace(protocolValue))
                throw new InvalidOperationException(
                    "Missing required <protocol> value in accessory decoder configuration.");

            var addressValue = decoderConfiguration.Element("address")?.Value;
            if (string.IsNullOrWhiteSpace(addressValue))
                throw new InvalidOperationException(
                    "Missing required <address> value in accessory decoder configuration.");

            Protocol = AccessoryDecoderUtils.GetProtocol(protocolValue);
            Address = AccessoryDecoderUtils.GetAddress(addressValue);
            Logging.Debug<AccessoryDecoder>($"Zubehördecoder Adresse {Address}: Protokoll {Protocol}.");
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            Logging.Error<AccessoryDecoder>(
                $"Fehler beim Laden der Zubehördecoder-Konfiguration: {ex.Message}", ex);
            throw new InvalidOperationException("Accessory decoder configuration could not be loaded.", ex);
        }
    }

    /// <inheritdoc />
    public int Address { get; }

    /// <inheritdoc />
    public AccessoryDecoderProtocol Protocol { get; }

    /// <inheritdoc />
    public ICommandStation? SubscribedCommandStation { get; private set; }

    /// <inheritdoc />
    public async Task SubscribeCommandStationAsync(ICommandStation commandStation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandStation);

        if (ReferenceEquals(SubscribedCommandStation, commandStation))
            return;

        if (SubscribedCommandStation is not null)
            throw new InvalidOperationException(
                $"Zubehördecoder {Address}: Es ist bereits eine Zentrale abonniert ({SubscribedCommandStation.GetType().Name}). " +
                "Vor erneutem Subscribe muss zuerst Unsubscribe aufgerufen werden.");

        SubscribedCommandStation = commandStation;
        if (commandStation is CommandStation concreteStation)
            concreteStation.RegisterAccessoryDecoder(Address, this);

        Logging.Debug<AccessoryDecoder>(
            $"Zubehördecoder {Address}: Zentrale '{commandStation.GetType().Name}' abonniert. Genau eine Zentrale erlaubt.");

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UnsubscribeCommandStationAsync(ICommandStation? commandStation)
    {
        if (commandStation is null)
            return Task.CompletedTask;

        if (ReferenceEquals(SubscribedCommandStation, commandStation))
        {
            if (commandStation is CommandStation concreteStation)
                concreteStation.UnregisterAccessoryDecoder(Address, this);

            SubscribedCommandStation = null;
            Logging.Debug<AccessoryDecoder>(
                $"Zubehördecoder {Address}: Zentrale '{commandStation.GetType().Name}' abgemeldet. Keine Zentrale abonniert.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SetFunctionAsync(int outputValue, AccessoryFunctionState functionState,
        CancellationToken cancellationToken = default)
    {
        if (Protocol == AccessoryDecoderProtocol.Dcc)
            if (outputValue is not (0 or 1))
                throw new ArgumentOutOfRangeException(nameof(outputValue), outputValue,
                    "DCC basic requires output value 0 or 1.");

        if (outputValue is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(outputValue), outputValue,
                "Accessory output value must be in range 0..255.");

        if (functionState is not (AccessoryFunctionState.On or AccessoryFunctionState.Off))
            throw new ArgumentOutOfRangeException(nameof(functionState), functionState,
                "Accessory state must be On or Off.");

        if (SubscribedCommandStation is null)
        {
            Logging.Warning<AccessoryDecoder>(
                $"Zubehördecoder {Address}: Keine Zentrale abonniert, Befehl ignoriert.");
            return;
        }

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SubscribedCommandStation
                .SetAccessoryValueAsync(
                    Address,
                    (byte)outputValue,
                    Protocol,
                    functionState,
                    0,
                    cancellationToken)
                .ConfigureAwait(false);

            Logging.Debug<AccessoryDecoder>(
                $"Zubehördecoder {Address}: OutputValue={(byte)outputValue} FunctionState={functionState} gesendet (Protokoll {Protocol}).");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ActivateFunctionAsync(int outputValue, int timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                "Activation timeout must be greater than 0 ms.");

        if (Protocol == AccessoryDecoderProtocol.DccExtended)
        {
            // Zeitgesteuerte Aktivierung für erweiterte Zubehördecoder gemäss RCN-213 (2.2): Bit 7 repräsentiert
            // den anzusteuernden Ausgang (0/1), Bits 0-6 die Aktivierungszeit in 100ms-Schritten.
            if (outputValue is not (0 or 1))
                throw new ArgumentOutOfRangeException(nameof(outputValue), outputValue,
                    "DCC extended timed activation requires output value 0 or 1.");

            if (SubscribedCommandStation is null)
            {
                Logging.Warning<AccessoryDecoder>(
                    $"Zubehördecoder {Address}: Keine Zentrale abonniert, Befehl ignoriert.");
                return;
            }

            await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SubscribedCommandStation
                    .SetAccessoryValueAsync(
                        Address,
                        (byte)outputValue,
                        Protocol,
                        AccessoryFunctionState.On,
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);

                Logging.Debug<AccessoryDecoder>(
                    $"Zubehördecoder {Address}: OutputValue={(byte)outputValue} hardware-timed mit {timeout} ms gesendet (Protokoll {Protocol}).");
            }
            finally
            {
                _commandLock.Release();
            }

            return;
        }

        // Zeitgesteuerte Aktivierung für einfache Zubehördecoder gemäss RCN-213 (2.1): Der gewählte Ausgang (0/1)
        // wird softwaregesteuert mit separatem Bit aktiviert und nach dem Timeout deaktiviert.
        await SetFunctionAsync(outputValue, AccessoryFunctionState.On, cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(timeout), cancellationToken).ConfigureAwait(false);
        await SetFunctionAsync(outputValue, AccessoryFunctionState.Off, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public event EventHandler<AccessoryStateChangedEventArgs>? StateChanged;

    /// <summary>
    ///     Called by command stations to push state updates into this decoder instance.
    /// </summary>
    internal void RaiseStateChanged(int outputValue, AccessoryFunctionState functionState)
    {
        if (outputValue is < 0 or > 255)
            return;

        if (functionState is not (AccessoryFunctionState.On or AccessoryFunctionState.Off))
            return;

        StateChanged?.Invoke(this, new AccessoryStateChangedEventArgs(
            Address, outputValue, functionState));
    }
}