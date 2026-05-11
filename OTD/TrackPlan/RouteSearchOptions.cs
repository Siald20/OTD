namespace OTD.TrackPlan;

public sealed class RouteSearchOptions
{
    public RouteSearchMode SearchMode { get; init; } = RouteSearchMode.PreferStraightSwitches;

    public bool AllowOccupiedSymbols { get; init; }

    public bool AllowLockedSymbols { get; init; }

    public int MaxVisitedSymbols { get; init; } = 500;

    public IRouteRule Rule { get; init; } = new DefaultRouteRule();
}
