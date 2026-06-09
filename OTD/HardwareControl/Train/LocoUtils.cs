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
using System.Linq;
using System.Xml.Linq;

namespace OTD.HardwareControl.Train;

public static class LocoUtils
{
    /// <summary>
    /// Builds a speedV-to-decoder-step lookup table from the decoder configuration.
    /// Reads <c>&lt;speedtable&gt;</c> interpolates missing speed steps,
    /// and creates a 1 km/h speedV grid mapped to the nearest decoder step.
    /// Returns an empty list if no <c>&lt;speedtable&gt;</c> element is provided.
    /// </summary>
    /// <param name="speedTableElement">The <c>speedtable</c> XML element.</param>
    /// <param name="effectiveSpeedSteps">Effective count of decoder speed steps.</param>
    /// <param name="vMax">Output: configured maximum speed (Vmax) from the speed table.</param>
    /// <returns>
    /// A list of <see cref="SpeedEntry"/> values ordered by speedV,
    /// where each speedV is mapped to a decoder step.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="effectiveSpeedSteps"/> is smaller than 2.</exception>
    /// <exception cref="FormatException">Thrown when one or more speed entries contain invalid numeric values.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the speed table contains fewer than two valid <c>speed</c> entries
    /// or when the smallest configured step is not <c>1</c>.
    /// </exception>
    internal static List<SpeedEntry> CreateSpeedStepsTable(XElement? speedTableElement, int effectiveSpeedSteps,
        out int vMin, out int vMax)
    {
        vMin = 0;
        vMax = 0;

        if (speedTableElement is null)
            return [];

        if (effectiveSpeedSteps < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveSpeedSteps),
                effectiveSpeedSteps,
                $"Speed mapping requires at least 2 effective speed steps. Received: {effectiveSpeedSteps}.");
        }

        var baseEntries = new List<SpeedEntry>();

        foreach (var speedElement in speedTableElement.Elements("speed"))
        {
            var speedVRaw = speedElement.Attribute("v")?.Value.Trim();
            var stepRaw = speedElement.Attribute("step")?.Value.Trim();

            if (!int.TryParse(speedVRaw, out var speedV) || !int.TryParse(stepRaw, out var step))
            {
                throw new FormatException(
                    $"Invalid <speed> entry in <speedtable>: v='{speedVRaw}', step='{stepRaw}'. Both values must be integers.");
            }

            baseEntries.Add(new SpeedEntry(speedV, step));
        }

        if (baseEntries.Count < 2)
        {
            throw new InvalidOperationException(
                "Speed mapping requires at least two valid <speed> entries in <speedtable>.");
        }

        var minStep = baseEntries.Min(e => e.SpeedStep);
        if (minStep != 1)
        {
            throw new InvalidOperationException(
                $"Speed mapping requires the smallest step in <speedtable> to be 1, but the smallest configured step is {minStep}.");
        }

        baseEntries = baseEntries.OrderBy(e => e.SpeedStep).ToList();

        var result = InterpolateSpeedSteps(baseEntries, effectiveSpeedSteps);

        vMin = (int)Math.Round(baseEntries.First(e => e.SpeedStep == 1).SpeedV);
        vMax = (int)Math.Round(baseEntries.Max(e => e.SpeedV));
        // result = BuildSpeedStageTable(result, vMax);

        return result.OrderBy(e => e.SpeedV).ToList();
    }

    /// <summary>
    /// Interpolates intermediate speed entries between the given base speed points.
    /// </summary>
    /// <param name="baseEntries">Base speed points sorted by decoder step.</param>
    /// <param name="effectiveSpeedSteps">Total number of decoder speed steps to generate.</param>
    /// <returns>Interpolated speed entries from step 1 to <paramref name="effectiveSpeedSteps"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when fewer than two base entries are provided.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="effectiveSpeedSteps"/> is smaller than 2.</exception>
    private static List<SpeedEntry> InterpolateSpeedSteps(List<SpeedEntry> baseEntries, int effectiveSpeedSteps)
    {
        if (baseEntries.Count < 2)
            throw new ArgumentException(
                $"Speed interpolation requires at least 2 base speed entries. Invalid value: {baseEntries.Count}.",
                nameof(baseEntries));

        if (effectiveSpeedSteps < 2)
            throw new ArgumentOutOfRangeException(
                nameof(effectiveSpeedSteps),
                effectiveSpeedSteps,
                $"Speed interpolation requires at least 2 effective speed steps (<speedsteps> in decoder configuration). Invalid value: {effectiveSpeedSteps}.");

        // Kurvenform beibehalten:
        // Wir verwenden die Reihenfolge der vorhandenen Basispunkte als Stützstellen
        // und strecken diese stückweise linear auf die gewünschte Schrittanzahl.
        var orderedBaseEntries = baseEntries.OrderBy(e => e.SpeedStep).ToList();
        var result = new List<SpeedEntry>(effectiveSpeedSteps);

        var lastSourceIndex = orderedBaseEntries.Count - 1;

        for (var targetStep = 1; targetStep <= effectiveSpeedSteps; targetStep++)
        {
            // Zielschritt auf kontinuierliche Position im Quell-Stützstellenraum abbilden.
            // targetStep=1 => sourcePosition=0
            // targetStep=effectiveSpeedSteps => sourcePosition=lastSourceIndex
            var ratio = (double)(targetStep - 1) / (effectiveSpeedSteps - 1);
            var sourcePosition = ratio * lastSourceIndex;

            var lowerIndex = (int)Math.Floor(sourcePosition);
            var upperIndex = (int)Math.Ceiling(sourcePosition);

            double interpolatedSpeedV;

            if (lowerIndex == upperIndex)
            {
                interpolatedSpeedV = orderedBaseEntries[lowerIndex].SpeedV;
            }
            else
            {
                var lower = orderedBaseEntries[lowerIndex];
                var upper = orderedBaseEntries[upperIndex];

                // Lokale Interpolation zwischen zwei benachbarten Stützpunkten.
                var localRatio = sourcePosition - lowerIndex;
                interpolatedSpeedV = lower.SpeedV + (upper.SpeedV - lower.SpeedV) * localRatio;
            }

            result.Add(new SpeedEntry(interpolatedSpeedV, targetStep));
        }

        return result;
    }

    /// <summary>
    /// Rechnet einen Decoder-SpeedStep kongruent zur Floor-Mapping-Logik in
    /// <c>SetSpeedVAsync</c> in eine gemeldete Geschwindigkeit (SpeedV, km/h) zurück.
    /// Für SpeedStep 0 oder eine leere Tabelle wird 0 zurückgegeben.
    /// </summary>
    /// <param name="speedTable">Interpolierte SpeedV-Tabelle.</param>
    /// <param name="speedStep">Vom Decoder gemeldeter SpeedStep.</param>
    /// <returns>SpeedV in km/h, kongruent zum Vorwaertsmapping.</returns>
    internal static int ResolveSpeedVForSpeedStep(IReadOnlyList<SpeedEntry> speedTable, int speedStep)
    {
        if (speedStep == 0 || speedTable.Count == 0)
            return 0;

        // Primär exakten Schritt nehmen, sonst nächstkleineren Schritt als Fallback.
        var stepEntry = speedTable.FirstOrDefault(e => e.SpeedStep == speedStep);
        if (stepEntry.SpeedStep == 0)
            stepEntry = speedTable
                .Where(e => e.SpeedStep <= speedStep)
                .OrderByDescending(e => e.SpeedStep)
                .FirstOrDefault();

        if (stepEntry.SpeedStep == 0)
            return 0;

        // Nie schneller melden als die reale (interpolierte) Schrittgeschwindigkeit.
        var speed = (int)Math.Floor(stepEntry.SpeedV);

        // Rueckwaerts-kongruent absichern: gemeldete V darf beim Vorwärtsmapping
        // niemals auf einen höheren Schritt springen als der Readback-Schritt.
        while (speed > 0 && ResolveSpeedStepForSpeedV(speedTable, speed) > speedStep)
            speed--;

        return speed;
    }

    /// <summary>
    /// Ermittelt zu einer Sollgeschwindigkeit (SpeedV) den groessten SpeedStep,
    /// dessen konfigurierte SpeedV die Sollgeschwindigkeit nicht ueberschreitet.
    /// </summary>
    /// <param name="speedTable">Interpolierte SpeedV-Tabelle.</param>
    /// <param name="speed">Sollgeschwindigkeit in km/h (SpeedV).</param>
    /// <returns>Passender Decoder-SpeedStep (Floor-Mapping).</returns>
    internal static int ResolveSpeedStepForSpeedV(IReadOnlyList<SpeedEntry> speedTable, int speed)
        => speedTable
            .Where(entry => entry.SpeedV <= speed)
            .OrderByDescending(entry => entry.SpeedV)
            .Select(entry => (int?)entry.SpeedStep)
            .FirstOrDefault() ?? 0;
}

/// <summary>
/// Represents a speedV-to-decoder-step mapping entry.
/// </summary>
public readonly record struct SpeedEntry(double SpeedV, int SpeedStep);