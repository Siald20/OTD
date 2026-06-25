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
using System.Xml.Linq;

namespace OTD.HardwareControl;

/// <summary>
///     Represents an accessory item (turnout, signal etc.) from <c>accessories.xml</c>.
///     An accessory item contains all possible function states (turnout and signal positions).
///     Each function state defines how the involved decoders should be driven.
/// </summary>
public class Accessory : IDisposable
{
    private const int DeferredReadBackEvaluationDelayPerAddressMs = 1000; // ToDo: Konfigurierbar machen?
    private readonly List<IAccessoryDecoder> _decoders = [];
    private readonly Dictionary<int, int> _lastReadBackValuesByAddress = new();
    private readonly Lock _readBackSync = new();
    private readonly List<AccessoryStateDefinition> _states = [];
    private readonly Dictionary<string, AccessoryStateDefinition> _statesById = new(StringComparer.OrdinalIgnoreCase);
    private int _readBackEvaluationGeneration;

    /// <summary>
    ///     Creates an accessory instance and binds it to the provided command station.
    /// </summary>
    /// <param name="accessoryId">UID of the accessory item to load.</param>
    /// <param name="commandStation">Command station this accessory is permanently bound to.</param>
    public Accessory(Guid accessoryId, ICommandStation commandStation)
    {
        ArgumentNullException.ThrowIfNull(commandStation);

        AccessoryId = accessoryId;

        try
        {
            AccessoryConfig = AccessoryUtils.GetAccessoryConfiguration(accessoryId);

            var typeAttribute = AccessoryConfig.Attribute("type")
                                ?? throw new InvalidOperationException(
                                    $"Missing required attribute 'type' in accessory '{accessoryId}'.");
            var subtypeAttribute = AccessoryConfig.Attribute("subtype")
                                   ?? throw new InvalidOperationException(
                                       $"Missing required attribute 'subtype' in accessory '{accessoryId}'.");
            var nameAttribute = AccessoryConfig.Attribute("name")
                                ?? throw new InvalidOperationException(
                                    $"Missing required attribute 'id' in accessory '{accessoryId}'.");
            var interlockingAttribute = AccessoryConfig.Attribute("interlocking");
            var decoderProtocolElement = AccessoryConfig.Element("decoder")?.Element("protocol");

            Type = AccessoryUtils.GetAccessoryType(typeAttribute.Value);
            Subtype = subtypeAttribute.Value.Trim();
            Name = nameAttribute.Value.Trim();
            Interlocking = interlockingAttribute is null ? string.Empty : interlockingAttribute.Value.Trim();
            Protocol = AccessoryUtils.GetDecoderProtocol(decoderProtocolElement?.Value);

            ActivationTime = AccessoryUtils.GetActivationTime(AccessoryConfig);
            DelayTime = AccessoryUtils.GetDelayTime(AccessoryConfig);

            var stateElements = AccessoryUtils.GetStateElements(AccessoryConfig, accessoryId);

            AccessoryStateUtils.ParseAccessoryStates(stateElements, accessoryId, Type, Subtype, Name, Interlocking,
                Protocol, _states, _statesById);
            InitializeDecoders();
            SubscribeCommandStation(commandStation);

            CurrentState = null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            Console.WriteLine($"Fehler beim Laden der Zubehör-Konfiguration '{accessoryId}': {ex.Message}");
            throw new InvalidOperationException($"Accessory configuration could not be loaded for '{accessoryId}'.",
                ex);
        }
    }

    /// <summary>
    ///     Unique identifier of this accessory.
    /// </summary>
    public Guid AccessoryId { get; }

    // ToDo: Property muss vermutlich nicht exponiert werden -> in Klassen-Variable umwandeln.
    /// <summary>
    ///     Raw XML configuration element loaded from <c>accessories.xml</c>.
    /// </summary>
    public XElement? AccessoryConfig { get; }

    /// <summary>
    ///     Accessory type from attribute <c>type</c>.
    /// </summary>
    public AccessoryType Type { get; }

    /// <summary>
    ///     Accessory subtype from attribute <c>subtype</c> (free text).
    /// </summary>
    public string Subtype { get; }

    /// <summary>
    ///     Accessory Name (turnout name, signal name etc.).
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Interlocking / signal box identifier to which the accessory belongs.
    /// </summary>
    public string Interlocking { get; }

