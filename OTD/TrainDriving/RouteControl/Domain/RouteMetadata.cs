// SPDX-License-Identifier: GPL-3.0-or-later

namespace OTD.TrainDriving.RouteControl.Domain;

public sealed record RouteMetadata(
    string? EntrySignal = null,
    string? ExitSignal = null);

