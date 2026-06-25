// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - FeedbackControl
// Copyright (C) 2026

using System;
using System.Collections.Generic;
using System.Linq;
using OTD.HardwareControl.Drivers;

namespace OTD.HardwareControl.FeedbackConfiguration;

/// <summary>
/// Manages feedback contact state and aggregates S88 events from one or more providers.
/// </summary>
internal sealed class FeedbackManager : IDisposable
{
    private readonly Dictionary<string, FeedbackContactState> _states = [];
    private bool _disposed;

    /// <summary>
    /// Raised when a contact changes state.
    /// </summary>
    public event EventHandler<FeedbackContactStateChangedEventArgs>? ContactStateChanged;

    /// <summary>
    /// Raised when a complete module state is received.
    /// </summary>
    public event EventHandler<FeedbackModuleStateReceivedEventArgs>? ModuleStateReceived;

    /// <summary>
    /// Gets the current state of a feedback contact, or null if unknown.
    /// </summary>
    public FeedbackContactState? GetContactState(int moduleAddress, int contactNumber)
    {
        var key = BuildKey(moduleAddress, contactNumber);
        _states.TryGetValue(key, out var state);
        return state;
    }

    /// <summary>
    /// Gets all known feedback contacts.
    /// </summary>
    public IReadOnlyList<FeedbackContactState> GetAllStates()
    {
        lock (_states)
        {
            return _states.Values.OrderBy(s => s.Key).ToList();
        }
    }

    /// <summary>
    /// Gets all contacts for one S88 module.
    /// </summary>
    public IReadOnlyList<FeedbackContactState> GetModuleContacts(int moduleAddress)
    {
        lock (_states)
        {
            return _states.Values
                .Where(s => s.ModuleAddress == moduleAddress)
                .OrderBy(s => s.ContactNumber)
                .ToList();
        }
    }

    /// <summary>
    /// Subscribes the manager to all events from the given S88 commander.
    /// </summary>
    public void SubscribeTo(LoDiS88Commander commander)
    {
        ArgumentNullException.ThrowIfNull(commander);

        commander.ContactStateChanged += OnContactStateChanged;
        commander.ModuleStateReceived += OnModuleStateReceived;
    }

    /// <summary>
    /// Unsubscribes the manager from all events of the given S88 commander.
    /// </summary>
    public void UnsubscribeFrom(LoDiS88Commander commander)
    {
        ArgumentNullException.ThrowIfNull(commander);

        commander.ContactStateChanged -= OnContactStateChanged;
        commander.ModuleStateReceived -= OnModuleStateReceived;
    }

    private void OnContactStateChanged(object? sender, S88StateChangedEventArgs e)
    {
        var newState = FeedbackContactState.FromEvent(e.ModuleAddress, e.ContactNumber, e.IsOccupied);
        lock (_states)
        {
            _states[newState.Key] = newState;
        }

        ContactStateChanged?.Invoke(this, new FeedbackContactStateChangedEventArgs(newState));
    }

    private void OnModuleStateReceived(object? sender, S88ModuleStateEventArgs e)
    {
        var states = new List<FeedbackContactState>();

        for (var contactNumber = 1; contactNumber <= 16; contactNumber++)
        {
            var isOccupied = e.GetContactState(contactNumber);
            var state = FeedbackContactState.FromEvent(e.ModuleAddress, contactNumber, isOccupied);

            lock (_states)
            {
                _states[state.Key] = state;
            }

            states.Add(state);
        }

        ModuleStateReceived?.Invoke(this, new FeedbackModuleStateReceivedEventArgs(e.ModuleAddress, states));
    }

    private static string BuildKey(int moduleAddress, int contactNumber)
        => $"{moduleAddress:D3}:{contactNumber:D2}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _states.Clear();
    }
}

/// <summary>
/// Arguments for contact state change events.
/// </summary>
internal sealed class FeedbackContactStateChangedEventArgs : EventArgs
{
    /// <summary>Updated contact state.</summary>
    public FeedbackContactState State { get; }

    public FeedbackContactStateChangedEventArgs(FeedbackContactState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }
}

/// <summary>
/// Arguments for module state received events.
/// </summary>
internal sealed class FeedbackModuleStateReceivedEventArgs : EventArgs
{
    /// <summary>Module address.</summary>
    public int ModuleAddress { get; }

    /// <summary>All 16 contact states of the module.</summary>
    public IReadOnlyList<FeedbackContactState> Contacts { get; }

    public FeedbackModuleStateReceivedEventArgs(int moduleAddress, IReadOnlyList<FeedbackContactState> contacts)
    {
        ModuleAddress = moduleAddress;
        Contacts = contacts ?? throw new ArgumentNullException(nameof(contacts));
    }
}

