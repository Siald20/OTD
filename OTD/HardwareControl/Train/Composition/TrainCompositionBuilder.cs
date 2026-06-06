// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <inf@batec.net>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl.Train.Composition;

/// <summary>
/// Mutable builder that is used to assemble a train composition before it is bound to a train.
/// </summary>
public sealed class TrainCompositionBuilder
{
    private readonly List<TrainVehicle> _vehicles;
    private readonly bool _preserveRuntimeState;
    private readonly Guid? _trainId;
    private readonly Func<TrainVehicle, IVehicle>? _vehicleFactory;
    private readonly CommandStation.CommandStation? _commandStation;

    /// <summary>
    /// Creates a new empty builder.
    /// </summary>
    public TrainCompositionBuilder()
        : this(Array.Empty<TrainVehicle>())
    {
    }

    /// <summary>
    /// Creates a new builder from an existing set of composition entries.
    /// Runtime instances are stripped so that the resulting composition can be rebound.
    /// </summary>
    public TrainCompositionBuilder(
        IEnumerable<TrainVehicle> vehicles,
        bool preserveRuntimeState = false,
        Guid? trainId = null,
        Func<TrainVehicle, IVehicle>? vehicleFactory = null,
        CommandStation.CommandStation? commandStation = null)
    {
        if (vehicles is null)
            throw new ArgumentNullException(nameof(vehicles));

        _preserveRuntimeState = preserveRuntimeState;
        _trainId = trainId;
        _vehicles = vehicles.Select(vehicle => Normalize(vehicle, _preserveRuntimeState)).ToList();
        _vehicleFactory = vehicleFactory;
        _commandStation = commandStation;
    }

    /// <summary>
    /// Number of entries currently held by the builder.
    /// </summary>
    public int Count => _vehicles.Count;

    /// <summary>
    /// Exposes the current entries as a read-only view.
    /// </summary>
    public IReadOnlyList<TrainVehicle> Vehicles => _vehicles.AsReadOnly();

    /// <summary>
    /// Adds a vehicle entry to the end of the composition.
    /// </summary>
    public TrainCompositionBuilder AddVehicle(TrainVehicle vehicle)
    {
        EnsureNotBoundToActiveTrain(nameof(AddVehicle));
        _vehicles.Add(Normalize(vehicle, _preserveRuntimeState));
        return this;
    }

    /// <summary>
    /// Adds a range of vehicle entries.
    /// </summary>
    public TrainCompositionBuilder AddVehicles(IEnumerable<TrainVehicle> vehicles)
    {
        EnsureNotBoundToActiveTrain(nameof(AddVehicles));

        if (vehicles is null)
            throw new ArgumentNullException(nameof(vehicles));

        foreach (var vehicle in vehicles)
            AddVehicle(vehicle);

        return this;
    }

    /// <summary>
    /// Inserts a vehicle entry at the specified index.
    /// </summary>
    public TrainCompositionBuilder InsertVehicle(int index, TrainVehicle vehicle)
    {
        EnsureNotBoundToActiveTrain(nameof(InsertVehicle));
        _vehicles.Insert(index, Normalize(vehicle, _preserveRuntimeState));
        return this;
    }

