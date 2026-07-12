// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace OTD.TrainDriving.RouteControl.Exceptions;

/// <summary>
/// Wird geworfen, wenn ein Feedback aktiviert wird, der aufgrund der aktuellen 
/// RouteLeg-Sequenz nicht erwartet wird.
/// 
/// Indiziert einen Fehlerbetrieb wie:
/// - Zug auf falscher Route
/// - Feedback-Fehlfunktion oder Doppelaktivierung
/// - Gleiswechsel-Fehler
/// - Unerwartete Zugbewegung
/// </summary>
public sealed class RouteUnexpectedFeedbackInputException : Exception
{
    public RouteUnexpectedFeedbackInputException(string message) : base(message)
    {
    }

    public RouteUnexpectedFeedbackInputException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}


