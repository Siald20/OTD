// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026

using System;
using OTD.Common;

namespace OTD.HardwareControl.Drivers;

/// <summary>
/// Centralized logging helpers for LoDi command station and feedback modules.
/// Keeps message format and categories consistent across LoDi components.
/// </summary>
internal static class LoDiLog
{
    public static void CommandDebug(string channel, string message)
    {
        var normalizedChannel = string.IsNullOrWhiteSpace(channel)
            ? "DEBUG"
            : channel.Trim().ToUpperInvariant();

        Logging.Debug(typeof(LoDiLog),
            $"[LoDi {normalizedChannel}] {message}");
    }

    public static void CommandInfo(string message)
    {
        Logging.Info(typeof(LoDiLog), $"[LoDi INFO] {message}");
    }

    public static void CommandWarning(string message)
    {
        Logging.Warning(typeof(LoDiLog), $"[LoDi WARN] {message}");
    }

    public static void CommandError(string message, Exception ex)
    {
        Logging.Error(typeof(LoDiLog), $"[LoDi ERROR] {message}", ex);
    }

    public static void FeedbackDebug(string message)
    {
        Logging.Debug(typeof(LoDiLog), $"[LoDiFeedback DEBUG] {message}");
    }
}


