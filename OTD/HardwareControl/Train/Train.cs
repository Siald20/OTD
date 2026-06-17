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

public class Train
{
    private readonly List<ICommandStation> _subscribedCommandStations = [];
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private HeadlightMode _headlightMode = HeadlightMode.Auto;
    private TrainDirection _trainDirection = TrainDirection.A;
    private TrainOperatingMode _operatingMode = TrainOperatingMode.ShutDown;

    public Train(
        Guid trainId,
        ICommandStation? commandStation = null)
    {
        TrainId = trainId;
        Id = trainId.ToString();
        if (commandStation is not null)
            _subscribedCommandStations.Add(commandStation);

        LoadComposition();
    }

    /// <summary>
    ///     Unique identifier of this train.
    /// </summary>
    public Guid TrainId { get; }

    /// <summary>
    ///     User-defined identifier for this train.
    /// </summary>
    public string Id { get; }

    // ToDo: ggf. obsolet, da nicht als public property verfügbar sein muss.
    public XElement? TrainConfig;

    /// <summary>
    ///     Total train length.
    ///     Primary source is the summed vehicle lengths from the composition.
    ///     Fallback source is trains.xml attribute <c>train/@length</c> when composition length is unknown (0).
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    ///     Minimum speed (VMin) of the train composition, based on the highest VMin value
    ///     (speed at speed step 1) among all vehicles.
    /// </summary>
    public int VMin { get; private set; }

    /// <summary>
    ///     Maximum speed (VMax) of the train composition, determined by the lowest VMax value
    ///     among all vehicles, either from the VMax configuration attribute or from the maximum
    ///     entry in the speedtable element.
    ///     If <c>&lt;model&gt;&lt;vmax&gt;</c> in trains.xml is set and smaller than the computed
    ///     composition VMax, it acts as an upper limit for the whole composition.
    /// </summary>
    public int VMax { get; private set; }

    /// <summary>
    ///     Total weight of the train composition in grams.
    ///     Primary source is the summed vehicle weights from the composition.
    ///     Fallback source is <c>&lt;model&gt;&lt;weight&gt;</c> in trains.xml when composition weight is unknown (0).
    /// </summary>
    public int Weight { get; private set; }

    /// <summary>
    ///     Full train composition in train order. Each entry contains the vehicle metadata
    ///     from trains.xml together with the initialized runtime instance in <see cref="TrainVehicle.VehicleInstance" />.
    /// </summary>
    public TrainComposition TrainComposition { get; private set; } = TrainComposition.Empty;

    /// <summary>
    ///     Gets or sets the current operating mode of the train.
    ///     The setter synchronously delegates to <see cref="SetOperatingModeAsync" /> and applies
    ///     the required transition across all vehicles. Switching to <see cref="TrainOperatingMode.ShutDown" />
    ///     or <see cref="TrainOperatingMode.Parking" /> enforces standstill, while
    ///     <see cref="TrainOperatingMode.Shunting" /> and <see cref="TrainOperatingMode.Travelling" />
    ///     enable processing of drive commands.
    /// </summary>
    public TrainOperatingMode OperatingMode
    {
        get => _operatingMode;
        set => SetOperatingModeAsync(value).GetAwaiter().GetResult();
    }
    
