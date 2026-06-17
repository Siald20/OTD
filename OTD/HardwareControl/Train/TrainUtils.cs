// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - DecoderControl
// Copyright (C) 2026
//
// Authors:
// - Hansueli Alder <info@batec.net>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OTD.HardwareControl;

public static class TrainUtils
{
    /// <summary>
    /// Parses the train composition from a train configuration element.
    /// Recognized entries are <c>&lt;loco&gt;</c> and <c>&lt;car&gt;</c>.
    /// </summary>
    /// <param name="trainConfiguration">The train configuration element loaded from <c>trains.xml</c>.</param>
    /// <returns>The parsed composition entries in train order.</returns>
    internal static List<TrainVehicle> GetComposition(XElement? trainConfiguration)
    {
        var compositionElement = trainConfiguration?.Element("composition");
        if (compositionElement is null)
            return [];

        var result = new List<TrainVehicle>();

        foreach (var vehicleElement in compositionElement.Elements()
                     .Where(x => x.Name.LocalName is "loco" or "car"))
        {
            var vehicleType = vehicleElement.Name.LocalName;
            var vehicleUidText = vehicleElement.Attribute("uid")?.Value;
            if (!Guid.TryParse(vehicleUidText, out var vehicleId))
            {
                Console.WriteLine($"Warnung: Ungültige oder fehlende Fahrzeug-UID '{vehicleUidText}' in trains.xml.");
                continue;
            }

            result.Add(new TrainVehicle(
                vehicleId,
                vehicleType,
                GetVehicleOrientation(vehicleElement.Attribute("orientation")?.Value, vehicleId),
                vehicleElement.Attribute("headlight_forward")?.Value ?? string.Empty,
                vehicleElement.Attribute("headlight_backward")?.Value ?? string.Empty));
        }

        return result;
    }

    /// <summary>
    /// Calculates composition key values from all train vehicles.
    /// </summary>
    /// <param name="composition">Train composition entries including <see cref="TrainVehicle.VehicleInstance"/>.</param>
    /// <returns>Values total composition length, composition VMin, composition VMax and total weight.</returns>
    internal static (int Length, int VMin, int VMax, int Weight) CalculateCompositionKeyValues(
        IReadOnlyCollection<TrainVehicle> composition)
    {
        if (composition.Count == 0)
            return (0, 0, 0, 0);

        var vehicles = composition
            .Select(entry => entry.VehicleInstance)
            .Where(instance => instance is not null)
            .Cast<IVehicle>()
            .ToList();

        if (vehicles.Count != composition.Count)
            return (0, 0, 0, 0);

        // Länge: Summe aller Fahrzeuglängen, falls alle Fahrzeuge eine gültige Länge haben, sonst 0.
        var length = vehicles.All(vehicle => vehicle.Length > 0)
            ? vehicles.Sum(vehicle => vehicle.Length)
            : 0;

        // VMin: Maximaler VMin-Wert aller Fahrzeuge
        var vMin = vehicles.Max(vehicle => vehicle.VMin);

        // VMax: Minimaler VMax-Wert aller Fahrzeuge, Fahrzeuge mit VMax = 0 (unbekannte VMax) werden ignoriert.
        // Fallback: Falls keines der Fahrzeuge eine gültige VMax-Angabe hat, wird 0 zurückgegeben.
        var vMax = vehicles
            .Where(vehicle => vehicle.VMax > 0)
            .Select(vehicle => vehicle.VMax)
            .DefaultIfEmpty(0)
            .Min();

        // Gewicht: Summe aller Fahrzeuggewichte, falls alle Fahrzeuge eine gültige Gewichtsangabe haben, sonst 0.
        var weight = vehicles.All(vehicle => vehicle.Weight > 0)
            ? vehicles.Sum(vehicle => vehicle.Weight)
            : 0;

        return (length, vMin, vMax, weight);
    }

    /// <summary>
    /// Backward-compatible wrapper for the composition key value calculation.
    /// </summary>
    // internal static (int Length, int VMin, int VMax) GetCompositionKeyValues(
    //     IReadOnlyCollection<TrainVehicle> composition)
    //     => CalculateCompositionKeyValues(composition);

    internal static VehicleOrientation GetVehicleOrientation(string? value, Guid locoId)
    {
        if (string.Equals(value, "reverse", StringComparison.OrdinalIgnoreCase))
            return VehicleOrientation.Reverse;

        if (string.Equals(value, "normal", StringComparison.OrdinalIgnoreCase))
            return VehicleOrientation.Normal;

        Console.WriteLine(
            $"Warnung: Ungültige Ausrichtung '{value}' für Fahrzeug {locoId}; Fallback auf 'normal'.");
        return VehicleOrientation.Normal;
    }

