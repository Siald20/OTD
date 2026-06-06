// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <info@batec.net>
// //
// // Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// // der GNU General Public License, wie von der Free Software Foundation,
// // entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// // veröffentlichten Version, weiterverbreiten und/oder modifizieren.
// //
// // Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// // jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// // MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// // Siehe die GNU General Public License für weitere Details.
// //
// // Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// // Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OTD.HardwareControl.CommandStation;

namespace OTD.HardwareControl.Train;

/// <summary>
/// Provides low-level locomotive decoder control.
/// </summary>
public class LocoDecoder : ILocoDecoder
{
    private readonly List<ICommandStation> _subscribedCommandStations = [];
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly Dictionary<int, FunctionState> _functionStates = [];

    /// <inheritdoc/>
    public int Address { get; }

    /// <inheritdoc/>
    public DecoderProtocol Protocol { get; }

    /// <inheritdoc/>
    public VehicleDirection Direction { get; private set; } = VehicleDirection.Undefined;

    /// <inheritdoc/>
    public int TotalSpeedSteps { get; }

    /// <inheritdoc/>
    public int SpeedStep { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<VehicleFunctions> Functions { get; }

    /// <summary>
    /// Creates a decoder instance from the given decoder XML configuration element.
    /// To complete setup, subscribe one or more command stations via <see cref="SubscribeCommandStationAsync"/>.
    /// </summary>
    /// <param name="decoderConfiguration">The <c>&lt;decoder&gt;</c> XML element from the vehicle configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="decoderConfiguration"/> is null.</exception>
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
            Functions = LocoDecoderUtils.GetFunctions(decoderConfiguration.Element("functiontable") ?? new XElement("functiontable"));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            Console.WriteLine($"Fehler beim Laden der LocoDecoder-Konfiguration: {ex.Message}");
            throw new InvalidOperationException("LocoDecoder configuration could not be loaded.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task SubscribeCommandStationAsync(ICommandStation commandStation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandStation);

        if (!_subscribedCommandStations.Contains(commandStation))
        {
            _subscribedCommandStations.Add(commandStation);
            // LocoDecoder initialisieren (Adresse, Protokoll, Anzahl Fahrstufen)
            commandStation.InitializeDecoder(Address, Protocol, TotalSpeedSteps);
            // Callback für Statusmeldungen (Fahrbefehle und Funktionen) von der Zentrale registrieren
            if (commandStation is CommandStation.CommandStation concreteStation)
                concreteStation.RegisterDecoder(Address, this);
            Console.WriteLine($"Zentrale '{commandStation.GetType().Name}' abonniert. Insgesamt {_subscribedCommandStations.Count} abonniert.");

            try
            {

                // // Geschwindigkeit und Fahrtrichtung abfragen (Antworten werden über Callback verarbeitet)
                Console.WriteLine($"Frage aktuelle Geschwindigkeit von der Zentrale ab (Adresse {Address})...");
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
                Console.WriteLine($"Fehler beim Abfragen des LocoDecoder-Status von der Zentrale: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public Task UnsubscribeCommandStationAsync(ICommandStation? commandStation, CancellationToken cancellationToken = default)
    {
        if (commandStation is null)
            return Task.CompletedTask;

        if (_subscribedCommandStations.Remove(commandStation))
        {
            // Registrierung der Status-Callbacks beenden
            if (commandStation is CommandStation.CommandStation concreteStation)
                concreteStation.UnregisterDecoder(Address);
            Console.WriteLine($"Zentrale '{commandStation.GetType().Name}' abgemeldet. Noch {_subscribedCommandStations.Count} abonniert.");
        }

        return Task.CompletedTask;
    }


    /// <inheritdoc/>
    public IReadOnlyList<ICommandStation> SubscribedCommandStations => _subscribedCommandStations;

    /// <summary>
    /// Sends the specified speed step and direction to all subscribed command stations.
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
            {
                await station.SetLocoSpeedAsync(Address, speedStep, direction, cancellationToken).ConfigureAwait(false);
            }
            Console.WriteLine($"Fahrbefehl {direction} mit SpeedStep {speedStep} an Adresse {Address} gesendet.");

            Direction = direction;
            SpeedStep = speedStep;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Sends emergency stop to all subscribed command stations.
    /// </summary>
    protected internal async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Send an emergency stop to all subscribed command stations
            foreach (var station in _subscribedCommandStations)
            {
                await station.EmergencyStopAsync(Address, cancellationToken).ConfigureAwait(false);
            }

            SpeedStep = 0;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SetFunctionStateAsync(
        int function,
        FunctionState state,
        CancellationToken cancellationToken = default)
    {
        if (state is FunctionState.Undefined)
            throw new ArgumentOutOfRangeException(nameof(state), state,
                "FunctionState.Undefined: kein gültiger Wert zum Schalten.");

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Send function command to all subscribed command stations
            foreach (var station in _subscribedCommandStations)
            {
                await station.SetLocoFunctionAsync(Address, function, state == FunctionState.On,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            _functionStates[function] = state;
            Console.WriteLine($"Funktion {function} {(state == FunctionState.On ? "AN" : "AUS")} an Adresse {Address} gesendet.");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <inheritdoc/>
    public FunctionState GetFunctionState(int functionNumber)
        => _functionStates.GetValueOrDefault(functionNumber, FunctionState.Undefined);

    /// <inheritdoc/>
    public async Task ActivateFunctionAsync(
        int function,
        int timeout,
        CancellationToken cancellationToken = default)
    {
        await SetFunctionStateAsync(function, FunctionState.On, cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(timeout), cancellationToken).ConfigureAwait(false);
        await SetFunctionStateAsync(function, FunctionState.Off, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public event EventHandler<LocoStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Called by the command station to report drive and function command updates.
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