    /// <summary>
    /// Removes the first entry with the given vehicle identifier.
    /// </summary>
    public bool RemoveVehicle(Guid vehicleId)
    {
        EnsureNotBoundToActiveTrain(nameof(RemoveVehicle));

        var index = _vehicles.FindIndex(vehicle => vehicle.VehicleId.Equals(vehicleId));
        if (index < 0)
            return false;

        _vehicles.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Replaces the first matching entry with a new one.
    /// </summary>
    public bool ReplaceVehicle(Guid vehicleId, TrainVehicle replacement)
    {
        EnsureNotBoundToActiveTrain(nameof(ReplaceVehicle));

        var index = _vehicles.FindIndex(vehicle => vehicle.VehicleId.Equals(vehicleId));
        if (index < 0)
            return false;

        _vehicles[index] = Normalize(replacement, _preserveRuntimeState);
        return true;
    }

    /// <summary>
    /// Returns the orientation setting for the specified vehicle.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when the vehicle is not part of this builder.</exception>
    public VehicleOrientation GetOrientation(Guid vehicleId)
    {
        var index = FindIndexOrThrow(vehicleId);
        return _vehicles[index].Orientation;
    }

    /// <summary>
    /// Updates the orientation setting for the specified vehicle.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when the vehicle is not part of this builder.</exception>
    public TrainCompositionBuilder SetOrientation(Guid vehicleId, VehicleOrientation orientation)
    {
        EnsureNotBoundToActiveTrain(nameof(SetOrientation));

        var index = FindIndexOrThrow(vehicleId);
        _vehicles[index] = _vehicles[index] with { Orientation = orientation };
        return this;
    }

    /// <summary>
    /// Returns the configured headlight pattern for decoder direction A (<c>headlight_forward</c>).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when the vehicle is not part of this builder.</exception>
    public string GetHeadlightForward(Guid vehicleId)
    {
        var index = FindIndexOrThrow(vehicleId);
        return _vehicles[index].HeadlightPatternForward;
    }

    /// <summary>
    /// Updates the headlight pattern for decoder direction A (<c>headlight_forward</c>).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when the vehicle is not part of this builder.</exception>
    public TrainCompositionBuilder SetHeadlightForward(Guid vehicleId, string? pattern)
    {
        EnsureNotBoundToActiveTrain(nameof(SetHeadlightForward));

        var index = FindIndexOrThrow(vehicleId);
        _vehicles[index] = _vehicles[index] with { HeadlightPatternForward = pattern?.Trim() ?? string.Empty };
        return this;
    }

    /// <summary>
    /// Returns the configured headlight pattern for decoder direction B (<c>headlight_backward</c>).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when the vehicle is not part of this builder.</exception>
    public string GetHeadlightBackward(Guid vehicleId)
    {
        var index = FindIndexOrThrow(vehicleId);
        return _vehicles[index].HeadlightPatternBackward;
    }

    /// <summary>
    /// Updates the headlight pattern for decoder direction B (<c>headlight_backward</c>).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when the vehicle is not part of this builder.</exception>
    public TrainCompositionBuilder SetHeadlightBackward(Guid vehicleId, string? pattern)
    {
        EnsureNotBoundToActiveTrain(nameof(SetHeadlightBackward));

        var index = FindIndexOrThrow(vehicleId);
        _vehicles[index] = _vehicles[index] with { HeadlightPatternBackward = pattern?.Trim() ?? string.Empty };
        return this;
    }

    /// <summary>
    /// Moves a vehicle entry from one position to another.
    /// </summary>
    public TrainCompositionBuilder MoveVehicle(int fromIndex, int toIndex)
    {
        EnsureNotBoundToActiveTrain(nameof(MoveVehicle));

        if (fromIndex == toIndex)
            return this;

        var vehicle = _vehicles[fromIndex];
        _vehicles.RemoveAt(fromIndex);

        if (toIndex < 0)
            toIndex = 0;
        else if (toIndex > _vehicles.Count)
            toIndex = _vehicles.Count;

        _vehicles.Insert(toIndex, vehicle);
        return this;
    }

    /// <summary>
    /// Splits the composition between two adjacent vehicles by triggering the required uncoupling action.
    /// </summary>
    /// <returns>
    /// Train IDs after split: the existing source train first, and the newly created train second.
    /// </returns>
    public (Guid SourceTrainId, Guid NewTrainId) SplitComposition(Guid vehicle1, Guid vehicle2)
        => SplitCompositionAsync(vehicle1, vehicle2).GetAwaiter().GetResult();

    /// <summary>
    /// Splits the composition between two adjacent vehicles by triggering the required uncoupling action.
    /// The selected vehicle and direction are derived from current decoder direction and available auto couplings.
    /// </summary>
    /// <returns>
    /// Train IDs after split: the existing source train first, and the newly created train second.
    /// </returns>
    public async Task<(Guid SourceTrainId, Guid NewTrainId)> SplitCompositionAsync(
        Guid vehicle1,
        Guid vehicle2,
        CancellationToken cancellationToken = default)
    {
        EnsureNotBoundToActiveTrain(nameof(SplitComposition));

        if (_trainId is null)
            throw new InvalidOperationException(
                "SplitComposition requires a source train UID. Provide trainId via TrainCompositionBuilder constructor.");

        var sourceTrainId = _trainId.Value;

        if (vehicle1 == vehicle2)
            throw new ArgumentException("SplitComposition requires two different vehicle IDs.");

        var index1 = FindIndexOrThrow(vehicle1);
        var index2 = FindIndexOrThrow(vehicle2);

        if (Math.Abs(index1 - index2) != 1)
            throw new InvalidOperationException(
                $"SplitComposition is only valid for adjacent vehicles. Requested indices: {index1} and {index2}.");

        var candidate1 = CreateSplitCandidate(index1);
        var candidate2 = CreateSplitCandidate(index2);

        if (!candidate1.HasAutoCoupling && !candidate2.HasAutoCoupling)
            throw new InvalidOperationException(
                $"SplitComposition between {vehicle1} and {vehicle2} is not possible because no automatic coupling is available at the separation point.");

        var currentDirection = ResolveCurrentDirection(candidate1, candidate2);
        var leadingIndex = currentDirection == VehicleDirection.Forward
            ? Math.Min(index1, index2)
            : Math.Max(index1, index2);

        var selected = candidate1.HasAutoCoupling && candidate2.HasAutoCoupling
            ? (candidate1.Index == leadingIndex ? candidate1 : candidate2)
            : (candidate1.HasAutoCoupling ? candidate1 : candidate2);

        var uncouplingDirection = selected.Index == leadingIndex
            ? currentDirection
            : OppositeDirection(currentDirection);

        var hasRequiredDirection = selected.AutoCouplings.Any(function => function.Direction == uncouplingDirection);
        if (!hasRequiredDirection)
            throw new InvalidOperationException(
                $"Vehicle {selected.Entry.VehicleId} has no auto coupling function configured for direction {uncouplingDirection}.");

        await ExecuteUncouplingAsync(selected, uncouplingDirection, cancellationToken).ConfigureAwait(false);

        var (remainingVehicles, detachedVehicles) = BuildSplitParts(index1, index2);
        var newTrainId = TrainCompositionUtils.SplitTrainConfiguration(sourceTrainId, remainingVehicles, detachedVehicles);

        _vehicles.Clear();
        foreach (var vehicle in remainingVehicles)
            _vehicles.Add(Normalize(vehicle, _preserveRuntimeState));

        Console.WriteLine(
            $"SplitComposition: Train {sourceTrainId} updated with {remainingVehicles.Count} vehicle(s). New train {newTrainId} created with {detachedVehicles.Count} vehicle(s).");

        return (sourceTrainId, newTrainId);
    }

    /// <summary>
    /// Joins two train configurations by appending vehicles from <paramref name="train2"/>
    /// after vehicles from <paramref name="train1"/>.
    /// Train attributes are preserved from train1 and train2 is removed.
    /// </summary>
    public void JoinComposition(Guid train1, Guid train2)
    {
        EnsureNotBoundToActiveTrain(nameof(JoinComposition));
        TrainCompositionUtils.JoinTrainConfigurations(train1, train2);
    }

    /// <summary>
    /// Clears all vehicle entries.
    /// </summary>
    public TrainCompositionBuilder Clear()
    {
        EnsureNotBoundToActiveTrain(nameof(Clear));
        _vehicles.Clear();
        return this;
    }

    /// <summary>
    /// Persists the current builder state to the <c>&lt;composition&gt;</c> element in <c>train.xml</c>
    /// for the specified train UID.
    /// </summary>
    public void SaveToTrainConfiguration(Guid trainId)
    {
        EnsureNotBoundToActiveTrain(nameof(SaveToTrainConfiguration));
        TrainUtils.UpdateTrainComposition(trainId, _vehicles);
    }

    /// <summary>
    /// Builds the immutable train composition and creates the runtime vehicle instances.
    /// </summary>
    /// <param name="vehicleFactory">Factory that creates the runtime vehicle controller for each entry.</param>
    /// <param name="configureVehicle">Optional callback that can configure each controller before binding.</param>
    public TrainComposition Build(
        Func<TrainVehicle, IVehicle> vehicleFactory,
        Action<IVehicle, TrainVehicle, int, int>? configureVehicle = null)
    {
        if (vehicleFactory is null)
            throw new ArgumentNullException(nameof(vehicleFactory));

        if (_vehicles.Count == 0)
            return TrainComposition.Empty;

        var boundVehicles = new List<TrainVehicle>(_vehicles.Count);

        for (var index = 0; index < _vehicles.Count; index++)
        {
            var vehicle = _vehicles[index];
            var controller = vehicleFactory(vehicle)
                ?? throw new InvalidOperationException(
                    $"Vehicle factory returned null for vehicle UID {vehicle.VehicleId}.");
            
            configureVehicle?.Invoke(controller, vehicle, index, _vehicles.Count);

            boundVehicles.Add(vehicle with { VehicleInstance = controller });
        }

        return new TrainComposition(boundVehicles);
    }

    private static TrainVehicle Normalize(TrainVehicle vehicle, bool preserveRuntimeState)
        => preserveRuntimeState
            ? vehicle
            : vehicle with { VehicleInstance = null, Position = 0 };

    private SplitCandidate CreateSplitCandidate(int index)
    {
        var controller = EnsureVehicleController(index);
        var entry = _vehicles[index];

        if (controller is null || !controller.HasDecoder)
            return new SplitCandidate(index, entry, null, [], false);

        var decoder = controller.LocoDecoder;

        var autoCouplings = AutoCouplingUtils.GetAutoCouplingFunctions(decoder.Functions)
            .Where(function => function.Direction is VehicleDirection.Forward or VehicleDirection.Backward)
            .ToArray();

        return new SplitCandidate(index, entry, controller, autoCouplings, autoCouplings.Length > 0);
    }

    private static VehicleDirection ResolveCurrentDirection(SplitCandidate candidate1, SplitCandidate candidate2)
    {
        var direction = SelectValidDirection(candidate1.Controller)
                        ?? SelectValidDirection(candidate2.Controller)
                        ?? throw new InvalidOperationException(
                            "Unable to resolve current decoder direction for split operation.");

        return direction;
    }

    private static VehicleDirection? SelectValidDirection(IVehicle? controller)
        => controller?.Direction is VehicleDirection.Forward or VehicleDirection.Backward
            ? controller.Direction
            : null;

    private async Task ExecuteUncouplingAsync(
        SplitCandidate selected,
        VehicleDirection direction,
        CancellationToken cancellationToken)
    {
        var controller = selected.Controller ?? EnsureVehicleController(selected.Index)
            ?? throw new InvalidOperationException(
                $"Vehicle instance for UID {selected.Entry.VehicleId} is not available for uncoupling.");

        if (!controller.HasDecoder)
            throw new InvalidOperationException(
                $"Vehicle {selected.Entry.VehicleId} requires a decoder for uncoupling operations.");

        var decoder = controller.LocoDecoder;

        var availableAutoCouplings = selected.AutoCouplings.Count > 0
            ? selected.AutoCouplings
            : AutoCouplingUtils.GetAutoCouplingFunctions(decoder.Functions)
                .Where(function => function.Direction is VehicleDirection.Forward or VehicleDirection.Backward)
                .ToArray();

        var couplingFunction = AutoCouplingUtils.FindAutoCouplingFunction(availableAutoCouplings, direction);
        if (couplingFunction is null)
            throw new InvalidOperationException(
                $"No auto coupling function for vehicle {selected.Entry.VehicleId} in direction {direction}.");

        if (controller.Direction != direction)
        {
            var trainDirection = ResolveTrainDirection(direction, selected.Entry.Orientation);
            await controller.SetDirectionAsync(trainDirection, selected.Entry.Orientation,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await AutoCouplingUtils.ApplyAutoCouplingAsync(controller, couplingFunction.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private IVehicle? EnsureVehicleController(int index)
    {
        var entry = _vehicles[index];
        if (entry.VehicleInstance is { } existing)
            return existing;

        if (_vehicleFactory is null)
            return null;

        var controller = _vehicleFactory(entry)
            ?? throw new InvalidOperationException($"Vehicle factory returned null for UID {entry.VehicleId}.");

        if (_commandStation is not null && controller.HasDecoder)
            controller.LocoDecoder.SubscribeCommandStationAsync(_commandStation).GetAwaiter().GetResult();

        _vehicles[index] = entry with { VehicleInstance = controller };
        return controller;
    }

    private static TrainDirection ResolveTrainDirection(VehicleDirection decoderDirection, VehicleOrientation orientation)
    {
        return orientation switch
        {
            VehicleOrientation.Normal => decoderDirection == VehicleDirection.Forward
                ? TrainDirection.A
                : TrainDirection.B,
            VehicleOrientation.Reverse => decoderDirection == VehicleDirection.Forward
                ? TrainDirection.B
                : TrainDirection.A,
            _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation,
                $"Unsupported vehicle orientation: {orientation}")
        };
    }

    private static VehicleDirection OppositeDirection(VehicleDirection direction)
        => direction switch
        {
            VehicleDirection.Forward => VehicleDirection.Backward,
            VehicleDirection.Backward => VehicleDirection.Forward,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction,
                "Direction must be Forward or Backward.")
        };

    private (List<TrainVehicle> RemainingVehicles, List<TrainVehicle> DetachedVehicles) BuildSplitParts(int index1, int index2)
    {
        var remainingVehicles = new List<TrainVehicle>();
        var detachedVehicles = new List<TrainVehicle>();

        if (index1 < index2)
        {
            for (var index = 0; index <= index1; index++)
                remainingVehicles.Add(_vehicles[index]);

            for (var index = index2; index < _vehicles.Count; index++)
                detachedVehicles.Add(_vehicles[index]);
        }
        else
        {
            for (var index = index1; index < _vehicles.Count; index++)
                remainingVehicles.Add(_vehicles[index]);

            for (var index = 0; index <= index2; index++)
                detachedVehicles.Add(_vehicles[index]);
        }

        if (remainingVehicles.Count == 0 || detachedVehicles.Count == 0)
            throw new InvalidOperationException("SplitComposition produced an empty composition part, which is not allowed.");

        return (remainingVehicles, detachedVehicles);
    }

    private int FindIndexOrThrow(Guid vehicleId)
    {
        var index = _vehicles.FindIndex(vehicle => vehicle.VehicleId.Equals(vehicleId));
        if (index >= 0)
            return index;

        throw new KeyNotFoundException($"Vehicle UID {vehicleId} is not part of the composition builder.");
    }

    private void EnsureNotBoundToActiveTrain(string operationName)
    {
        if (_vehicles.Any(vehicle => vehicle.VehicleInstance is not null))
        {
            throw new InvalidOperationException(
                $"{operationName} is only allowed for detached compositions. " +
                "Detach the composition from an active Train instance first (only allowed in OperatingMode.ShutDown).");
        }
    }

    private readonly record struct SplitCandidate(
        int Index,
        TrainVehicle Entry,
        IVehicle? Controller,
        IReadOnlyList<AutoCouplingFunction> AutoCouplings,
        bool HasAutoCoupling);
}
