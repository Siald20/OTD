// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - FeedbackControl
// Copyright (C) 2026

using System.Collections.Generic;

namespace OTD.HardwareControl.FeedbackConfiguration;

/// <summary>
/// Connection parameters for a feedback provider.
/// </summary>
public readonly record struct FeedbackConnectionConfig(string IpAddress, int Port);

/// <summary>
/// Startup behavior for a feedback provider.
/// </summary>
public readonly record struct FeedbackStartupConfig(bool QueryOnStartup, bool SubscribeOnStartup);

/// <summary>
/// Configuration model for one feedback provider entry.
/// </summary>
public sealed record FeedbackProviderConfig(
    string Uid,
    string Driver,
    FeedbackConnectionConfig Connection,
    FeedbackStartupConfig Startup,
    IReadOnlyList<int> Modules);

