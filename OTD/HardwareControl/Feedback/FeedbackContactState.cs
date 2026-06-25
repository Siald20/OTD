// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - FeedbackControl
// Copyright (C) 2026

using System;

namespace OTD.HardwareControl.FeedbackConfiguration;

/// <summary>
/// Persistent state of one S88 feedback contact (typically one section of track).
/// </summary>
public sealed record FeedbackContactState(
    int ModuleAddress,
    int ContactNumber,
    bool IsOccupied,
    DateTime LastUpdated)
{
    /// <summary>
    /// Unique key combining module address and contact number.
    /// </summary>
    public string Key => $"{ModuleAddress:D3}:{ContactNumber:D2}";

    /// <summary>
    /// Creates a new state from an S88 contact change event.
    /// </summary>
    public static FeedbackContactState FromEvent(int moduleAddress, int contactNumber, bool isOccupied)
        => new(moduleAddress, contactNumber, isOccupied, DateTime.UtcNow);
}

