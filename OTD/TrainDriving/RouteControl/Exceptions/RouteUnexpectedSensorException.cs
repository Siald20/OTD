// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.RouteControl.Exceptions;

/// <summary>
/// Wird geworfen, wenn ein Sensor aktiviert wird, der aufgrund der aktuellen 
/// RouteLeg-Sequenz nicht erwartet wird.
/// 
/// Indiziert einen Fehlerbetrieb wie:
/// - Zug auf falscher Route
/// - Sensor-Fehlfunktion oder Doppelaktivierung
/// - Gleiswechsel-Fehler
/// - Unerwartete Zugbewegung
/// </summary>
public sealed class RouteUnexpectedSensorException : Exception
{
    public RouteUnexpectedSensorException(string message) : base(message)
    {
    }

    public RouteUnexpectedSensorException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}