    /// <summary>
    ///     Accessory decoder protocol.
    /// </summary>
    public AccessoryDecoderProtocol Protocol { get; }

    /// <summary>
    ///     Current state of the accessory (turnout or signal positions etc.). />.
    /// </summary>
    public string? CurrentState { get; private set; }

    /// <summary>
    ///     Activation time in milliseconds for magnetic accessories (e.g. turnout coils).
    ///     For DCC basic this triggers software-based pulse activation with auto-off.
    ///     For DCC extended this value is encoded into the data byte (bits 0..6).
    ///     Read from &lt;decoder&gt;&lt;activationtime&gt; in <c>accessories.xml</c>.
    ///     Default is 0 (no timed activation).
    /// </summary>
    public int ActivationTime { get; }

    /// <summary>
    ///     Delay time in milliseconds between consecutive decoder address changes.
    ///     When multiple decoder addresses are present and need to be switched sequentially,
    ///     this delay prevents the decoder from being overwhelmed by simultaneous commands.
    ///     This is particularly important for accessories like magnetic coil turnouts that
    ///     cannot handle multiple simultaneous commands. Read from &lt;decoder&gt;&lt;delaytime&gt;
    ///     in <c>accessories.xml</c>. Default is 0 (no delay).
    /// </summary>
    public int DelayTime { get; }

    /// <summary>
    ///     All possible states of this accessory (as defined in <c>accessories.xml</c>).
    /// </summary>
    public IReadOnlyList<AccessoryStateDefinition> States => _states;

    /// <summary>
    ///     All parsed decoder commands in flat form.
    /// </summary>
    public IReadOnlyList<AccessoryStateCommand> StateCommands => _states
        .SelectMany(state => state.Commands)
        .ToList();

    /// <summary>
    ///     Physical decoder instances used by this accessory.
    /// </summary>
    public IReadOnlyList<IAccessoryDecoder> Decoders => _decoders;

    /// <summary>
    ///     The subscribed command station (representative view from the first decoder).
    ///     For multi-decoder accessories every decoder has the same station binding.
    /// </summary>
    public ICommandStation? SubscribedCommandStation =>
        _decoders.Count > 0
            ? _decoders[0].SubscribedCommandStation
            : null;

    /// <summary>
    ///     Releases the command-station binding of all underlying decoders.
    /// </summary>
    public void Dispose()
    {
        var commandStation = SubscribedCommandStation;
        if (commandStation is null)
            return;

        foreach (var decoder in _decoders)
            decoder.UnsubscribeCommandStationAsync(commandStation).GetAwaiter().GetResult();

        Console.WriteLine(
            $"Zubehör {Type} {Name}: Zentrale '{commandStation.GetType().Name}' beim Dispose abgemeldet.");
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Raised when one of the underlying decoder instances reports a state change.
    /// </summary>
    public event EventHandler<AccessoryStateChangedEventArgs>? StateChanged;

    /// <summary>
    ///     Sets the accessory to a configured state.
    /// </summary>
    public async Task SetStateAsync(string stateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stateId))
            throw new ArgumentException("State id must not be empty.", nameof(stateId));

        if (!_statesById.TryGetValue(stateId.Trim(), out var state))
            throw new ArgumentOutOfRangeException(nameof(stateId), stateId, $"Unknown accessory state '{stateId}'.");

        // nichts unternehmen, falls bereits im gewünschten Schaltzustand
        if (string.Equals(CurrentState, state.State, StringComparison.OrdinalIgnoreCase))
            return;

        Console.WriteLine(
            $"Zubehör {Type} {Name}: Setze Zustand '{state.State}'{(string.IsNullOrWhiteSpace(state.Description) ? string.Empty : $" ({state.Description})")}");

        var validatedCommands = state.Commands
            .Select(command =>
                AccessoryStateUtils.ValidateCommandMetadata(AccessoryId, state, command, Type, Subtype, Name,
                    Interlocking))
            .ToList();