    /// <summary>
    ///     Globally enables or disables train headlights via decoder master functions.
    ///     Enabling triggers a re-initialization of headlight patterns for the current operating mode.
    /// </summary>
    public HeadlightMode HeadlightMode
    {
        get => _headlightMode;
        set => SwitchHeadlightModeAsync(value).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Gets or sets the current train travelling direction.
    ///     The direction can be changed in every operating mode.
    /// </summary>
    public TrainDirection TrainDirection
    {
        get => _trainDirection;
        set => SetTrainDirectionAsync(value).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Gets the current target speed of the train in km/h.
    ///     This is the last requested train speed and is applied to drivable vehicles (locomotives) only.
    /// </summary>
    public int SpeedV { get; private set; }

    /// <summary>
    ///     All command stations currently subscribed for this train.
    /// </summary>
    public IReadOnlyList<ICommandStation> SubscribedCommandStations => _subscribedCommandStations.AsReadOnly();
    
    /// <summary>
    ///     Refreshes the train composition and re-initializes all runtime vehicle instances based
    ///     on the current train and vehicle configuration.
    /// </summary>
    public void LoadComposition()
    {
        if (_operatingMode is not TrainOperatingMode.ShutDown)
            throw new InvalidOperationException(
                $"Train refresh is only allowed in ShutDown operating mode. Current operation mode for Train {TrainId} is: {_operatingMode}.");

        var trainConfig = TrainUtils.ReadXConfiguration("train", TrainId);

        if (trainConfig is null)
            throw new InvalidOperationException($"Train {TrainId} could not be found.");

        var configuredLength = TrainUtils.GetLengthFromTrainConfig(trainConfig.Attribute("length")?.Value);
        var configuredVMax = TrainUtils.GetVehicleVMax(trainConfig.Element("model"));
        var configuredWeight = TrainUtils.GetVehicleWeight(trainConfig.Element("model"));

        var compositionVehicles = TrainUtils.GetComposition(trainConfig);

        if (compositionVehicles.Count == 0)
            throw new InvalidOperationException($"Train {TrainId} does not include vehicles.");

        if (!compositionVehicles.Any(vehicle =>
                string.Equals(vehicle.VehicleType, "loco", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Train {TrainId} doesn't include at least one locomotive.");

        var compositionBuilder = new TrainCompositionBuilder(compositionVehicles);
        var previousComposition = TrainComposition;
        var subscribedStationsSnapshot = _subscribedCommandStations.ToArray();
        var newComposition = compositionBuilder.Build(
            CreateVehicleController,
            (controller, _, _, _) =>
            {
                if (!controller.HasDecoder)
                    return;

                foreach (var commandStation in subscribedStationsSnapshot)
                    controller.LocoDecoder.SubscribeCommandStationAsync(commandStation).GetAwaiter().GetResult();
            });

        UnsubscribeCommandStations(previousComposition, subscribedStationsSnapshot);
        TrainComposition = newComposition;

        // Zug-Länge: Fallback auf konfigurierte Werte aus trains.xml, falls nicht für alle Fahrzeuge ein Wert erfasst ist.
        Length = TrainComposition.Length > 0 ? TrainComposition.Length : configuredLength;
        // Zug-Mindestgeschwindigkeit (kleinste Geschwindigkeit aller Fahrzeuge bei Fahrstufe 1)
        VMin = TrainComposition.VMin;
        // Zug-Höchstgeschwindigkeit: Fallback auf trains.xml, falls nicht für alle Fahrzeuge ein Wert erfasst ist oder trains.xml einen niedrigeren Wert vorgibt.
        VMax = configuredVMax > 0 && TrainComposition.VMax > 0 && configuredVMax < TrainComposition.VMax
            ? configuredVMax
            : TrainComposition.VMax;
        // Zug-Gewicht: Fallback auf trains.xml, falls nicht für alle Fahrzeuge ein Wert erfasst ist.
        Weight = TrainComposition.Weight > 0 ? TrainComposition.Weight : configuredWeight;

        TrainConfig = trainConfig;
    
        Console.WriteLine(
            $"Loading of train composition {Id} completed. {TrainComposition.Count} vehicle(s) initialized. Length: {Length} mm, VMin: {VMin} km/h, VMax: {VMax} km/h, Weight: {Weight} t.");
    }

    /// <summary>
    ///     Sets the train operating mode and applies the required state transition across the composition.
    ///     ShutDown and Parking enforce standstill, while DrivingA and DrivingB
    ///     switch to the corresponding travel direction and update headlights accordingly.
    /// </summary>
    /// <param name="mode">Target operating mode.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if an unsupported mode is provided.</exception>
    public async Task SetOperatingModeAsync(TrainOperatingMode mode, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (SpeedV > 0)
                throw new InvalidOperationException(
                    $"Train {TrainId} cannot change operating mode while speed is greater than 0 km/h. Current speed: {SpeedV} km/h.");

            // ToDo: Prüfen, ob Befehle bei identischem Modus ignoriert werden sollen.
            // ToDo: Grundsätzlich sollte die Steuerlogik volle Kontrolle haben und Befehle bei Bedarf wiederholen können.
            if (_operatingMode == mode)
                return;

            switch (mode)
            {
                // Zug wird heruntergefahren und nach Möglichkeit aus der Command-Queue der Zentrale entfernt.
                case TrainOperatingMode.ShutDown:
                    _operatingMode = TrainOperatingMode.ShutDown;
                    break;

                // Zug wird parkiert, es werden keine Fahrbefehle verarbeitet.
                case TrainOperatingMode.Parking:
                    _operatingMode = TrainOperatingMode.Parking;
                    break;

                // Zug verkehrt im Rangiermodus mit reduzierter Maximalgeschwindigkeit.
                case TrainOperatingMode.Shunting:
                    await SendDirectionAsync(_trainDirection, cancellationToken)
                        .ConfigureAwait(false);
                    _operatingMode = TrainOperatingMode.Shunting;
                    break;

                // Zug verkehrt im Fahrmodus. Die konkrete Fahrtrichtung wird über TrainDirection bestimmt.
                case TrainOperatingMode.Travelling:
                    await SendDirectionAsync(_trainDirection, cancellationToken)
                        .ConfigureAwait(false);
                    _operatingMode = TrainOperatingMode.Travelling;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode,
                        $"Unsupported operating mode {mode} for train {TrainId}.");
            }

            // Stirnlichter entsprechend Betriebsmodus aktualisieren.
            await UpdateHeadlightFunctionsAsync(_headlightMode, mode, cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"Zug {TrainId}: Betriebszustand auf {_operatingMode} gesetzt.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Setzen des Betriebsmodus {mode} für Zug {TrainId}: {ex.Message}");
            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    ///     Sets the train travelling direction independent of the current operating mode.
    /// </summary>
    public async Task SetTrainDirectionAsync(
        TrainDirection direction,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_trainDirection == direction)
                return;

            _trainDirection = direction;
            await SendDirectionAsync(_trainDirection, cancellationToken).ConfigureAwait(false);
            await UpdateHeadlightFunctionsAsync(_headlightMode, _operatingMode, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine($"Zug {TrainId}: Fahrtrichtung auf {_trainDirection} gesetzt.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    ///     Sets the same target speed for all drivable vehicles in this train composition.
    /// </summary>
    public async Task SetSpeedVAsync(int speed, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_operatingMode is not (TrainOperatingMode.Shunting or TrainOperatingMode.Travelling))
                throw new InvalidOperationException(
                    $"Zug {TrainId}: Fahrbefehl {speed} km/h nicht zulässig im Modus {_operatingMode}.");

            if (_operatingMode is TrainOperatingMode.Shunting)
            {
                var shuntingVMax = Math.Min(VMax, 40);
                if (speed > shuntingVMax)
                {
                    throw new InvalidOperationException(
                        $"Zug {TrainId}: Fahrbefehl {speed} km/h überschreitet die zulässige Rangier-Höchstgeschwindigkeit von {shuntingVMax} km/h.");
                }
            }

            await SendSpeedCommandToAllAsync(speed, cancellationToken).ConfigureAwait(false);
            SpeedV = speed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Senden des Fahrbefehls {speed} km/h für Zug {TrainId}: {ex.Message}");
            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    ///     Sets the speed for the train composition.
    /// </summary>
    public void SetSpeedV(int speed)
    {
        SetSpeedVAsync(speed).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Subscribes a command station for all locomotive decoders in this train.
    /// </summary>
    public async Task SubscribeCommandStationAsync(
        ICommandStation commandStation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandStation);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_subscribedCommandStations.Contains(commandStation))
                return;

            _subscribedCommandStations.Add(commandStation);
            await SubscribeCommandStationAsync(TrainComposition, commandStation, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine($"Zug {TrainId}: Zentrale '{commandStation.GetType().Name}' abonniert. Insgesamt {_subscribedCommandStations.Count} Zentrale(n) gebunden.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    ///     Synchronously subscribes a command station for all locomotive decoders in this train.
    /// </summary>
    public void SubscribeCommandStation(ICommandStation commandStation)
        => SubscribeCommandStationAsync(commandStation).GetAwaiter().GetResult();

    /// <summary>
    ///     Unsubscribes a command station from all locomotive decoders in this train.
    /// </summary>
    public async Task UnsubscribeCommandStationAsync(
        ICommandStation? commandStation,
        CancellationToken cancellationToken = default)
    {
        if (commandStation is null)
            return;

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_subscribedCommandStations.Remove(commandStation))
                return;

            await UnsubscribeStationAsync(TrainComposition, commandStation, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine($"Zug {TrainId}: Zentrale '{commandStation.GetType().Name}' abgemeldet. Noch {_subscribedCommandStations.Count} Zentrale(n) gebunden.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    ///     Synchronously unsubscribes a command station from all locomotive decoders in this train.
    /// </summary>
    public void UnsubscribeCommandStation(ICommandStation? commandStation)
        => UnsubscribeCommandStationAsync(commandStation).GetAwaiter().GetResult();

    private async Task SendSpeedCommandToAllAsync(int speed,
        CancellationToken cancellationToken)
    {
        try
        {
            var tasks = new List<Task>(TrainComposition.Count);

            foreach (var vehicleEntry in TrainComposition)
                if (vehicleEntry.VehicleInstance is Loco locoController)
                    tasks.Add(locoController.SetSpeedVAsync(speed, cancellationToken: cancellationToken));

            await Task.WhenAll(tasks).ConfigureAwait(false);

            Console.WriteLine(
                $"Zug {TrainId}: Fahrbefehl {speed} km/h an {tasks.Count} angetriebene Fahrzeug(e) gesendet.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Senden des Fahrbefehls {speed} km/h für Zug {TrainId}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    ///     Triggers an emergency stop for all drivable vehicles in this train composition.
    /// </summary>
    public async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        if (TrainComposition.Count == 0)
        {
            Console.WriteLine($"Warnung: Zug {TrainId} enthält keine Fahrzeuge.");
            return;
        }

        var tasks = new List<Task>(TrainComposition.Count);

        foreach (var vehicleEntry in TrainComposition)
            if (vehicleEntry.VehicleInstance is Loco locoController)
                tasks.Add(locoController.EmergencyStopAsync(cancellationToken));
            else
                Console.WriteLine($"Warnung: Fahrzeug {vehicleEntry.VehicleId} ist kein Loco-Controller.");

        await Task.WhenAll(tasks).ConfigureAwait(false);
        Console.WriteLine($"Zug {TrainId}: Notbremsung an {tasks.Count} angetriebene Fahrzeug(e) gesendet.");
    }

    /// <summary>
    ///     Triggers an emergency stop for all drivable vehicles in this train composition.
    /// </summary>
    public void EmergencyStop()
    {
        EmergencyStopAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Sets the given function state.
    ///     If no vehicle IDs are provided, all vehicles in this train are addressed.
    /// </summary>
    public async Task SetFunctionStateAsync(
        int function,
        LocoDecoderFunctionState state,
        Guid[]? vehicleIds = null,
        CancellationToken cancellationToken = default)
    {
        if (state is LocoDecoderFunctionState.Undefined)
            throw new ArgumentOutOfRangeException(nameof(state), state,
                "FunctionState.Undefined: unzulässiger Wert zum Schalten.");

        var selectedControllers = ResolveSelectedControllers(vehicleIds);
        if (selectedControllers.Count == 0)
            return;

        var tasks = selectedControllers
            .Select(controller => controller.LocoDecoder)
            .OfType<ILocoDecoder>()
            .Select(decoder => decoder.SetFunctionStateAsync(function, state, cancellationToken))
            .ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        Console.WriteLine(
            $"Zug {TrainId}: Funktion {function} auf {tasks.Count} Fahrzeugdecoder gesetzt ({state}).");
    }

    /// <summary>
    ///     Sets the given function state for selected vehicles identified by 0-based train positions.
    ///     If no positions are provided, all vehicles in this train are addressed.
    /// </summary>
    public Task SetFunctionStateAsync(
        int function,
        LocoDecoderFunctionState state,
        int[]? positions,
        CancellationToken cancellationToken = default)
    {
        var vehicleIds = ResolveVehicleIdsByPositions(positions);
        return SetFunctionStateAsync(function, state, vehicleIds, cancellationToken);
    }

    /// <summary>
    ///     Synchronously activates the given function for the specified timeout.
    ///     If no vehicle IDs are provided, all vehicles in this train are addressed.
    /// </summary>
    public void ActivateFunction(int function, int timeout, params Guid[] vehicleIds)
    {
        ActivateFunctionAsync(function, timeout, vehicleIds).GetAwaiter().GetResult();
    }

    // Switching the headlight mode for the whole train composition (auto, on, off). Suppressed headlights on selected vehicles will stay always off.
    public async Task SwitchHeadlightModeAsync(HeadlightMode headlightMode,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_headlightMode == headlightMode)
                return;

            _headlightMode = headlightMode;
            await UpdateHeadlightFunctionsAsync(headlightMode, _operatingMode, cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"Zug {TrainId}: Stirnlichter auf {headlightMode} gesetzt.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    ///     Detaches the currently bound composition so it can be modified via <see cref="TrainCompositionBuilder"/>.
    ///     This is only allowed in <see cref="TrainOperatingMode.ShutDown"/>.
    /// </summary>
    public TrainCompositionBuilder DetachComposition()
        => DetachCompositionAsync().GetAwaiter().GetResult();

    /// <summary>
    ///     Detaches the currently bound composition so it can be modified via <see cref="TrainCompositionBuilder"/>.
    ///     This is only allowed in <see cref="TrainOperatingMode.ShutDown"/>.
    /// </summary>
    public async Task<TrainCompositionBuilder> DetachCompositionAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_operatingMode is not TrainOperatingMode.ShutDown)
                throw new InvalidOperationException(
                    $"Train {TrainId} can only be detached in ShutDown mode. Current mode: {_operatingMode}.");

            var compositionToDetach = TrainComposition;
            UnsubscribeCommandStations(compositionToDetach, _subscribedCommandStations.ToArray());

            TrainComposition = TrainComposition.Empty;
            Length = 0;
            VMin = 0;
            VMax = 0;
            Weight = 0;
            SpeedV = 0;

            return new TrainCompositionBuilder(
                compositionToDetach,
                preserveRuntimeState: false,
                trainId: TrainId,
                vehicleFactory: CreateVehicleController,
                commandStations: _subscribedCommandStations.ToArray());
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    ///     Activates the given function for the specified timeout.
    ///     If no vehicle IDs are provided, all vehicles in this train are addressed.
    /// </summary>
    public async Task ActivateFunctionAsync(
        int function,
        int timeout,
        Guid[]? vehicleIds = null,
        CancellationToken cancellationToken = default)
    {
        var selectedControllers = ResolveSelectedControllers(vehicleIds);
        if (selectedControllers.Count == 0)
            return;

        var tasks = selectedControllers
            .Select(controller => controller.LocoDecoder)
            .OfType<ILocoDecoder>()
            .Select(decoder => decoder.ActivateFunctionAsync(function, timeout, cancellationToken))
            .ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        Console.WriteLine(
            $"Zug {TrainId}: Funktion {function} fuer {timeout} ms auf {tasks.Count} Fahrzeugdecoder aktiviert.");
    }

    /// <summary>
    ///     Activates the given function for the specified timeout on selected vehicles identified by 0-based train positions.
    ///     If no positions are provided, all vehicles in this train are addressed.
    /// </summary>
    public Task ActivateFunctionAsync(
        int function,
        int timeout,
        int[]? positions,
        CancellationToken cancellationToken = default)
    {
        var vehicleIds = ResolveVehicleIdsByPositions(positions);
        return ActivateFunctionAsync(function, timeout, vehicleIds, cancellationToken);
    }

    private Guid[] ResolveVehicleIdsByPositions(int[]? positions)
    {
        // leer/null => alle Fahrzeuge; Positionen sind 0-basiert
        if (positions is not { Length: > 0 })
            return TrainComposition.Select(v => v.VehicleId).ToArray();

        var positionSet = new HashSet<int>(positions);

        return TrainComposition
            .Select((vehicle, index) => new { vehicle.VehicleId, Position = index })
            .Where(x => positionSet.Contains(x.Position))
            .Select(x => x.VehicleId)
            .Distinct()
            .ToArray();
    }

    private List<IVehicle> ResolveSelectedControllers(
        Guid[]? vehicleIds)
    {
        if (!TrainComposition.Any())
        {
            Console.WriteLine($"Warnung: Zug {TrainId} enthält keine Fahrzeuge.");
            return new List<IVehicle>();
        }

        var hasVehicleIds = vehicleIds is { Length: > 0 };

        var selectedUids = new HashSet<Guid>();
        if (hasVehicleIds)
            foreach (var vehicleId in vehicleIds!)
                selectedUids.Add(vehicleId);

        if (!hasVehicleIds)
            foreach (var vehicleEntry in TrainComposition)
                selectedUids.Add(vehicleEntry.VehicleId);

        var selectedControllers = new List<IVehicle>(selectedUids.Count);
        foreach (var vehicleEntry in TrainComposition)
        {
            if (!selectedUids.Contains(vehicleEntry.VehicleId))
                continue;

            if (vehicleEntry.VehicleInstance is { } controller)
                selectedControllers.Add(controller);
            else
                Console.WriteLine($"Warnung: Fahrzeug-Instanz für UID {vehicleEntry.VehicleId} fehlt.");
        }

        if (selectedControllers.Count == 0)
            Console.WriteLine("Warnung: Keine passenden Fahrzeuge fuer die ausgewaehlte Funktionssteuerung gefunden.");

        return selectedControllers;
    }

    private IVehicle CreateVehicleController(TrainVehicle vehicle)
    {
        return vehicle.VehicleType.ToLowerInvariant() switch
        {
            "loco" => new Loco(vehicle.VehicleId),
            "car" => new Car(vehicle.VehicleId),
            _ => throw new InvalidOperationException(
                $"Train {TrainId} has an invalid vehicle type '{vehicle.VehicleType}' for vehicle UID {vehicle.VehicleId}")
        };
    }

    private async Task SubscribeCommandStationAsync(
        TrainComposition trainComposition,
        ICommandStation commandStation,
        CancellationToken cancellationToken = default)
    {
        foreach (var vehicle in trainComposition)
        {
            if (vehicle.VehicleInstance is not { HasDecoder: true } controller)
                continue;

            await controller.LocoDecoder.SubscribeCommandStationAsync(commandStation, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task UnsubscribeStationAsync(
        TrainComposition trainComposition,
        ICommandStation commandStation,
        CancellationToken cancellationToken = default)
    {
        foreach (var vehicle in trainComposition)
        {
            if (vehicle.VehicleInstance is not { HasDecoder: true } controller)
                continue;

            await controller.LocoDecoder.UnsubscribeCommandStationAsync(commandStation, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void UnsubscribeCommandStations(
        TrainComposition trainComposition,
        IReadOnlyList<ICommandStation> commandStations)
    {
        if (commandStations.Count == 0)
            return;

        foreach (var vehicle in trainComposition)
        {
            if (vehicle.VehicleInstance is not { HasDecoder: true } controller)
                continue;

            foreach (var commandStation in commandStations)
                controller.LocoDecoder.UnsubscribeCommandStationAsync(commandStation).GetAwaiter().GetResult();
        }
    }

    // Setzt die Fahrtrichtung aller Fahrzeugdecoder der Zugkomposition.
    private async Task SendDirectionAsync(TrainDirection trainDirection,
        CancellationToken cancellationToken = default)
    {
        // ToDo: überprüfen, ob hier eine Behandlung notwendig ist
        // if (operatingMode is TrainOperatingMode.Travelling)
        //     throw new InvalidOperationException(
        //         $"Zug {TrainId}: Fahrtrichtungswechsel ist nur in einem Fahrmodus zulässig. Übergeben: {operatingMode}.");

        if (TrainComposition.Count == 0)
        {
            Console.WriteLine($"Warnung: Zug {TrainId} enthält keine Fahrzeuge.");
            return;
        }

        var tasks = new List<Task>(TrainComposition.Count);

        // Fahrtrichtung an alle Fahrzeugdecoder weiterleiten.
        // Nicht angetriebene Fahrzeuge werden mit SpeedStep 0 adressiert,
        // damit ihre LocoDecoder-Richtung für die Stirnlichtlogik synchron bleibt.
        foreach (var vehicleEntry in TrainComposition)
        {
            var vehicle = vehicleEntry.VehicleInstance;
            if (vehicle is null)
                continue;

            tasks.Add(vehicle.SetDirectionAsync(trainDirection, vehicleEntry.Orientation,
                cancellationToken: cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        Console.WriteLine(
            $"Zug {TrainId}: Fahrtrichtung {trainDirection} an {tasks.Count} Fahrzeugdecoder gesendet.");
    }

    // Sendet die Befehle zum Schalten der Stirnbeleuchtung an alle Fahrzeuge der Zugkomposition,
    // abhängig vom Betriebsmodus, inklusive Schalten der zugeordneten Beleuchtungsmuster.
    // mainHeadlightState signalisiert, ob die Beleuchtung auf den gesamten Zug bezogen ein- oder ausgeschaltet sein soll.
    // Nur Fahrzeuge mit LocoDecoder (HasDecoder) werden berücksichtigt. Funktionen werden nur geschaltet, wenn sich der Zustand ändert.
    private async Task UpdateHeadlightFunctionsAsync(HeadlightMode headlightsMode, TrainOperatingMode operatingMode,
        CancellationToken cancellationToken = default)
    {
        if (TrainComposition.Count == 0)
            return;

        // Zustand der Stirnbeleuchtung bei Betriebsmodus ShutDown unverändert lassen, sofern Stirnbeleuchtung
        // nicht im Auto-Modus. Stirnlichter werden beim Verlassen des ShutDown-Modus automatisch aktualisiert.
        if (operatingMode is TrainOperatingMode.ShutDown && headlightsMode is HeadlightMode.On or HeadlightMode.Off)
            return;

        // Soll-Zustand (ein/aus) der Stirnbeleuchtung für den Zug aufgrund Licht- und Betriebsmodus ermitteln.
        var mainHeadlightState = HeadlightUtils.DetermineMainHeadlightState(headlightsMode, operatingMode);
        if (mainHeadlightState is LocoDecoderFunctionState.Undefined)
        {
            Console.WriteLine(
                $"Warnung: Ungültiger Stirnlicht-Sollzustand für Zug {TrainId} (HeadlightMode={headlightsMode}, OperatingMode={operatingMode}). Keine Schaltung ausgeführt.");
            return;
        }

        var tasks = new List<Task>();

        // Stirnbeleuchtung bei allen Fahrzeugen mit LocoDecoder schalten
        foreach (var vehicleEntry in TrainComposition)
        {
            if (vehicleEntry.VehicleInstance is not { HasDecoder: true } vehicle)
                continue;

            var decoder = vehicle.LocoDecoder;

            var availableHeadlightFunctions = HeadlightUtils.GetAvailableHeadlightFunctions(decoder.Functions);
            if (availableHeadlightFunctions.Count == 0) continue;

            var masterFunction = availableHeadlightFunctions.FirstOrDefault(f => f.IsMaster);
            if (!masterFunction.IsMaster)
            {
                throw new InvalidOperationException(
                    $"Keine Master-Stirnlichtfunktion für Fahrzeug {vehicleEntry.VehicleId} konfiguriert. " +
                    "Bitte in der XML-Konfiguration eine Headlight-Funktion mit master=\"true\" definieren.");
            }

            var isFirstOrLastVehicle = vehicleEntry.Position == 1 || vehicleEntry.Position == TrainComposition.Count;
            var decoderDirection = LocoDecoderUtils.ResolveDecoderDirection(_trainDirection, vehicleEntry.Orientation);
            var configuredPattern = decoderDirection == VehicleDirection.Forward
                ? vehicleEntry.HeadlightPatternForward
                : vehicleEntry.HeadlightPatternBackward;

            var matchingPatternFunction = (HeadlightPatternFunction?)null;

            if (mainHeadlightState is LocoDecoderFunctionState.On)
            {
                // Prüfen, ob spezifische Stirnlicht-Funktionen für OperatingMode Parking oder Shunting definiert sind.
                // Falls ja, dann werden diese verwendet.
                matchingPatternFunction = operatingMode switch
                {
                    TrainOperatingMode.Parking => availableHeadlightFunctions
                        .Where(function =>
                            string.Equals(function.Mode.Trim(), "parking", StringComparison.OrdinalIgnoreCase))
                        .Select(function => (HeadlightPatternFunction?)function)
                        .FirstOrDefault(),
                    TrainOperatingMode.Shunting => availableHeadlightFunctions
                        .Where(function =>
                            string.Equals(function.Mode.Trim(), "shunting", StringComparison.OrdinalIgnoreCase))
                        .Select(function => (HeadlightPatternFunction?)function)
                        .FirstOrDefault(),
                    _ => null
                };

                // Prüfen, ob bei der Konfiguration der Zugkomposition für die aktuelle Fahrtrichtung
                // ein spezifisches Pattern definiert ist. Falls ja, dann wird dieses verwendet.
                if (matchingPatternFunction is null && !string.IsNullOrWhiteSpace(configuredPattern))
                    matchingPatternFunction =
                        HeadlightUtils.FindHeadlightPattern(configuredPattern, decoderDirection,
                            availableHeadlightFunctions);
            }

            var nonMasterFunctionNumbers = availableHeadlightFunctions
                .Where(f => !f.IsMaster)
                .Select(f => f.FunctionNumber)
                .Distinct()
                .ToList();

            // Ausschalten: falls Stirnbeleuchtung global ausgeschaltet wird oder Fahrzeug innerhalb des Zuges
            // liegt und keine spezifischen Funktionen/Patterns definiert sind.
            if (mainHeadlightState == LocoDecoderFunctionState.Off || (!isFirstOrLastVehicle && matchingPatternFunction is null))
            {
                // zuerst Master-Stirnlichtfunktion ausschalten
                if (decoder.GetFunctionState(masterFunction.FunctionNumber) != LocoDecoderFunctionState.Off)
                    tasks.Add(decoder.SetFunctionStateAsync(masterFunction.FunctionNumber, LocoDecoderFunctionState.Off,
                        cancellationToken));

                // dann alle spezifischen Stirnlicht-Funktionen ausschalten
                foreach (var fn in nonMasterFunctionNumbers)
                    if (decoder.GetFunctionState(fn) != LocoDecoderFunctionState.Off)
                        tasks.Add(decoder.SetFunctionStateAsync(fn, LocoDecoderFunctionState.Off, cancellationToken));

                continue;
            }

            // Einschalten 1: spezifische Stirnlicht-Funktion einschalten und ggf. übrige aktive
            // Stirnlicht-Funktion(en) ausschalten.
            var matchingNonMasterNumber = matchingPatternFunction is { IsMaster: false } m
                ? m.FunctionNumber : (int?)null;

            foreach (var fn in nonMasterFunctionNumbers)
            {
                var targetState = fn == matchingNonMasterNumber ? LocoDecoderFunctionState.On : LocoDecoderFunctionState.Off;
                if (decoder.GetFunctionState(fn) != targetState)
                    tasks.Add(decoder.SetFunctionStateAsync(fn, targetState, cancellationToken));
            }

            // Einschalten 2: Master-Stirnlichtfunktion einschalten.
            if (decoder.GetFunctionState(masterFunction.FunctionNumber) != LocoDecoderFunctionState.On)
                tasks.Add(decoder.SetFunctionStateAsync(masterFunction.FunctionNumber, LocoDecoderFunctionState.On,
                    cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

}
