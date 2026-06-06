// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - AccessoryControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// der GNU General Public License, wie von der Free Software Foundation,
// entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// veröffentlichten Version, weiterverbreiten und/oder modifizieren.
//
// Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// Siehe die GNU General Public License für weitere Details.
//
// Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OTD.HardwareControl.CommandStation;

namespace OTD.HardwareControl.Accessory;

/// <summary>
/// Low-level control for one accessory decoder address (turnouts, signals, etc.).
///
/// The command station is assigned explicitly via subscribe/unsubscribe on the instance.
/// In contrast to locomotive decoders, exactly one station can be subscribed at a time
/// because accessories are stationary and fixed to one control domain.
/// </summary>
public class AccessoryDecoder : IAccessoryDecoder
{
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    /// <inheritdoc/>
    public int Address { get; }

    /// <inheritdoc/>
    public DecoderProtocol Protocol { get; }

    /// <inheritdoc/>
    public ICommandStation? SubscribedCommandStation { get; private set; }

    /// <summary>
    /// Creates an accessory decoder instance from XML configuration.
    /// Use <see cref="SubscribeCommandStationAsync"/> to connect a command station.
    /// </summary>
    public AccessoryDecoder(XElement decoderConfiguration)
    {
        ArgumentNullException.ThrowIfNull(decoderConfiguration);

        try
        {
            var protocolValue = decoderConfiguration.Element("protocol")?.Value;
            if (string.IsNullOrWhiteSpace(protocolValue))
                throw new InvalidOperationException("Missing required <protocol> value in accessory decoder configuration.");

            var addressValue = decoderConfiguration.Element("address")?.Value;
            if (string.IsNullOrWhiteSpace(addressValue))
                throw new InvalidOperationException("Missing required <address> value in accessory decoder configuration.");

            Protocol = AccessoryDecoderUtils.GetProtocol(protocolValue);
            Address = AccessoryDecoderUtils.GetAddress(addressValue);
            Console.WriteLine($"Zubehördecoder Adresse {Address}: Protokoll {Protocol}.");
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            Console.WriteLine($"Fehler beim Laden der Zubehördecoder-Konfiguration: {ex.Message}");
            throw new InvalidOperationException("Accessory decoder configuration could not be loaded.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task SubscribeCommandStationAsync(ICommandStation commandStation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandStation);

        if (ReferenceEquals(SubscribedCommandStation, commandStation))
            return;

        if (SubscribedCommandStation is not null)
        {
            throw new InvalidOperationException(
                $"Zubehördecoder {Address}: Es ist bereits eine Zentrale abonniert ({SubscribedCommandStation.GetType().Name}). " +
                "Vor erneutem Subscribe muss zuerst Unsubscribe aufgerufen werden.");
        }

        SubscribedCommandStation = commandStation;
        if (commandStation is CommandStation.CommandStation concreteStation)
            concreteStation.RegisterAccessoryDecoder(Address, this);

        Console.WriteLine(
            $"Zubehördecoder {Address}: Zentrale '{commandStation.GetType().Name}' abonniert. Genau eine Zentrale erlaubt.");

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task UnsubscribeCommandStationAsync(ICommandStation? commandStation)
    {
        if (commandStation is null)
            return Task.CompletedTask;

        if (ReferenceEquals(SubscribedCommandStation, commandStation))
        {
            if (commandStation is CommandStation.CommandStation concreteStation)
                concreteStation.UnregisterAccessoryDecoder(Address, this);

            SubscribedCommandStation = null;
            Console.WriteLine(
                $"Zubehördecoder {Address}: Zentrale '{commandStation.GetType().Name}' abgemeldet. Keine Zentrale abonniert.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task SetFunctionAsync(int outputValue, FunctionState functionState, CancellationToken cancellationToken = default)
    {
        if (Protocol == DecoderProtocol.Dcc)
        {
            if (outputValue is not (0 or 1))
                throw new ArgumentOutOfRangeException(nameof(outputValue), outputValue,
                    "DCC basic requires output value 0 or 1.");
        }

        if (outputValue is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(outputValue), outputValue, "Accessory output value must be in range 0..255.");

        if (functionState is not (FunctionState.On or FunctionState.Off))
            throw new ArgumentOutOfRangeException(nameof(functionState), functionState, "Accessory state must be On or Off.");

        if (SubscribedCommandStation is null)
        {
            Console.WriteLine($"Zubehördecoder {Address}: Keine Zentrale abonniert, Befehl ignoriert.");
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
                    activationTimeMs: 0,
                    cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine(
                $"Zubehördecoder {Address}: OutputValue={(byte)outputValue} FunctionState={functionState} gesendet (Protokoll {Protocol}).");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ActivateFunctionAsync(int outputValue, int timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Activation timeout must be greater than 0 ms.");

        if (Protocol == DecoderProtocol.DccExtended)
        {
            // Zeitgesteuerte Aktivierung für erweiterte Zubehördecoder gemäss RCN-213 (2.2): Bit 7 repräsentiert
            // den anzusteuernden Ausgang (0/1), Bits 0-6 die Aktivierungszeit in 100ms-Schritten.
            if (outputValue is not (0 or 1))
            {
                throw new ArgumentOutOfRangeException(nameof(outputValue), outputValue,
                    "DCC extended timed activation requires output value 0 or 1.");
            }

            if (SubscribedCommandStation is null)
            {
                Console.WriteLine($"Zubehördecoder {Address}: Keine Zentrale abonniert, Befehl ignoriert.");
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
                        FunctionState.On,
                        activationTimeMs: timeout,
                        cancellationToken)
                    .ConfigureAwait(false);

                Console.WriteLine(
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
        await SetFunctionAsync(outputValue, FunctionState.On, cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(timeout), cancellationToken).ConfigureAwait(false);
        await SetFunctionAsync(outputValue, FunctionState.Off, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public event EventHandler<AccessoryStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Called by command stations to push state updates into this decoder instance.
    /// </summary>
    internal void RaiseStateChanged(int outputValue, FunctionState functionState)
    {
        if (outputValue is < 0 or > 255)
            return;

        if (functionState is not (FunctionState.On or FunctionState.Off))
            return;


        StateChanged?.Invoke(this, new AccessoryStateChangedEventArgs(
            Address, outputValue, functionState));
    }
}