        var duplicateAddress = validatedCommands
            .GroupBy(command => command.Address)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateAddress is not null)
            throw new InvalidOperationException(
                $"Accessory '{AccessoryId}' contains duplicate decoder address {duplicateAddress.Key} " +
                $"within state '{state.State}'. Each decoder address must appear only once per state.");

        var decodersByAddress = _decoders.ToDictionary(d => d.Address);

        for (var i = 0; i < validatedCommands.Count; i++)
        {
            // Verzögerung beim Schalten bei mehreren anzusteuernden Decoder-Adressen, um Überlastungen am Modul zu verhindern
            if (i > 0 && DelayTime > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(DelayTime), cancellationToken).ConfigureAwait(false);

            var command = validatedCommands[i];
            if (!decodersByAddress.TryGetValue(command.Address, out var decoder))
                continue;

            if (ActivationTime > 0)
            {
                // Zeitgesteuerte Aktivierung
                await decoder.ActivateFunctionAsync(command.OutputValue, ActivationTime, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                // Keine zeitgesteuerte Aktivierung:
                // - DCC basic: explizites Flip-Flop in Accessory (0/1), damit nur ein Ausgang aktiv ist.
                // - Andere Protokolle: gewählter Ausgang wird normal gesetzt.
                if (Protocol == AccessoryDecoderProtocol.Dcc)
                {
                    if (command.OutputValue is not (0 or 1))
                        throw new InvalidOperationException(
                            $"Accessory '{AccessoryId}' state '{state.State}' uses invalid output value {command.OutputValue} for DCC basic. Allowed values are 0 or 1.");

                    var otherOutput = command.OutputValue == 0 ? 1 : 0;

                    // Gegen-Ausgang zuerst deaktivieren, dann gewählten Ausgang aktivieren.
                    await decoder.SetFunctionAsync(otherOutput, AccessoryFunctionState.Off, cancellationToken)
                        .ConfigureAwait(false);
                    await decoder.SetFunctionAsync(command.OutputValue, AccessoryFunctionState.On, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await decoder.SetFunctionAsync(command.OutputValue, AccessoryFunctionState.On, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        CurrentState = state.State;

        if (ActivationTime > 0)
            Console.WriteLine(Protocol == AccessoryDecoderProtocol.DccExtended
                ? $"Zubehör {Type} {Name}: Schaltzeit {ActivationTime} ms als Decoder-Daten übermittelt."
                : $"Zubehör {Type} {Name}: Auto-Off nach {ActivationTime} ms ausgeführt.");
    }

    /// <summary>
    ///     Synchronously sets the accessory to a configured state.
    /// </summary>
    public void SetState(string stateId)
    {
        SetStateAsync(stateId).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Returns the parsed state definition for the given state id, if present.
    /// </summary>
    public AccessoryStateDefinition? GetState(string stateId)
    {
        if (string.IsNullOrWhiteSpace(stateId))
            return null;

        return _statesById.TryGetValue(stateId.Trim(), out var state) ? state : null;
    }

    /// <summary>
    ///     Returns all configured state ids.
    /// </summary>
    public IReadOnlyList<string> GetAvailableStateIds()
    {
        return _states.Select(state => state.State).ToList();
    }

    private void InitializeDecoders()
    {
        var commandsByAddress = _states
            .SelectMany(state => state.Commands.Select(command =>
                AccessoryStateUtils.ValidateCommandMetadata(AccessoryId, state, command, Type, Subtype, Name,
                    Interlocking)))
            .GroupBy(command => command.Address)
            .OrderBy(group => group.Key)
            .ToList();

        foreach (var addressGroup in commandsByAddress)
        {
            var decoderConfig = AccessoryUtils.GetDecoderConfiguration(addressGroup.Key, Protocol);
            var decoder = new AccessoryDecoder(decoderConfig); // keine Zentrale im Konstruktor
            decoder.StateChanged += OnDecoderStateChanged;
            _decoders.Add(decoder);
        }
    }

    // Zubehörartikel bei Zentrale registrieren
    private void SubscribeCommandStation(ICommandStation commandStation)
    {
        if (SubscribedCommandStation is not null && !ReferenceEquals(SubscribedCommandStation, commandStation))
            throw new InvalidOperationException(
                $"Zubehör {Type} {Name}: Es ist bereits eine Zentrale abonniert ({SubscribedCommandStation.GetType().Name}).");

        foreach (var decoder in _decoders)
            decoder.SubscribeCommandStationAsync(commandStation).GetAwaiter().GetResult();

        Console.WriteLine($"Zubehör {Type} {Name}: Zentrale '{commandStation.GetType().Name}' gebunden " +
                          $"({_decoders.Count} AccessoryDecoder, genau eine Zentrale erlaubt).");
    }

    // Wird aufgerufen, wenn sich der Zustand des Decoders ändert (Readback von Zentrale)
    private void OnDecoderStateChanged(object? sender, AccessoryStateChangedEventArgs args)
    {
        StateChanged?.Invoke(this, args);

        if (_decoders.Count > 1)
        {
            ScheduleDeferredReadBackStateEvaluation(args);
            return;
        }

        if (args.FunctionState != AccessoryFunctionState.On)
            return;

        string? resolvedStateId;
        lock (_readBackSync)
        {
            _lastReadBackValuesByAddress[args.Address] = args.OutputValue;
            resolvedStateId =
                AccessoryStateUtils.ResolveCurrentStateFromReadBack(_states, _lastReadBackValuesByAddress);
        }

        ApplyResolvedReadBackState(resolvedStateId, false, 0);
    }

    // Verzögerte Readback-Auswertung planen, falls der Zubehörartikel mit mehreren Decodern verbunden ist
    // (je nachdem werden Decoder auch von externen Clients mit Verzögerung geschaltet).
    private void ScheduleDeferredReadBackStateEvaluation(AccessoryStateChangedEventArgs args)
    {
        int generation;
        int delayMilliseconds;

        lock (_readBackSync)
        {
            // Nur On-ReadBacks übernehmen.
            if (args.FunctionState == AccessoryFunctionState.On)
                _lastReadBackValuesByAddress[args.Address] = args.OutputValue;

            // Neuere Meldungen machen ältere Auswertungen ungültig.
            generation = ++_readBackEvaluationGeneration;
            // Pro Adresse 1000 ms Sammelwartezeit.
            delayMilliseconds = _decoders.Count * DeferredReadBackEvaluationDelayPerAddressMs;
        }

        // Finale Zustandszuordnung verzögert ausführen.
        _ = EvaluateDeferredReadBackStateAsync(generation, delayMilliseconds);
    }

    // Wertet Readback-Zustände nach Ablauf des Sammelfensters (Verzögerung) aus.
    private async Task EvaluateDeferredReadBackStateAsync(int generation, int delayMilliseconds)
    {
        // Bis zum Ende des Sammelfensters warten.
        await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds)).ConfigureAwait(false);

        string? resolvedStateId;
        lock (_readBackSync)
        {
            // Veraltete Auswertungen abbrechen.
            if (generation != _readBackEvaluationGeneration)
                return;

            // Zustand erst nach Ablauf des Sammelfensters auflösen.
            resolvedStateId =
                AccessoryStateUtils.ResolveCurrentStateFromReadBack(_states, _lastReadBackValuesByAddress);
        }

        ApplyResolvedReadBackState(resolvedStateId, true, delayMilliseconds);
    }

    // Ermittelten Zustand (Weichenstellung, Fahrbegriff) übernehmen.
    private void ApplyResolvedReadBackState(string? resolvedStateId, bool delayedEvaluation, int delayMilliseconds)
    {
        // Nur eindeutige Zustände übernehmen.
        if (resolvedStateId is null)
            return;

        // Bereits bekannten Zustand nicht erneut setzen.
        if (string.Equals(CurrentState, resolvedStateId, StringComparison.OrdinalIgnoreCase))
            return;

        // Zustand übernehmen.
        CurrentState = resolvedStateId;

        if (delayedEvaluation)
        {
            // Verzögerte Zustandszuordnung protokollieren.
            Console.WriteLine(
                $"Zubehör {Type} {Name}: ReadBack-State-ID nach Sammelwartezeit ({delayMilliseconds} ms) = '{CurrentState}'.");
            return;
        }

        Console.WriteLine($"Zubehör {Type} {Name}: ReadBack-State-ID = '{CurrentState}'.");
    }
}

/// <summary>
///     Flat output value command entry for a single accessory state.
/// </summary>
public readonly record struct AccessoryStateCommand(
    AccessoryType Type,
    string Subtype,
    string Id,
    string Interlocking,
    string State,
    int Address,
    int OutputValue);

/// <summary>
///     One accessory state including its description and all decoder commands.
/// </summary>
public readonly record struct AccessoryStateDefinition(
    AccessoryType Type,
    string Subtype,
    string Id,
    string Interlocking,
    string State,
    string Description,
    IReadOnlyList<AccessoryStateCommand> Commands);