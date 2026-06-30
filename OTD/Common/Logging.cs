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
using System.Diagnostics;
using System.Runtime.CompilerServices;

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
    /// Standardmäßig aus, pro Namespace aktivierbar.
    /// </summary>
    Debug = 3
}

/// <summary>
/// Zentrale Logging-Engine für OTD.
/// 
/// Bietet einheitliche, thread-sichere Log-Ausgabe in Konsole und optional Datei.
/// Log-Stufen: Info (Standard), Warning, Error, Debug (zuschaltbar).
/// 
/// Nutzung:
/// <code>
/// Logging.EnableDebugFor&lt;OTD.TrainDriving.TrainDriving&gt;(extended: true);
/// Logging.Info("Zug gestartet");
/// Logging.Debug("Paket gesendet");
/// Logging.DebugExtended("Raw payload: ...");
/// Logging.Error("Kritischer Fehler", ex);
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
    private static readonly Dictionary<string, (bool Enabled, bool Extended)> DebugEnabledByNamespace = new(StringComparer.Ordinal);

    // ========== Konfigurationsmethoden ==========

    /// <summary>
    /// Aktiviert Debug-Ausgabe fuer ein Namespace-Praefix.
    /// Der Schalter gilt auch fuer alle Unter-Namespaces.
    /// </summary>
    /// <param name="namespacePrefix">Namespace oder Namespace-Praefix (z. B. OTD.TrainDriving).</param>
    /// <param name="extended">True, um erweiterte Debug-Ausgabe zu aktivieren.</param>
    public static void EnableDebugForNamespace(string namespacePrefix, bool extended = false)
    {
        lock (LockObject)
        {
            var key = NormalizeNamespace(namespacePrefix);
            DebugEnabledByNamespace[key] = (Enabled: true, Extended: extended);
        }
    }

    /// <summary>
    /// Deaktiviert Debug-Ausgabe fuer ein Namespace-Praefix.
    /// Ein spezifischeres Praefix kann damit ein uebergeordnetes Praefix ueberschreiben.
    /// </summary>
    /// <param name="namespacePrefix">Namespace oder Namespace-Praefix.</param>
    public static void DisableDebugForNamespace(string namespacePrefix)
    {
        lock (LockObject)
        {
            var key = NormalizeNamespace(namespacePrefix);
            DebugEnabledByNamespace[key] = (Enabled: false, Extended: false);
        }
    }

    /// <summary>
    /// Aktiviert Debug-Ausgabe fuer den Namespace eines Typs.
    /// Der Schalter gilt auch fuer alle Unter-Namespaces.
    /// </summary>
    public static void EnableDebugFor<T>(bool extended = false)
    {
        EnableDebugForNamespace(GetNamespace(typeof(T)), extended);
    }

    /// <summary>
    /// Aktiviert Debug-Ausgabe fuer den Namespace eines Typs.
    /// Der Schalter gilt auch fuer alle Unter-Namespaces.
    /// </summary>
    public static void EnableDebugFor(Type type, bool extended = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        EnableDebugForNamespace(GetNamespace(type), extended);
    }

    /// <summary>
    /// Deaktiviert Debug-Ausgabe fuer den Namespace eines Typs.
    /// </summary>
    public static void DisableDebugFor<T>()
    {
        DisableDebugForNamespace(GetNamespace(typeof(T)));
    }

    /// <summary>
    /// Deaktiviert Debug-Ausgabe fuer den Namespace eines Typs.
    /// </summary>
    public static void DisableDebugFor(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        DisableDebugForNamespace(GetNamespace(type));
    }

    /// <summary>
    /// Prueft, ob Debug fuer ein Namespace-Praefix aktiviert ist.
    /// </summary>
    /// <param name="namespaceName">Konkreter Namespace des Aufrufers.</param>
    /// <returns>true, wenn Debug fuer den Namespace (oder ein passendes Praefix) aktiv ist.</returns>
    public static bool IsDebugEnabled(string namespaceName)
    {
        var normalizedNamespace = NormalizeNamespace(namespaceName);

        lock (LockObject)
        {
            if (!TryGetBestNamespaceRule(normalizedNamespace, out var rule))
                return false;

            return rule.Enabled;
        }
    }

    /// <summary>
    /// Prueft, ob erweiterter Debug fuer ein Namespace-Praefix aktiviert ist.
    /// </summary>
    /// <param name="namespaceName">Konkreter Namespace des Aufrufers.</param>
    /// <returns>true, wenn Extended-Debug fuer den Namespace (oder ein passendes Praefix) aktiv ist.</returns>
    public static bool IsExtendedDebugEnabled(string namespaceName)
    {
        var normalizedNamespace = NormalizeNamespace(namespaceName);

        lock (LockObject)
        {
            if (!TryGetBestNamespaceRule(normalizedNamespace, out var rule))
                return false;

            return rule.Enabled && rule.Extended;
        }
    }

    /// <summary>
    /// Prueft, ob ein Log-Level fuer einen Namespace ausgegeben werden soll.
    /// Info, Warning, Error sind immer aktiviert.
    /// Debug muss explizit fuer Namespace-Praefixe aktiviert werden.
    /// </summary>
    /// <param name="namespaceName">Konkreter Namespace des Aufrufers.</param>
    /// <param name="level">Die Log-Stufe.</param>
    /// <returns>true, wenn der Log ausgegeben werden soll.</returns>
    public static bool IsEnabled(string namespaceName, LogLevel level)
    {
        if (level != LogLevel.Debug)
            return true;

        return IsDebugEnabled(namespaceName);
    }

    // ========== Log-Methoden ==========

    /// <summary>
    /// Protokolliert eine Info-Nachricht (Standard, immer aktiv).
    /// </summary>
    public static void Info(string namespaceName, string message)
    {
        LogInternal(NormalizeNamespace(namespaceName), LogLevel.Info, message, null);
    }

    /// <summary>
    /// Protokolliert eine Info-Nachricht unter Verwendung des aufrufenden Namespaces.
    /// </summary>
    public static void Info(string message)
    {
        Info(ResolveCallerNamespace(), message);
    }

    /// <summary>
    /// Protokolliert eine Info-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void Info<T>(string message)
    {
        Info(GetNamespace(typeof(T)), message);
    }

    /// <summary>
    /// Protokolliert eine Info-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void Info(Type sourceType, string message)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        Info(GetNamespace(sourceType), message);
    }

    /// <summary>
    /// Protokolliert eine Warning-Nachricht.
    /// </summary>
    public static void Warning(string namespaceName, string message)
    {
        LogInternal(NormalizeNamespace(namespaceName), LogLevel.Warning, message, null);
    }

    /// <summary>
    /// Protokolliert eine Warning-Nachricht unter Verwendung des aufrufenden Namespaces.
    /// </summary>
    public static void Warning(string message)
    {
        Warning(ResolveCallerNamespace(), message);
    }

    /// <summary>
    /// Protokolliert eine Warning-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void Warning<T>(string message)
    {
        Warning(GetNamespace(typeof(T)), message);
    }

    /// <summary>
    /// Protokolliert eine Warning-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void Warning(Type sourceType, string message)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        Warning(GetNamespace(sourceType), message);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht.
    /// </summary>
    public static void Error(string namespaceName, string message)
    {
        LogInternal(NormalizeNamespace(namespaceName), LogLevel.Error, message, null);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht unter Verwendung des aufrufenden Namespaces.
    /// </summary>
    public static void Error(string message)
    {
        Error(ResolveCallerNamespace(), message);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void Error<T>(string message)
    {
        Error(GetNamespace(typeof(T)), message);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void Error(Type sourceType, string message)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        Error(GetNamespace(sourceType), message);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht mit Exception-Details.
    /// </summary>
    public static void Error(string namespaceName, string message, Exception? ex)
    {
        var fullMessage = ex is not null
            ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}"
            : message;
        LogInternal(NormalizeNamespace(namespaceName), LogLevel.Error, fullMessage, ex);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht mit Exception-Details unter Verwendung des aufrufenden Namespaces.
    /// </summary>
    public static void Error(string message, Exception? ex)
    {
        Error(ResolveCallerNamespace(), message, ex);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht mit Exception-Details mit Namespace aus einem Typ.
    /// </summary>
    public static void Error<T>(string message, Exception? ex)
    {
        Error(GetNamespace(typeof(T)), message, ex);
    }

    /// <summary>
    /// Protokolliert eine Error-Nachricht mit Exception-Details mit Namespace aus einem Typ.
    /// </summary>
    public static void Error(Type sourceType, string message, Exception? ex)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        Error(GetNamespace(sourceType), message, ex);
    }

    /// <summary>
    /// Protokolliert eine Debug-Nachricht (nur wenn Debug für den Namespace aktiv ist).
    /// </summary>
    public static void Debug(string namespaceName, string message)
    {
        var normalizedNamespace = NormalizeNamespace(namespaceName);

        if (!IsEnabled(normalizedNamespace, LogLevel.Debug))
            return;

        LogInternal(normalizedNamespace, LogLevel.Debug, message, null);
    }

    /// <summary>
    /// Protokolliert eine Debug-Nachricht unter Verwendung des aufrufenden Namespaces.
    /// </summary>
    public static void Debug(string message)
    {
        Debug(ResolveCallerNamespace(), message);
    }

    /// <summary>
    /// Protokolliert eine Debug-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void Debug<T>(string message)
    {
        Debug(GetNamespace(typeof(T)), message);
    }

    /// <summary>
    /// Protokolliert eine Debug-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void Debug(Type sourceType, string message)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        Debug(GetNamespace(sourceType), message);
    }

    /// <summary>
    /// Protokolliert eine erweiterte Debug-Nachricht
    /// (nur wenn Debug und Extended-Debug fuer den Namespace aktiv sind).
    /// </summary>
    public static void DebugExtended(string namespaceName, string message)
    {
        var normalizedNamespace = NormalizeNamespace(namespaceName);

        if (!IsDebugEnabled(normalizedNamespace) || !IsExtendedDebugEnabled(normalizedNamespace))
            return;

        LogInternal(normalizedNamespace, LogLevel.Debug, message, null, isExtendedDebug: true);
    }

    /// <summary>
    /// Protokolliert eine erweiterte Debug-Nachricht unter Verwendung des aufrufenden Namespaces.
    /// </summary>
    public static void DebugExtended(string message)
    {
        DebugExtended(ResolveCallerNamespace(), message);
    }

    /// <summary>
    /// Protokolliert eine erweiterte Debug-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void DebugExtended<T>(string message)
    {
        DebugExtended(GetNamespace(typeof(T)), message);
    }

    /// <summary>
    /// Protokolliert eine erweiterte Debug-Nachricht mit Namespace aus einem Typ.
    /// </summary>
    public static void DebugExtended(Type sourceType, string message)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        DebugExtended(GetNamespace(sourceType), message);
    }

    // ========== Interne Implementierung ==========

    private static void LogInternal(
        string namespaceName,
        LogLevel level,
        string message,
        Exception? ex,
        bool isExtendedDebug = false)
    {
        lock (LockObject)
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelStr = isExtendedDebug ? "DEBUG+" : level.ToString().ToUpperInvariant();
            var categoryStr = namespaceName;

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
                                ? ConsoleColor.Blue
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ResolveCallerNamespace()
    {
        var stackTrace = new StackTrace();

        for (var i = 1; i < stackTrace.FrameCount; i++)
        {
            var declaringType = stackTrace.GetFrame(i)?.GetMethod()?.DeclaringType;
            if (declaringType is null)
                continue;

            if (declaringType == typeof(Logging))
                continue;

            return NormalizeNamespace(declaringType.Namespace ?? declaringType.FullName ?? "GLOBAL");
        }

        return "GLOBAL";
    }

    private static bool TryGetBestNamespaceRule(string namespaceName, out (bool Enabled, bool Extended) rule)
    {
        var bestPrefixLength = -1;
        var bestRule = (Enabled: false, Extended: false);

        foreach (var entry in DebugEnabledByNamespace)
        {
            if (!IsNamespaceMatch(entry.Key, namespaceName))
                continue;

            if (entry.Key.Length <= bestPrefixLength)
                continue;

            bestPrefixLength = entry.Key.Length;
            bestRule = entry.Value;
        }

        rule = bestRule;
        return bestPrefixLength >= 0;
    }

    private static bool IsNamespaceMatch(string prefix, string namespaceName)
    {
        if (string.Equals(prefix, namespaceName, StringComparison.Ordinal))
            return true;

        return namespaceName.StartsWith(prefix + ".", StringComparison.Ordinal);
    }

    private static string NormalizeNamespace(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
            return "GLOBAL";

        return namespaceName.Trim().TrimEnd('.');
    }

    private static string GetNamespace(Type type)
    {
        return NormalizeNamespace(type.Namespace ?? type.FullName ?? "GLOBAL");
    }
}