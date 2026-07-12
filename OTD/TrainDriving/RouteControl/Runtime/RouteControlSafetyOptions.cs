// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Runtime;

/// <summary>
/// Prozessweite Sicherheitsoptionen fuer RouteControl-Feedbacktracking.
/// </summary>
public static class RouteControlSafetyOptions
{
    /// <summary>
    /// Aktiviert die Notbremsung bei unerwarteter Feedbackaktivierung vor der Zugspitze.
    /// </summary>
    public static bool EnableUnexpectedAheadFeedbackInputEmergencyStop { get; set; }

    /// <summary>
    /// Toleranz in cm fuer unerwartete Feedbacken vor dem naechst erwarteten Feedback.
    /// Notbremsung nur bei Delta > Toleranz.
    /// </summary>
    public static double UnexpectedAheadFeedbackInputToleranceCm { get; set; } = 30.0;

    /// <summary>
    /// Aktiviert die Notbremsung bei Liegenbleiben/Stall vor dem naechsten erwarteten Rueckmelder.
    /// </summary>
    public static bool EnableStuckAlertEmergencyStop { get; set; }

    /// <summary>
    /// Gemeinsamer relativer Slack in Prozent fuer den StuckAlert zwischen letztem aktiviertem Rueckmelder
    /// und dem naechsten erwarteten Rueckmelder.
    ///
    /// Die zulaessige Zusatzstrecke wird als `(Segmentlaenge × SAT) + 15 cm` berechnet.
    /// Dieselbe physische Zusatzstrecke wird anschliessend ueber die aktuelle Ueberwachungsgeschwindigkeit
    /// in eine minimale Wartezeit umgerechnet. Damit gelten Distanz- und Zeitgate konsistent auf derselben Basis.
    /// </summary>
    public static double StuckAlertTolerancePercent { get; set; } = 25.0;
}

