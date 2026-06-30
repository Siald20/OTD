// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using OTD.Common;
using OTD.TrainDriving.RouteControl.Domain;
using OTD.TrainDriving.RouteControl.Exceptions;
using OTD.TrainDriving.Trajectory;

namespace OTD.TrainDriving.RouteControl.Services;

public sealed class XmlRouteDefinitionService : IRouteDefinitionService
{
    private const string ConfigFileName = "routelegs.xml";

    private readonly object _sync = new();
    private readonly string _configPath;
    private Dictionary<(string From, string To), StaticRouteLegData> _legs = new();

    public event Action? DefinitionsChanged;

    public XmlRouteDefinitionService(string? configFilePath = null)
    {
        _configPath = string.IsNullOrWhiteSpace(configFilePath)
            ? GetDefaultConfigFilePath()
            : Path.GetFullPath(configFilePath);

        Reload();
    }

    public bool TryGetLeg(string fromWaypointId, string toWaypointId, out StaticRouteLegData legData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromWaypointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toWaypointId);

        var key = (NormalizeKey(fromWaypointId), NormalizeKey(toWaypointId));
        lock (_sync)
        {
            return _legs.TryGetValue(key, out legData!);
        }
    }

    public IReadOnlyCollection<StaticRouteLegData> GetAllLegs()
    {
        lock (_sync)
        {
            return _legs.Values.ToList().AsReadOnly();
        }
    }

    public void Reload()
    {
        var loaded = LoadFromFile(_configPath);
        lock (_sync)
        {
            _legs = loaded;
        }

        Logging.Info<XmlRouteDefinitionService>($"Route definition config loaded: legs={loaded.Count}, file='{_configPath}'.");
        DefinitionsChanged?.Invoke();
    }

    public static string GetDefaultConfigFilePath()
    {
        var appDataPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AppData");
        return Path.GetFullPath(Path.Combine(appDataPath, ConfigFileName));
    }

    private static Dictionary<(string From, string To), StaticRouteLegData> LoadFromFile(string configPath)
    {
        if (!File.Exists(configPath))
            throw new RouteValidationException($"Route definition file not found: {configPath}");

        XDocument document;
        try
        {
            document = XDocument.Load(configPath);
        }
        catch (Exception ex)
        {
            throw new RouteValidationException($"Failed to load route definition file '{configPath}': {ex.Message}");
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "routelegs", StringComparison.OrdinalIgnoreCase))
            throw new RouteValidationException("routelegs.xml root element must be <routelegs>.");

        var globalDefaults = ParseDefaults(root.Element("defaults"), null);
        var result = new Dictionary<(string From, string To), StaticRouteLegData>();

        foreach (var legElement in root.Elements("routeleg"))
        {
            var from = RequireAttribute(legElement, "fromwaypoint", "routeleg");
            var to = RequireAttribute(legElement, "towaypoint", "routeleg");

            var distanceCm = ParseIntAttribute(legElement, "distance_cm")
                ?? ParseIntAttribute(legElement, "length")
                ?? throw new RouteValidationException($"RouteLeg '{from}->{to}' requires attribute distance_cm (legacy: length).");

            if (distanceCm <= 0)
                throw new RouteValidationException($"RouteLeg '{from}->{to}' requires distance_cm > 0.");

            var category = OptionalAttribute(legElement, "category") ?? "other";
            var localDefaults = ParseDefaults(legElement.Element("defaults"), globalDefaults);

            var maxSpeedKmh = ParseDoubleAttribute(legElement, "max_speed_kmh")
                              ?? ResolveSpeedByCategory(localDefaults.SpeedByCategoryKmh, category);

            var sensors = ParseSensors(legElement.Element("sensors"), distanceCm, localDefaults.SensorActivationTimeoutMs);

            var key = (NormalizeKey(from), NormalizeKey(to));
            if (result.ContainsKey(key))
                throw new RouteValidationException($"Duplicate static route definition for '{from}->{to}'.");

            result[key] = new StaticRouteLegData(
                FromWaypointId: from.Trim(),
                ToWaypointId: to.Trim(),
                DistanceCm: distanceCm,
                SensorMarkers: sensors,
                DefaultMaxSpeedKmh: maxSpeedKmh,
                DefaultDriveProfile: localDefaults.DriveProfile,
                DefaultAccelerationStartPolicy: localDefaults.AccelerationStartPolicy,
                DefaultStopPoint: localDefaults.StopPoint,
                Metadata: null);
        }

        return result;
    }

    private static IReadOnlyList<SensorMarker>? ParseSensors(XElement? sensorsElement, int distanceCm, int? defaultTimeoutMs)
    {
        if (sensorsElement is null)
            return null;

        var markers = new List<SensorMarker>();
        var localIds = new HashSet<int>();

        foreach (var sensorElement in sensorsElement.Elements("sensor"))
        {
            var sensorId = ParseIntAttribute(sensorElement, "id")
                ?? throw new RouteValidationException("sensor requires id.");
            var offsetCm = ParseIntAttribute(sensorElement, "offset_cm")
                ?? ParseIntAttribute(sensorElement, "offset")
                ?? throw new RouteValidationException($"sensor '{sensorId}' requires offset_cm (legacy: offset).");

            if (offsetCm < 0 || offsetCm > distanceCm)
            {
                throw new RouteValidationException(
                    $"SensorMarker '{sensorId}' must be within [0, {distanceCm}] cm.");
            }

            if (!localIds.Add(sensorId))
                throw new RouteValidationException($"Duplicate SensorMarker '{sensorId}' in one routeleg.");

            var timeoutMs = ParseIntAttribute(sensorElement, "activation_timeout_ms") ?? defaultTimeoutMs;
            markers.Add(new SensorMarker(SensorId: sensorId, OffsetCm: offsetCm, ActivationTimeoutMs: timeoutMs));
        }

        return markers.Count == 0 ? null : markers.AsReadOnly();
    }

    private static RouteDefaults ParseDefaults(XElement? defaultsElement, RouteDefaults? fallback)
    {
        var speedByCategory = fallback?.SpeedByCategoryKmh is null
            ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double>(fallback.SpeedByCategoryKmh, StringComparer.OrdinalIgnoreCase);

        var driveProfile = fallback?.DriveProfile;
        var accelerationStartPolicy = fallback?.AccelerationStartPolicy;
        var stopPoint = fallback?.StopPoint;
        var sensorActivationTimeoutMs = fallback?.SensorActivationTimeoutMs;

        if (defaultsElement is not null)
        {
            var policyElement = defaultsElement.Element("acceleration_start_policy");
            var policyValue = OptionalAttribute(policyElement, "value");
            if (!string.IsNullOrWhiteSpace(policyValue) &&
                Enum.TryParse<AccelerationStartPolicy>(policyValue, true, out var parsedPolicy))
            {
                accelerationStartPolicy = parsedPolicy;
            }

            var driveProfileElement = defaultsElement.Element("driveprofile");
            if (driveProfileElement is not null)
            {
                driveProfile = new RouteDriveProfile(
                    AccelerationPreset: ParseAccelerationPreset(OptionalAttribute(driveProfileElement, "acceleration_preset")),
                    BrakingPreset: ParseBrakingPreset(OptionalAttribute(driveProfileElement, "braking_preset")));
            }
            else
            {
                var legacyDriveProfilesElement = defaultsElement.Element("driveprofiles");
                if (legacyDriveProfilesElement is not null)
                    driveProfile = ParseLegacyDriveProfile(legacyDriveProfilesElement, fallback?.DriveProfile);
            }

            var speedLimitsElement = defaultsElement.Element("speedlimits");
            if (speedLimitsElement is not null)
            {
                foreach (var speedLimitElement in speedLimitsElement.Elements("speedlimit"))
                {
                    var category = OptionalAttribute(speedLimitElement, "category")
                                   ?? OptionalAttribute(speedLimitElement, "catetory")
                                   ?? "other";
                    var speedKmh = ParseDoubleAttribute(speedLimitElement, "speed_kmh")
                                   ?? ParseDoubleAttribute(speedLimitElement, "speed")
                                   ?? throw new RouteValidationException($"speedlimit for category '{category}' requires speed_kmh (legacy: speed).");
                    if (speedKmh <= 0)
                        throw new RouteValidationException($"speedlimit for category '{category}' must be > 0.");

                    speedByCategory[category.Trim()] = speedKmh;
                }
            }

            var stopPointElement = defaultsElement.Element("stoppoint");
            if (stopPointElement is not null)
            {
                var enabled = ParseBoolAttribute(stopPointElement, "enabled") ?? true;
                if (enabled)
                {
                    var offset = ParseIntAttribute(stopPointElement, "offset_cm")
                                 ?? ParseIntAttribute(stopPointElement, "offset")
                                 ?? throw new RouteValidationException("defaults/stoppoint requires offset_cm when enabled=true.");
                    var reason = OptionalAttribute(stopPointElement, "reason");
                    stopPoint = new StopPoint(OffsetCm: offset, StopReason: reason);
                }
                else
                {
                    stopPoint = null;
                }
            }

            var sensorElement = defaultsElement.Element("sensor");
            if (sensorElement is not null)
            {
                sensorActivationTimeoutMs = ParseIntAttribute(sensorElement, "activation_timeout_ms");
            }
        }

        return new RouteDefaults(speedByCategory, driveProfile, accelerationStartPolicy, stopPoint, sensorActivationTimeoutMs);
    }

    private static RouteDriveProfile? ParseLegacyDriveProfile(XElement legacyContainer, RouteDriveProfile? fallback)
    {
        var acceleration = fallback?.AccelerationPreset;
        var braking = fallback?.BrakingPreset;

        foreach (var profileElement in legacyContainer.Elements("driveprofile"))
        {
            var profileType = OptionalAttribute(profileElement, "profile");
            var presetValue = OptionalAttribute(profileElement, "preset");
            if (string.IsNullOrWhiteSpace(profileType) || string.IsNullOrWhiteSpace(presetValue))
                continue;

            if (string.Equals(profileType, "acceleration", StringComparison.OrdinalIgnoreCase))
                acceleration = ParseAccelerationPreset(presetValue);
            else if (string.Equals(profileType, "braking", StringComparison.OrdinalIgnoreCase))
                braking = ParseBrakingPreset(presetValue);
        }

        if (acceleration is null && braking is null)
            return fallback;

        return new RouteDriveProfile(acceleration, braking);
    }

    private static AccelerationTrajectoryPreset? ParseAccelerationPreset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Enum.TryParse<AccelerationTrajectoryPreset>(value, true, out var parsed)
            ? parsed
            : throw new RouteValidationException($"Unknown acceleration preset '{value}'.");
    }

    private static BrakingTrajectoryPreset? ParseBrakingPreset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Enum.TryParse<BrakingTrajectoryPreset>(value, true, out var parsed)
            ? parsed
            : throw new RouteValidationException($"Unknown braking preset '{value}'.");
    }

    private static double? ResolveSpeedByCategory(Dictionary<string, double> speedByCategory, string category)
    {
        if (speedByCategory.TryGetValue(category.Trim(), out var speedByCategoryValue))
            return speedByCategoryValue;

        return speedByCategory.TryGetValue("other", out var speedByOtherValue)
            ? speedByOtherValue
            : null;
    }

    private static string NormalizeKey(string value) => value.Trim().ToUpperInvariant();

    private static string RequireAttribute(XElement element, string name, string context)
    {
        var value = OptionalAttribute(element, name);
        return !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new RouteValidationException($"Missing required attribute '{name}' in {context}.");
    }

    private static string? OptionalAttribute(XElement? element, string name)
    {
        return element?.Attribute(name)?.Value?.Trim();
    }

    private static int? ParseIntAttribute(XElement? element, string name)
    {
        var raw = OptionalAttribute(element, name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new RouteValidationException($"Invalid integer value '{raw}' for attribute '{name}'.");
    }

    private static double? ParseDoubleAttribute(XElement? element, string name)
    {
        var raw = OptionalAttribute(element, name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new RouteValidationException($"Invalid numeric value '{raw}' for attribute '{name}'.");
    }

    private static bool? ParseBoolAttribute(XElement? element, string name)
    {
        var raw = OptionalAttribute(element, name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (bool.TryParse(raw, out var parsed))
            return parsed;

        throw new RouteValidationException($"Invalid boolean value '{raw}' for attribute '{name}'.");
    }

    private sealed record RouteDefaults(
        Dictionary<string, double> SpeedByCategoryKmh,
        RouteDriveProfile? DriveProfile,
        AccelerationStartPolicy? AccelerationStartPolicy,
        StopPoint? StopPoint,
        int? SensorActivationTimeoutMs);
}


