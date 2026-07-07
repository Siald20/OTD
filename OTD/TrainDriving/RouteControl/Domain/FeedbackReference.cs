// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record FeedbackReference(
    string TargetId,
    FeedbackReferenceKind TargetKind,
    int OffsetCm,
    string? Role = null,
    int? ActivationTimeoutMs = null);

