// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <name@example.com>
// //
// // Dieses Programm ist freie Software: Sie können es unter den Bedingungen
// // der GNU General Public License, wie von der Free Software Foundation,
// // entweder Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
// // veröffentlichten Version, weiterverbreiten und/oder modifizieren.
// //
// // Dieses Programm wird in der Hoffnung bereitgestellt, dass es nützlich sein wird,
// // jedoch OHNE JEDE GEWÄHRLEISTUNG; sogar ohne die implizite Gewährleistung der
// // MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
// // Siehe die GNU General Public License für weitere Details.
// //
// // Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
// // Programm erhalten haben. Falls nicht, siehe <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;

namespace OTD.Common;

/// <summary>
/// Log-Stufen für zentrale Diagnose und Fehlererfassung.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Normale Laufzeitinformationen, wichtige Ereignisse.
    /// Standardmäßig aktiv, nicht abschaltbar.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Auffällige, aber tolerierbare Situationen.
    /// Standardmäßig aktiv.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Fehler mit Handlungsbedarf, Abbruch, Exceptions.
    /// Standardmäßig aktiv.
    /// </summary>
    Error = 2,

    /// <summary>
    /// Zuschaltbare Entwicklerdiagnose.
    /// Standardmäßig aus, pro Kategorie aktivierbar.
    /// </summary>
    Debug = 3
}

/// <summary>
/// Haupt-Kategorien für modulares, selektives Logging.
/// </summary>
public enum LogCategory
{
    App = 0,
    Ui = 1,
    Train = 2,
    TrainDriving = 3,
    CommandStation = 4,
    Feedback = 5,
    Accessory = 6,
    Interlocking = 7,
    Configuration = 8
}

/// <summary>
/// Zentrale Logging-Engine für OTD.
/// 
/// Bietet einheitliche, thread-sichere Log-Ausgabe in Konsole und optional Datei.
/// Log-Stufen: Info (Standard), Warning, Error, Debug (zuschaltbar).
/// 
/// Nutzung:
/// <code>
/// Logging.Info(LogCategory.Train, "Zug gestartet");
/// Logging.Debug(LogCategory.CommandStation, "Paket gesendet");
/// Logging.Warning(LogCategory.Feedback, "Sensor nicht antwortet");
/// Logging.Error(LogCategory.App, "Kritischer Fehler", ex);
/// </code>
/// </summary>
public static class Logging
{
    // ========== Konfiguration ==========

    /// <summary>
    /// Aktiviert Konsolenausgabe (standardmäßig true).
    /// </summary>
    public static bool EnableConsole { get; set; } = true;

    /// <summary>
    /// Aktiviert Dateiausgabe (standardmäßig false).
    /// </summary>
    public static bool EnableFile { get; set; } = false;

    /// <summary>
    /// Pfad zur Log-Datei (z. B. "otd-2026-06-25.log").
    /// Wird nur verwendet, wenn EnableFile true ist.
    /// </summary>
    public static string? LogFilePath { get; set; }

    // ========== Interne State ==========

    private static readonly object LockObject = new object();
    private static readonly Dictionary<LogCategory, bool> DebugEnabledByCategory = new();
    private static readonly Dictionary<LogCategory, bool> ExtendedDebugEnabledByCategory = new();

    // ========== Konfigurationsmethoden ==========

    /// <summary>
    /// Aktiviert Debug-Ausgabe für eine bestimmte Kategorie.
    /// Optional kann der erweiterte Debug-Modus mitaktiviert werden.
    /// </summary>
    /// <param name="category">Die Kategorie.</param>
    /// <param name="extended">True, um erweiterte Debug-Ausgabe zu aktivieren.</param>
    public static void EnableDebugForCategory(LogCategory category, bool extended = false)
    {
        lock (LockObject)
        {
            DebugEnabledByCategory[category] = true;
            ExtendedDebugEnabledByCategory[category] = extended;
        }
    }

    /// <summary>
    /// Deaktiviert Debug-Ausgabe für eine bestimmte Kategorie.
    /// </summary>
    /// <param name="category">Die Kategorie.</param>
    public static void DisableDebugForCategory(LogCategory category)
    {
        lock (LockObject)
        {
            DebugEnabledByCategory[category] = false;
            ExtendedDebugEnabledByCategory[category] = false;
        }
    }

    /// <summary>
    /// Prüft, ob Debug für eine Kategorie aktiviert ist.
    /// </summary>
    /// <param name="category">Die Kategorie.</param>
    /// <returns>true, wenn Debug für diese Kategorie aktiv ist.</returns>
    public static bool IsDebugEnabled(LogCategory category)
    {
        lock (LockObject)
        {
            return DebugEnabledByCategory.TryGetValue(category, out var enabled) && enabled;
        }
    }

