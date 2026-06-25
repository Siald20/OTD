// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - FeedbackControl
// Copyright (C) 2026

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl.Drivers;

namespace OTD.HardwareControl.FeedbackConfiguration;

/// <summary>
/// Orchestrates feedback providers and centralizes all feedback state management.
/// </summary>
internal sealed class FeedbackService : IDisposable
{
    private readonly FeedbackManager _manager = new();
    private readonly List<LoDiS88Commander> _commanders = [];
    private bool _disposed;

    /// <summary>
    /// Raised when a feedback contact state changes.
    /// </summary>
    public event EventHandler<FeedbackContactStateChangedEventArgs>? ContactStateChanged
    {
        add => _manager.ContactStateChanged += value;
        remove => _manager.ContactStateChanged -= value;
    }

    /// <summary>
    /// Raised when a complete module state is received.
    /// </summary>
    public event EventHandler<FeedbackModuleStateReceivedEventArgs>? ModuleStateReceived
    {
        add => _manager.ModuleStateReceived += value;
        remove => _manager.ModuleStateReceived -= value;
    }

    /// <summary>
    /// Gets the underlying feedback manager.
    /// </summary>
    public FeedbackManager Manager => _manager;

    /// <summary>
    /// Loads all feedback providers, connects them, and starts event collection.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown for missing configuration or connection failures.</exception>
    public async Task InitializeAsync(string? configFilePath = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var providers = FeedbackConfigLoader.LoadProviders(configFilePath);

            foreach (var provider in providers)
            {
                var commander = new LoDiS88Commander();
                _commanders.Add(commander);
                _manager.SubscribeTo(commander);

                await InitializeProviderAsync(commander, provider, cancellationToken)
                    .ConfigureAwait(false);
            }

            Console.WriteLine($"Feedback service initialized with {_commanders.Count} provider(s).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing feedback service: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Initializes one provider (connect + query/subscribe modules).
    /// </summary>
    private static async Task InitializeProviderAsync(
        LoDiS88Commander commander,
        FeedbackProviderConfig provider,
        CancellationToken cancellationToken)
    {
        try
        {
            await FeedbackInitializer.InitializeLoDiS88Async(commander, provider, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine($"Feedback provider '{provider.Uid}' initialized @ {provider.Connection.IpAddress}:{provider.Connection.Port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing provider '{provider.Uid}': {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Disconnects all feedback providers and stops event collection.
    /// </summary>
    public async Task ShutdownAsync()
    {
        foreach (var commander in _commanders)
        {
            _manager.UnsubscribeFrom(commander);
            try
            {
                await commander.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disconnecting feedback provider: {ex.Message}");
            }
        }

        _commanders.Clear();
        Console.WriteLine("Feedback service shut down.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { ShutdownAsync().Wait(TimeSpan.FromSeconds(5)); }
        catch { /* ignore */ }

        foreach (var commander in _commanders)
            commander.Dispose();

        _manager.Dispose();
    }
}

