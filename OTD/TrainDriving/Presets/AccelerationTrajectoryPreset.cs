// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.Presets;

/// <summary>
/// Vordefinierte Kurvenprofile fuer Beschleunigungsphasen (currentSpeed &lt; targetSpeed).
/// </summary>
public enum AccelerationTrajectoryPreset
{
    /// <summary>
    /// Linearer Anstieg – konstante Beschleunigung ueber die gesamte Strecke.
    /// </summary>
    Linear,

    /// <summary>
    /// Sanfte S-Kurve – ruhiges, gleichmaessiges Anfahren.
    /// </summary>
    Comfort,

    /// <summary>
    /// Fruhes, kraeftiges Beschleunigen – hohes Tempo wird schnell erreicht.
    /// </summary>
    EarlyAcceleration,

    /// <summary>
    /// Spaetes Beschleunigen – lange auf niedriger Geschwindigkeit, dann kraeftiger Antritt.
    /// </summary>
    LateAcceleration,

    /// <summary>
    /// Ausgewogener Verlauf ueber einen mittigen Kontrollpunkt.
    /// </summary>
    BalancedControlPoint
}