    /// <summary>
    /// Prüft, ob erweiterter Debug für eine Kategorie aktiviert ist.
    /// </summary>
    /// <param name="category">Die Kategorie.</param>
    /// <returns>true, wenn erweiterter Debug für diese Kategorie aktiv ist.</returns>
    public static bool IsExtendedDebugEnabled(LogCategory category)
    {
        lock (LockObject)
        {
            return ExtendedDebugEnabledByCategory.TryGetValue(category, out var enabled) && enabled;
        }
    }

    /// <summary>
    /// Prüft, ob ein Log-Level für eine Kategorie ausgegeben werden soll.
    /// Info, Warning, Error sind immer aktiviert.
    /// Debug muss explizit pro Kategorie aktiviert werden.
    /// </summary>
    /// <param name="category">Die Kategorie.</param>
    /// <param name="level">Die Log-Stufe.</param>
    /// <returns>true, wenn der Log ausgegeben werden soll.</returns>
    public static bool IsEnabled(LogCategory category, LogLevel level)
    {
        if (level != LogLevel.Debug)
            return true;

        return IsDebugEnabled(category);
    }

    // ========== Log-Methoden ==========

    /// <summary>
    /// Protokolliert eine Info-Nachricht (Standard, immer aktiv).
    /// </summary>
    public static void Info(LogCategory category, string message)
    {
        LogInternal(category, LogLevel.Info, message, null);
    }

    /// <summary>
    /// Protokolliert eine Warning-Nachricht.
    /// </summary>
    public static void Warning(LogCategory category, string message)
    {
        LogInternal(category, LogLevel.Warning, message, null);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht.
    /// </summary>
    public static void Error(LogCategory category, string message)
    {
        LogInternal(category, LogLevel.Error, message, null);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht mit Exception-Details.
    /// </summary>
    public static void Error(LogCategory category, string message, Exception? ex)
    {
        var fullMessage = ex is not null
            ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}"
            : message;
        LogInternal(category, LogLevel.Error, fullMessage, ex);
    }

    /// <summary>
    /// Protokolliert eine Debug-Nachricht (nur wenn Debug für die Kategorie aktiv ist).
    /// </summary>
    public static void Debug(LogCategory category, string message)
    {
        if (!IsEnabled(category, LogLevel.Debug))
            return;

        LogInternal(category, LogLevel.Debug, message, null);
    }

    /// <summary>
    /// Protokolliert eine erweiterte Debug-Nachricht
    /// (nur wenn Debug und Extended-Debug für die Kategorie aktiv sind).
    /// </summary>
    public static void DebugExtended(LogCategory category, string message)
    {
        if (!IsDebugEnabled(category) || !IsExtendedDebugEnabled(category))
            return;

        LogInternal(category, LogLevel.Debug, message, null, isExtendedDebug: true);
    }

    // ========== Interne Implementierung ==========

    private static void LogInternal(
        LogCategory category,
        LogLevel level,
        string message,
        Exception? ex,
        bool isExtendedDebug = false)
    {
        lock (LockObject)
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelStr = isExtendedDebug ? "DEBUG+" : level.ToString().ToUpperInvariant();
            var categoryStr = category.ToString();

            var formattedMessage = $"{timestamp} [{levelStr}] [{categoryStr}] {message}";

            // Konsole
            if (EnableConsole)
            {
                // Farbcodierung für visuelle Unterscheidung (optional, nur bei Konsole)
                var originalForeground = Console.ForegroundColor;
                try
                {
                    switch (level)
                    {
                        case LogLevel.Warning:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            break;
                        case LogLevel.Error:
                            Console.ForegroundColor = ConsoleColor.Red;
                            break;
                        case LogLevel.Debug:
                            Console.ForegroundColor = isExtendedDebug
                                ? ConsoleColor.DarkCyan
                                : ConsoleColor.Cyan;
                            break;
                        default:
                            Console.ForegroundColor = originalForeground;
                            break;
                    }

                    Console.WriteLine(formattedMessage);

                    // Exception-Stacktrace bei Debug und Error
                    if (ex is not null && (level == LogLevel.Debug || level == LogLevel.Error))
                    {
                        Console.WriteLine($"  Stacktrace: {ex.StackTrace}");
                    }
                }
                finally
                {
                    Console.ForegroundColor = originalForeground;
                }
            }

            // Datei
            if (EnableFile && !string.IsNullOrWhiteSpace(LogFilePath))
            {
                try
                {
                    var fileMessage = ex is not null
                        ? $"{formattedMessage}\n  Stacktrace: {ex.StackTrace}"
                        : formattedMessage;

                    System.IO.File.AppendAllText(LogFilePath, fileMessage + Environment.NewLine);
                }
                catch
                {
                    // Fehler beim Schreiben in Datei: ignorieren, um Logging-Fehler nicht zu propagieren
                }
            }
        }
    }
}