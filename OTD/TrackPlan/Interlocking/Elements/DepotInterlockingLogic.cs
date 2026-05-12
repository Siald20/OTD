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

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer Depot hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Depot hier einbauen.
        base.Release(context, symbol, result);
    }
}
