// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

/// <summary>
/// Consolidated feedback domain types used by RouteControl layout parsing and runtime tracking.
/// </summary>
public static class FeedbackHandling
{
    /// <summary>
    /// Classifies feedback semantics on the route: point contact or occupancy trigger.
    /// </summary>
    public enum FeedbackType
    {
        /// <summary>
        /// Point contact (e.g., reed switch) with a fixed offset on the host track.
        /// </summary>
        ContactFeedback,

        /// <summary>
        /// Occupancy trigger at host-track entry from the current travel direction.
        /// </summary>
        OccupancyFeedback
    }

    /// <summary>
    /// Absolute activation position of a detector relative to the current RouteLeg start.
    /// </summary>
    public sealed record FeedbackActivationPoint(
        int FeedbackId,
        int OffsetCm,
        FeedbackType Type = FeedbackType.ContactFeedback,
        int? ActivationTimeoutMs = null);

    /// <summary>
    /// Layout declaration of an occupancy detector bound to a host track/path.
    /// </summary>
    public sealed record OccupancyFeedback(
        string Id,
        int DetectorId,
        string HostTrackId,
        FeedbackType Type = FeedbackType.OccupancyFeedback,
        string? Description = null);

    /// <summary>
    /// Layout declaration of a point contact detector with a fixed host offset.
    /// </summary>
    public sealed record ContactFeedback(
        string Id,
        int DetectorId,
        string HostTrackId,
        int OffsetCm,
        FeedbackType Type = FeedbackType.ContactFeedback,
        string? Description = null);
}



