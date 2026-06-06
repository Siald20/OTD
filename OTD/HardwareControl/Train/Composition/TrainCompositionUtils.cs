// // SPDX-License-Identifier: GPL-3.0-or-later
// //
// // OpenTrainDrive - DecoderControl
// // Copyright (C) 2026
// //
// // Authors:
// // - Hansueli Alder <inf@batec.net>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OTD.HardwareControl.Train.Composition;

/// <summary>
/// Provides persistence operations for splitting train compositions in <c>train.xml</c>.
/// </summary>
internal static class TrainCompositionUtils
{
    /// <summary>
    /// Splits an existing train configuration into two train entries.
    /// The original train keeps <paramref name="remainingVehicles"/>, while a new train (full metadata clone)
    /// is created for <paramref name="detachedVehicles"/>.
    /// </summary>
    /// <returns>UID of the newly created train entry.</returns>
    internal static Guid SplitTrainConfiguration(
        Guid sourceTrainId,
        IReadOnlyList<TrainVehicle> remainingVehicles,
        IReadOnlyList<TrainVehicle> detachedVehicles)
    {
        if (remainingVehicles is null)
            throw new ArgumentNullException(nameof(remainingVehicles));

        if (detachedVehicles is null)
            throw new ArgumentNullException(nameof(detachedVehicles));

        if (remainingVehicles.Count == 0)
            throw new InvalidOperationException("Remaining composition must not be empty.");

        if (detachedVehicles.Count == 0)
            throw new InvalidOperationException("Detached composition must not be empty.");

        var trainConfigPath = Path.Combine(GetConfigFilePath(), "train.xml");
        if (!File.Exists(trainConfigPath))
            throw new InvalidOperationException($"Configuration file not found: {trainConfigPath}");

        XDocument document;
        try
        {
            document = XDocument.Load(trainConfigPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load train.xml: {ex.Message}", ex);
        }

        var sourceTrainElement = document.Root?
            .Elements("train")
            .FirstOrDefault(element => element.Attribute("uid")?.Value == sourceTrainId.ToString());

        if (sourceTrainElement is null)
            throw new InvalidOperationException($"Train {sourceTrainId} could not be found in train.xml.");

        var newTrainId = Guid.NewGuid();
        var newTrainElement = new XElement(sourceTrainElement);
        newTrainElement.SetAttributeValue("uid", newTrainId.ToString());

        sourceTrainElement.Element("composition")?.Remove();
        sourceTrainElement.Add(CreateCompositionElement(remainingVehicles));

        newTrainElement.Element("composition")?.Remove();
        newTrainElement.Add(CreateCompositionElement(detachedVehicles));

        document.Root?.Add(newTrainElement);

        var backupPath = trainConfigPath + ".bak";
        try
        {
            File.Copy(trainConfigPath, backupPath, overwrite: true);
            document.Save(trainConfigPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to save train.xml (backup: {backupPath}): {ex.Message}", ex);
        }

        return newTrainId;
    }

    /// <summary>
    /// Joins two existing train configurations into one composition.
    /// Vehicles from <paramref name="train1Id"/> stay first, followed by vehicles from
    /// <paramref name="train2Id"/>. Metadata of train1 is preserved and train2 is removed.
    /// </summary>
    internal static void JoinTrainConfigurations(Guid train1Id, Guid train2Id)
    {
        if (train1Id == train2Id)
            throw new ArgumentException("JoinComposition requires two different train IDs.");

        var trainConfigPath = Path.Combine(GetConfigFilePath(), "train.xml");
        if (!File.Exists(trainConfigPath))
            throw new InvalidOperationException($"Configuration file not found: {trainConfigPath}");

        XDocument document;
        try
        {
            document = XDocument.Load(trainConfigPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load train.xml: {ex.Message}", ex);
        }

        var trainElements = document.Root?.Elements("train").ToList() ?? [];
        var train1Element = trainElements.FirstOrDefault(element => element.Attribute("uid")?.Value == train1Id.ToString());
        var train2Element = trainElements.FirstOrDefault(element => element.Attribute("uid")?.Value == train2Id.ToString());

        if (train1Element is null)
            throw new InvalidOperationException($"Train {train1Id} could not be found in train.xml.");

        if (train2Element is null)
            throw new InvalidOperationException($"Train {train2Id} could not be found in train.xml.");

        var train1Composition = TrainUtils.GetComposition(train1Element);
        var train2Composition = TrainUtils.GetComposition(train2Element);

        if (train1Composition.Count == 0)
            throw new InvalidOperationException($"Train {train1Id} composition must not be empty.");

        if (train2Composition.Count == 0)
            throw new InvalidOperationException($"Train {train2Id} composition must not be empty.");

        var joinedComposition = new List<TrainVehicle>(train1Composition.Count + train2Composition.Count);
        joinedComposition.AddRange(train1Composition);
        joinedComposition.AddRange(train2Composition);

        train1Element.Element("composition")?.Remove();
        train1Element.Add(CreateCompositionElement(joinedComposition));
        train2Element.Remove();

        var backupPath = trainConfigPath + ".bak";
        try
        {
            File.Copy(trainConfigPath, backupPath, overwrite: true);
            document.Save(trainConfigPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to save train.xml (backup: {backupPath}): {ex.Message}", ex);
        }
    }

    private static XElement CreateCompositionElement(IReadOnlyList<TrainVehicle> vehicles)
    {
        var compositionElement = new XElement("composition");

        foreach (var vehicle in vehicles)
        {
            var vehicleType = vehicle.VehicleType.ToLowerInvariant();
            if (vehicleType is not ("loco" or "car"))
            {
                throw new InvalidOperationException(
                    $"Invalid vehicle type '{vehicle.VehicleType}' for vehicle UID {vehicle.VehicleId}");
            }

            var vehicleElement = new XElement(vehicleType,
                new XAttribute("uid", vehicle.VehicleId),
                new XAttribute("orientation",
                    vehicle.Orientation == VehicleOrientation.Reverse ? "reverse" : "normal"));

            if (!string.IsNullOrWhiteSpace(vehicle.HeadlightPatternForward))
                vehicleElement.SetAttributeValue("headlight_forward", vehicle.HeadlightPatternForward);

            if (!string.IsNullOrWhiteSpace(vehicle.HeadlightPatternBackward))
                vehicleElement.SetAttributeValue("headlight_backward", vehicle.HeadlightPatternBackward);

            compositionElement.Add(vehicleElement);
        }

        return compositionElement;
    }

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

