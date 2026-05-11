using System.Collections.Generic;

namespace OTD.TrackPlan;

public sealed class RouteResult
{
    public required TrackSymbol StartSignal { get; init; }

    public required TrackSymbol TargetSignal { get; init; }

    public required IReadOnlyList<TrackSymbol> Symbols { get; init; }

    public required IReadOnlyList<TrackConnection> Connections { get; init; }

    public required IReadOnlyList<SwitchCommand> SwitchCommands { get; init; }

    public int Cost { get; init; }
}

public sealed class SwitchCommand
{
    public required string SwitchId { get; init; }

    public required string SwitchName { get; init; }

    public SwitchPosition Position { get; init; }
}

public sealed class RouteSearchFailure
{
    public required string Message { get; init; }
}

public sealed class RouteSearchResult
{
    private RouteSearchResult(RouteResult? route, RouteSearchFailure? failure)
    {
        Route = route;
        Failure = failure;
    }

    public RouteResult? Route { get; }

    public RouteSearchFailure? Failure { get; }

    public bool IsSuccess => Route is not null;

    public static RouteSearchResult Success(RouteResult route)
    {
        return new RouteSearchResult(route, null);
    }

    public static RouteSearchResult Failed(string message)
    {
        return new RouteSearchResult(null, new RouteSearchFailure { Message = message });
    }
}
