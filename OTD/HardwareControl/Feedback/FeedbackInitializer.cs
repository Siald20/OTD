// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - FeedbackControl
// Copyright (C) 2026

using System;
using System.Threading;
using System.Threading.Tasks;
using OTD.HardwareControl.CommandStation.LoDi;

namespace OTD.HardwareControl.Feedback;

/// <summary>
/// Applies loaded feedback configuration to concrete feedback drivers.
/// </summary>
public static class FeedbackInitializer
{
    /// <summary>
    /// Connects and initializes a LoDi S88 commander from one provider config.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the provider is not of driver type lodi-s88-commander.</exception>
    public static async Task InitializeLoDiS88Async(
        LoDiS88Commander commander,
        FeedbackProviderConfig provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commander);
        ArgumentNullException.ThrowIfNull(provider);

        if (!string.Equals(provider.Driver, FeedbackConfigLoader.DriverLoDiS88Commander, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Provider '{provider.Uid}' has unsupported driver '{provider.Driver}' for LoDi S88 initialization.");
        }

        await commander.ConnectAsync(provider.Connection.IpAddress, provider.Connection.Port, cancellationToken)
            .ConfigureAwait(false);

        foreach (var moduleAddress in provider.Modules)
        {
            if (provider.Startup.QueryOnStartup)
            {
                await commander.QueryModuleAsync(moduleAddress, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (provider.Startup.SubscribeOnStartup)
            {
                await commander.SubscribeModuleAsync(moduleAddress, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}

