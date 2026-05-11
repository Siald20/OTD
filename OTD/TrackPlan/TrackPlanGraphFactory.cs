namespace OTD.TrackPlan;

public sealed class TrackPlanGraphFactory
{
    public TrackPlanGraph CreateGraph(TrackPlanDocument document)
    {
        var graph = new TrackPlanGraph();

        foreach (var drawnSymbol in document.Symbols)
        {
            graph.AddSymbol(new TrackSymbol
            {
                Id = drawnSymbol.Id,
                Name = drawnSymbol.Name,
                Kind = drawnSymbol.Kind,
                X = drawnSymbol.X,
                Y = drawnSymbol.Y,
                SignalDirection = drawnSymbol.SignalDirection,
                CurrentSwitchPosition = drawnSymbol.CurrentSwitchPosition,
                SwitchRouteOptions = drawnSymbol.SwitchRouteOptions,
                Properties = drawnSymbol.Properties
            });
        }

        foreach (var drawnConnection in document.Connections)
        {
            var connection = new TrackConnection
            {
                FromSymbolId = drawnConnection.FromSymbolId,
                FromPort = drawnConnection.FromPort,
                ToSymbolId = drawnConnection.ToSymbolId,
                ToPort = drawnConnection.ToPort,
                Cost = drawnConnection.Cost,
                IsEnabled = drawnConnection.IsEnabled
            };

            graph.AddConnection(connection);

            if (drawnConnection.IsBidirectional)
            {
                graph.AddConnection(connection.Reverse());
            }
        }

        return graph;
    }
}
