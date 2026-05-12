namespace OTD.TrackPlan.Interlocking;

public sealed record LineBlockBoundaryDirectionUpdate(
    string RemoteLineBlockId,
    string RemoteDirection,
    bool IsBlocked);
