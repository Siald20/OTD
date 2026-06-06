// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OTD.HardwareControl.Train;

/// <summary>
/// Defines how drive and function commands are routed to command stations.
/// </summary>
public enum RoutingMode
{
    /// <summary>Commands are sent to the primary command station only.</summary>
    PrimaryOnly,
    /// <summary>Commands are mirrored to both primary and secondary command stations simultaneously.</summary>
    MirrorBoth,
    /// <summary>Commands are sent to the secondary command station only.</summary>
    SecondaryOnly
}

/// <summary>
/// Identifies the type of a dispatched train command.
/// </summary>
public enum TrainCommandKind
{
    /// <summary>A drive command (speed step and direction).</summary>
    Drive,
    /// <summary>A decoder function command.</summary>
    Function,
    /// <summary>An emergency stop command.</summary>
    EmergencyStop
}

/// <summary>
/// Event arguments raised after a train command was successfully dispatched to a command station.
/// </summary>
public sealed class TrainCommandDispatchedEventArgs : EventArgs
{
    /// <summary>
    /// Creates a new instance with the given dispatch context.
    /// </summary>
    public TrainCommandDispatchedEventArgs(
        TrainCommandKind kind,
        Guid trainId,
        Guid? locoId,
        int address,
        string stationName,
        RoutingMode mode)
    {
        Kind = kind;
        TrainId = trainId;
        LocoId = locoId;
        Address = address;
        StationName = stationName;
        Mode = mode;
        TimestampUtc = DateTime.UtcNow;
    }

    /// <summary>Kind of command that was dispatched.</summary>
    public TrainCommandKind Kind { get; }
    /// <summary>Identifier of the train that originated the command.</summary>
    public Guid TrainId { get; }
    /// <summary>Identifier of the locomotive, if applicable.</summary>
    public Guid? LocoId { get; }
    /// <summary>LocoDecoder address the command was sent to.</summary>
    public int Address { get; }
    /// <summary>Name of the command station that received the command.</summary>
    public string StationName { get; }
    /// <summary>Routing mode that was active when the command was sent.</summary>
    public RoutingMode Mode { get; }
    /// <summary>UTC timestamp at which the command was dispatched.</summary>
    public DateTime TimestampUtc { get; }
}

/// <summary>
/// Event arguments raised when dispatching a train command to a command station fails.
/// </summary>
public sealed class TrainCommandFailedEventArgs : EventArgs
{
    /// <summary>
    /// Creates a new instance with the given failure context.
    /// </summary>
    public TrainCommandFailedEventArgs(
        TrainCommandKind kind,
        Guid trainId,
        Guid? locoId,
        int address,
        string stationName,
        RoutingMode mode,
        Exception exception)
    {
        Kind = kind;
        TrainId = trainId;
        LocoId = locoId;
        Address = address;
        StationName = stationName;
        Mode = mode;
        Exception = exception;
        TimestampUtc = DateTime.UtcNow;
    }

    /// <summary>Kind of command that failed.</summary>
    public TrainCommandKind Kind { get; }
    /// <summary>Identifier of the train that originated the command.</summary>
    public Guid TrainId { get; }
    /// <summary>Identifier of the locomotive, if applicable.</summary>
    public Guid? LocoId { get; }
    /// <summary>LocoDecoder address the command was sent to.</summary>
    public int Address { get; }
    /// <summary>Name of the command station that was targeted.</summary>
    public string StationName { get; }
    /// <summary>Routing mode that was active when the command failed.</summary>
    public RoutingMode Mode { get; }
    /// <summary>Exception that caused the failure.</summary>
    public Exception Exception { get; }
    /// <summary>UTC timestamp at which the failure occurred.</summary>
    public DateTime TimestampUtc { get; }
}

/// <summary>
/// Contract for routing train commands to one or more command stations.
/// </summary>
public interface ITrainCommandRouter
{
    /// <summary>Currently active routing mode.</summary>
    RoutingMode Mode { get; }

    /// <summary>Raised after a command was successfully dispatched to a command station.</summary>
    event EventHandler<TrainCommandDispatchedEventArgs>? CommandDispatched;

    /// <summary>Raised when dispatching a command to a command station fails.</summary>
    event EventHandler<TrainCommandFailedEventArgs>? CommandFailed;

    /// <summary>Changes the active routing mode.</summary>
    void SetMode(RoutingMode mode);

