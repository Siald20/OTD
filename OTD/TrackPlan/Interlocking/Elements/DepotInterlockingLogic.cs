namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class DepotInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoDepotExitReleasedProperty = "Demo.DepotExitReleased";

    public DepotInterlockingLogic()
        : base(TrackSymbolKind.Depot)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               ValidateRequiredProperty(symbol, DemoDepotExitReleasedProperty, $"{symbol.Name} hat in der Demo keine Ausfahrfreigabe.");
    }
}
