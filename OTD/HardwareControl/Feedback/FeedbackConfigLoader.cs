// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - FeedbackControl
// Copyright (C) 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OTD.HardwareControl.FeedbackConfiguration;

/// <summary>
/// Loads and validates feedback provider configuration from AppData/feedback.xml.
/// </summary>
public static class FeedbackConfigLoader
{
    public const string DriverLoDiS88Commander = "lodi-s88-commander";

    /// <summary>
    /// Loads all configured feedback providers from feedback.xml.
    /// </summary>
    /// <param name="configFilePath">Optional absolute path to feedback.xml; default is AppData/feedback.xml.</param>
    /// <exception cref="InvalidOperationException">Thrown for missing file or invalid XML/config values.</exception>
    public static IReadOnlyList<FeedbackProviderConfig> LoadProviders(string? configFilePath = null)
    {
        var filePath = string.IsNullOrWhiteSpace(configFilePath)
            ? GetDefaultConfigFilePath()
            : configFilePath;

        if (!File.Exists(filePath))
            throw new InvalidOperationException($"Feedback configuration file not found: {filePath}");

        XDocument document;
        try
        {
            document = XDocument.Load(filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load feedback configuration '{filePath}': {ex.Message}", ex);
        }

        var providers = document.Root?
            .Elements("provider")
            .Select(ParseProvider)
            .ToList();

        if (providers is null)
            throw new InvalidOperationException("Feedback configuration has no root element.");

        if (providers.Count == 0)
            throw new InvalidOperationException("Feedback configuration must define at least one <provider> entry.");

        return providers;
    }

    /// <summary>
    /// Loads one provider by uid.
    /// </summary>
    public static FeedbackProviderConfig LoadProviderByUid(string uid, string? configFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(uid))
            throw new InvalidOperationException("Provider uid must not be empty.");

        var provider = LoadProviders(configFilePath)
            .FirstOrDefault(entry => string.Equals(entry.Uid, uid, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
            throw new InvalidOperationException($"Feedback provider with uid '{uid}' not found.");

        return provider;
    }

    private static FeedbackProviderConfig ParseProvider(XElement providerElement)
    {
        var uid = RequireAttribute(providerElement, "uid", "provider");
        var driver = RequireAttribute(providerElement, "driver", $"provider '{uid}'").Trim();

        var connectionElement = providerElement.Element("connection")
            ?? throw new InvalidOperationException($"Feedback provider '{uid}' is missing required <connection> element.");

        var ipAddress = RequireAttribute(connectionElement, "ip", $"provider '{uid}' / connection").Trim();
        var portText = connectionElement.Attribute("port")?.Value?.Trim();
        var port = 11092;
        if (!string.IsNullOrWhiteSpace(portText))
        {
            if (!int.TryParse(portText, out port) || port < 1 || port > 65535)
            {
                throw new InvalidOperationException(
                    $"Feedback provider '{uid}' has invalid port '{portText}'. Valid range: 1..65535.");
            }
        }

        var startupElement = providerElement.Element("startup");
        var queryOnStartup = ParseBoolAttribute(startupElement, "queryOnStartup", defaultValue: true);
        var subscribeOnStartup = ParseBoolAttribute(startupElement, "subscribeOnStartup", defaultValue: true);

        var moduleElements = providerElement.Element("modules")?.Elements("module") ?? [];
        var modules = moduleElements
            .Select(module => ParseModuleAddress(uid, module))
            .Distinct()
            .ToList();

        if (modules.Count == 0)
            throw new InvalidOperationException($"Feedback provider '{uid}' must define at least one <module address='...'>.");

        return new FeedbackProviderConfig(
            Uid: uid,
            Driver: driver,
            Connection: new FeedbackConnectionConfig(ipAddress, port),
            Startup: new FeedbackStartupConfig(queryOnStartup, subscribeOnStartup),
            Modules: modules);
    }

    private static int ParseModuleAddress(string uid, XElement moduleElement)
    {
        var addressText = RequireAttribute(moduleElement, "address", $"provider '{uid}' / module").Trim();
        if (!int.TryParse(addressText, out var moduleAddress) || moduleAddress < 1 || moduleAddress > 255)
        {
            throw new InvalidOperationException(
                $"Feedback provider '{uid}' has invalid module address '{addressText}'. Valid range: 1..255.");
        }

        return moduleAddress;
    }

    private static bool ParseBoolAttribute(XElement? element, string attributeName, bool defaultValue)
    {
        if (element is null)
            return defaultValue;

        var value = element.Attribute(attributeName)?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (bool.TryParse(value, out var parsed))
            return parsed;

        throw new InvalidOperationException($"Invalid boolean value '{value}' for attribute '{attributeName}'.");
    }

    private static string RequireAttribute(XElement element, string attributeName, string context)
    {
        var value = element.Attribute(attributeName)?.Value;
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"Missing required attribute '{attributeName}' in {context}.");
    }

    private static string GetDefaultConfigFilePath()
    {
        var appDataPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AppData");
        return Path.GetFullPath(Path.Combine(appDataPath, "feedback.xml"));
    }
}