    /// <summary>
    /// Parses and validates a vehicle length value from a string attribute.
    /// Returns 0 if the attribute is missing or empty.
    /// </summary>
    /// <param name="lengthAttributeValue">The <c>length</c> attribute value as string.</param>
    /// <returns>The parsed length as integer, or 0 if not specified.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the value is not numeric or smaller than 1.
    /// </exception>
    internal static int GetVehicleLength(string? lengthAttributeValue)
    {
        // Default-Wert zurückgeben, falls kein Wert angegeben ist.
        if (string.IsNullOrWhiteSpace(lengthAttributeValue))
            return 0;

        if (int.TryParse(lengthAttributeValue, out var length) && length >= 1)
            return length;

        throw new ArgumentOutOfRangeException(
            nameof(lengthAttributeValue),
            lengthAttributeValue,
            $"Invalid length value: '{lengthAttributeValue}'. Must be a positive integer greater then 0.");
    }

    /// <summary>
    /// Parses the optional train-level length fallback from <c>trains.xml</c>.
    /// Returns 0 if the attribute is missing, empty, or explicitly set to 0.
    /// </summary>
    /// <param name="lengthAttributeValue">The train <c>length</c> attribute value as string.</param>
    /// <returns>The parsed train length or 0 if unknown.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the value is non-numeric or negative.
    /// </exception>
    internal static int GetLengthFromTrainConfig(string? lengthAttributeValue)
    {
        if (string.IsNullOrWhiteSpace(lengthAttributeValue))
            return 0;

        if (!int.TryParse(lengthAttributeValue, out var length) || length < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lengthAttributeValue),
                lengthAttributeValue,
                $"Invalid train length value: '{lengthAttributeValue}'. Must be a non-negative integer.");
        }

        return length;
    }

    /// <summary>
    /// Parses and validates the maximum speed from <c>&lt;model&gt;&lt;vmax&gt;</c>.
    /// Returns the provided fallback if the element is missing or empty.
    /// </summary>
    internal static int GetVehicleVMax(XElement? modelElement, int defaultVMax = 0)
    {
        var vMaxValue = modelElement?.Element("vmax")?.Value;

        if (string.IsNullOrWhiteSpace(vMaxValue))
            return defaultVMax;

        if (!int.TryParse(vMaxValue, out var vMax) || vMax < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelElement),
                vMaxValue,
                $"Invalid maximum speed value in <model><vmax>: '{vMaxValue}'. Must be a positive integer greater than 0.");
        }

        return vMax;
    }

    /// <summary>
    /// Parses and validates the vehicle weight from <c>&lt;model&gt;&lt;weight&gt;</c>.
    /// Returns 0 if the element is missing or empty.
    /// </summary>
    internal static int GetVehicleWeight(XElement? modelElement)
    {
        var weightValue = modelElement?.Element("weight")?.Value;

        if (string.IsNullOrWhiteSpace(weightValue))
            return 0;

        if (!int.TryParse(weightValue, out var weight) || weight < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelElement),
                weightValue,
                $"Invalid weight value in <model><weight>: '{weightValue}'. Must be a positive integer greater than 0.");
        }

        return weight;
    }

    /// <summary>
    /// Reads the configuration element of the specified type whose <c>uid</c>
    /// attribute matches the provided identifier.
    /// </summary>
    /// <param name="configType">
    /// The configuration type to load, for example <c>loco</c>, <c>car</c>, or <c>train</c>.
    /// </param>
    /// <param name="id">The unique identifier of the requested configuration element.</param>
    /// <returns>
    /// The matching configuration <see cref="XElement"/>, or <see langword="null"/>
    /// if the configuration file does not exist, cannot be loaded, or no matching element is found.
    /// </returns>
    public static XElement? ReadXConfiguration(string configType, Guid id)
    {
        var configFilePath = GetConfigFilePath();
        configFilePath = Path.Combine(configFilePath, GetConfigFileName(configType));

        if (!File.Exists(configFilePath))
        {
            Console.WriteLine($"Fehler: Konfigurationsdatei nicht gefunden: {configFilePath}");
            return null;
        }

        XDocument configElement;
        try
        {
            configElement = XDocument.Load(configFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Laden der Datei {GetConfigFileName(configType)}: {ex.Message}");
            return null;
        }

        var config = configElement.Root?
            .Elements(configType)
            .FirstOrDefault(l => l.Attribute("uid")?.Value == id.ToString());

        if (config == null)
            Console.WriteLine($"Fehler: Konfiguration vom Typ <{configType}> mit UID {id} nicht gefunden.");

        return config;
    }

    /// <summary>
    /// Replaces the <c>&lt;composition&gt;</c> element of the specified train in <c>trains.xml</c>
    /// with the provided vehicle entries.
    /// </summary>
    /// <param name="trainId">UID of the train element to update.</param>
    /// <param name="vehicles">Ordered composition entries to persist.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="vehicles"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when trains.xml is missing, the train UID does not exist, the composition is empty,
    /// no locomotive exists, or a vehicle type is unsupported.
    /// </exception>
    internal static void UpdateTrainComposition(Guid trainId, IReadOnlyList<TrainVehicle> vehicles)
    {
        if (vehicles is null)
            throw new ArgumentNullException(nameof(vehicles));

        if (vehicles.Count == 0)
            throw new InvalidOperationException($"Train {trainId} does not include vehicles.");

        if (!vehicles.Any(vehicle => string.Equals(vehicle.VehicleType, "loco", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Train {trainId} doesn't include at least one locomotive.");

        var trainConfigPath = Path.Combine(GetConfigFilePath(), "trains.xml");
        if (!File.Exists(trainConfigPath))
            throw new InvalidOperationException($"Configuration file not found: {trainConfigPath}");

        XDocument document;
        try
        {
            document = XDocument.Load(trainConfigPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load trains.xml: {ex.Message}", ex);
        }

        var trainElement = document.Root?
            .Elements("train")
            .FirstOrDefault(element => element.Attribute("uid")?.Value == trainId.ToString());

        if (trainElement is null)
            throw new InvalidOperationException($"Train {trainId} could not be found in trains.xml.");

        var compositionElement = trainElement.Element("composition");
        if (compositionElement is null)
        {
            compositionElement = new XElement("composition");
            trainElement.Add(compositionElement);
        }

        compositionElement.RemoveNodes();

        foreach (var vehicle in vehicles)
        {
            var vehicleType = vehicle.VehicleType.ToLowerInvariant();
            if (vehicleType is not ("loco" or "car"))
            {
                throw new InvalidOperationException(
                    $"Train {trainId} has an invalid vehicle type '{vehicle.VehicleType}' for vehicle UID {vehicle.VehicleId}");
            }

            var vehicleElement = new XElement(vehicleType,
                new XAttribute("uid", vehicle.VehicleId),
                new XAttribute("orientation", vehicle.Orientation == VehicleOrientation.Reverse ? "reverse" : "normal"));

            if (!string.IsNullOrWhiteSpace(vehicle.HeadlightPatternForward))
                vehicleElement.SetAttributeValue("headlight_forward", vehicle.HeadlightPatternForward);

            if (!string.IsNullOrWhiteSpace(vehicle.HeadlightPatternBackward))
                vehicleElement.SetAttributeValue("headlight_backward", vehicle.HeadlightPatternBackward);

            compositionElement.Add(vehicleElement);
        }

        var backupPath = trainConfigPath + ".bak";

        try
        {
            File.Copy(trainConfigPath, backupPath, overwrite: true);
            document.Save(trainConfigPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to save trains.xml (backup: {backupPath}): {ex.Message}", ex);
        }
    }

    private static string GetConfigFileName(string configType)
        => configType switch
        {
            "train" => "trains.xml",
            "loco" => "locos.xml",
            "car" => "cars.xml",
            "accessory" => "accessories.xml",
            _ => $"{configType}.xml"
        };

    //  Ermittelt den absoluten Pfad zur Konfigurationsdatei im AppData-Verzeichnis
    //  ToDo: Später in globale Anwendungs-Konfiguration implementieren
    private static string GetConfigFilePath()
    {
        var locoFilePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "AppData"
        );

        return Path.GetFullPath(locoFilePath);
    }
}

/// <summary>
/// Represents one vehicle entry from a train composition.
/// </summary>
/// <param name="VehicleId">Unique identifier of the vehicle.</param>
/// <param name="VehicleType">XML element name from the train composition, for example <c>loco</c> or <c>car</c>.</param>
/// <param name="Orientation">Vehicle orientation within the train composition.</param>
/// <param name="HeadlightPatternForward">Headlight pattern for decoder direction A.</param>
/// <param name="HeadlightPatternBackward">Headlight pattern for decoder direction B.</param>
public readonly record struct TrainVehicle(
    Guid VehicleId,
    string VehicleType,
    VehicleOrientation Orientation,
    string HeadlightPatternForward,
    string HeadlightPatternBackward,
    IVehicle? VehicleInstance = null)
{
    /// <summary>
    /// position of the vehicle within the immutable train composition.
    /// 0 means "not assigned" (e.g. builder stage before composition build).
    /// </summary>
    public int Position { get; init; }
}



