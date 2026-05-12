namespace OTD.TrackPlan.Interlocking;

public sealed record LineBlockBoundaryLink(
    string LocalLineBlockId,
    string RemoteLineBlockId);