    /// <summary>
    /// Sends a drive command (speed step and direction) to the specified decoder address.
    /// </summary>
    Task SendDriveAsync(
        Guid trainId,
        Guid locoId,
        int address,
        int speedStep,
        VehicleDirection direction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a function state command to the specified decoder address.
    /// </summary>
    Task SendFunctionAsync(
        Guid trainId,
        Guid locoId,
        int address,
        int function,
        FunctionState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an emergency stop command to the specified decoder address.
    /// </summary>
    Task EmergencyStopLocoAsync(
        Guid trainId,
        int address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an emergency stop command to all given decoder addresses.
    /// </summary>
    Task EmergencyStopTrainAsync(
        Guid trainId,
        IReadOnlyCollection<int> addresses,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Routes train commands to a primary and an optional secondary command station.
/// The active <see cref="RoutingMode"/> determines whether commands are sent to the
/// primary only, mirrored to both, or forwarded to the secondary only.
/// </summary>
public sealed class TrainCommandRouter : ITrainCommandRouter
{
    private readonly CommandStation.CommandStation _primary;
    private readonly CommandStation.CommandStation? _secondary;

    /// <summary>
    /// Creates a new router with a mandatory primary and an optional secondary command station.
    /// </summary>
    /// <param name="primary">Primary command station. Must not be null.</param>
    /// <param name="secondary">Optional secondary command station for mirroring.</param>
    public TrainCommandRouter(CommandStation.CommandStation primary, CommandStation.CommandStation? secondary = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        _primary = primary;
        _secondary = secondary;
    }

    /// <inheritdoc/>
    public RoutingMode Mode { get; private set; } = RoutingMode.PrimaryOnly;

    /// <inheritdoc/>
    public event EventHandler<TrainCommandDispatchedEventArgs>? CommandDispatched;
    /// <inheritdoc/>
    public event EventHandler<TrainCommandFailedEventArgs>? CommandFailed;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="RoutingMode.SecondaryOnly"/> is requested but no secondary station is configured.
    /// </exception>
    public void SetMode(RoutingMode mode)
    {
        if (mode == RoutingMode.SecondaryOnly && _secondary == null)
            throw new InvalidOperationException("SecondaryOnly ist ohne Secondary-Zentrale nicht moeglich.");

        Mode = mode;
    }

    /// <inheritdoc/>
    public Task SendDriveAsync(
        Guid trainId,
        Guid locoId,
        int address,
        int speedStep,
        VehicleDirection direction,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            TrainCommandKind.Drive,
            trainId,
            locoId,
            address,
            station => station.SetLocoSpeedAsync(address, speedStep, direction, cancellationToken),
            cancellationToken);

    /// <inheritdoc/>
    public Task SendFunctionAsync(
        Guid trainId,
        Guid locoId,
        int address,
        int function,
        FunctionState state,
        CancellationToken cancellationToken = default)
    {
        bool isOn = state == FunctionState.On;
        return DispatchAsync(
            TrainCommandKind.Function,
            trainId,
            locoId,
            address,
            station => station.SetLocoFunctionAsync(address, function, isOn, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task EmergencyStopLocoAsync(
        Guid trainId,
        int address,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            TrainCommandKind.EmergencyStop,
            trainId,
            locoId: null,
            address,
            station => station.EmergencyStopAsync(address, cancellationToken),
            cancellationToken);

    /// <inheritdoc/>
    public async Task EmergencyStopTrainAsync(
        Guid trainId,
        IReadOnlyCollection<int> addresses,
        CancellationToken cancellationToken = default)
    {
        if (addresses == null || addresses.Count == 0)
            return;

        foreach (var address in addresses.Where(a => a > 0).Distinct())
        {
            await EmergencyStopLocoAsync(trainId, address, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchAsync(
        TrainCommandKind kind,
        Guid trainId,
        Guid? locoId,
        int address,
        Func<CommandStation.CommandStation, Task> sendAction,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (address <= 0)
            throw new ArgumentOutOfRangeException(nameof(address), "Lokadresse muss > 0 sein.");

        var targets = ResolveTargets();
        if (targets.Count == 0)
            throw new InvalidOperationException("Kein gueltiges Routing-Ziel vorhanden.");

        if (Mode == RoutingMode.MirrorBoth && targets.Count > 1)
        {
            var tasks = targets.Select(t => SendToOneAsync(kind, trainId, locoId, address, t.Name, t.Station, sendAction));
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return;
        }

        var target = targets[0];
        await SendToOneAsync(kind, trainId, locoId, address, target.Name, target.Station, sendAction).ConfigureAwait(false);
    }

    private async Task SendToOneAsync(
        TrainCommandKind kind,
        Guid trainId,
        Guid? locoId,
        int address,
        string stationName,
        CommandStation.CommandStation station,
        Func<CommandStation.CommandStation, Task> sendAction)
    {
        try
        {
            await sendAction(station).ConfigureAwait(false);
            CommandDispatched?.Invoke(this, new TrainCommandDispatchedEventArgs(
                kind, trainId, locoId, address, stationName, Mode));
        }
        catch (Exception ex)
        {
            CommandFailed?.Invoke(this, new TrainCommandFailedEventArgs(
                kind, trainId, locoId, address, stationName, Mode, ex));
            throw;
        }
    }

    private List<(string Name, CommandStation.CommandStation Station)> ResolveTargets()
    {
        return Mode switch
        {
            RoutingMode.PrimaryOnly =>
                new List<(string, CommandStation.CommandStation)> { ("Primary", _primary) },
            RoutingMode.SecondaryOnly when _secondary != null =>
                new List<(string, CommandStation.CommandStation)> { ("Secondary", _secondary) },
            RoutingMode.MirrorBoth when _secondary != null =>
                new List<(string, CommandStation.CommandStation)>
                {
                    ("Primary", _primary),
                    ("Secondary", _secondary)
                },
            _ => new List<(string, CommandStation.CommandStation)> { ("Primary", _primary) }
        };
    }
}
