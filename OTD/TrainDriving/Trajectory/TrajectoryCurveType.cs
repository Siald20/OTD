// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.Trajectory;

/// <summary>
/// Selects how speed is distributed over distance.
/// </summary>
public enum TrajectoryCurveType
{
    /// <summary>
    /// Lineare Kurve: Konstante Geschwindigkeitsverteilung über die Distanz.
    /// </summary>
    Linear,
    
    /// <summary>
    /// Kontrollpunkt-Kurve: Geschwindigkeit wird durch Kontrollpunkte definiert.
    /// </summary>
    ControlPoint,
    
    /// <summary>
    /// Ease-In/Out-Kurve: Sanfte Beschleunigung am Anfang und Verzögerung am Ende.
    /// </summary>
    EaseInOut
}
