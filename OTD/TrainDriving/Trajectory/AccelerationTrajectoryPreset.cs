// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.Trajectory;

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

// <summary>
/// Vordefinierte Kurvenprofile fuer Bremsenphasen (currentSpeed &gt; targetSpeed).
/// </summary>
public enum BrakingTrajectoryPreset
{
    /// <summary>
    /// Linearer Abfall – konstante Verzoegerung ueber die gesamte Strecke.
    /// </summary>
    Linear,

    /// <summary>
    /// Sanfte S-Kurve – ruhiges, gleichmaessiges Abbremsen.
    /// </summary>
    Comfort,

    /// <summary>
    /// Fruehes und kraeftiges Bremsen – Geschwindigkeit wird schnell abgebaut.
    /// </summary>
    AggressiveBrake,

    /// <summary>
    /// Fruehes, aber moderates Bremsen – bei halber Strecke liegt die Geschwindigkeit
    /// bei etwa einem Drittel des Startwerts, danach Ausrollen bis 0.
    /// </summary>
    EarlyBrake,

    /// <summary>
    /// Spaetes Bremsen – lange Haltephase auf hoher Geschwindigkeit, dann starkes Abbremsen.
    /// </summary>
    LateBrake,

    /// <summary>
    /// Ausgewogener Verlauf ueber einen mittigen Kontrollpunkt.
    /// </summary>
    BalancedControlPoint
}
