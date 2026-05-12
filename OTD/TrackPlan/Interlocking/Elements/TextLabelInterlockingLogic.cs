namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class TextLabelInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoOperationalLabelProperty = "Demo.OperationalLabel";

    public TextLabelInterlockingLogic()
        : base(TrackSymbolKind.TextLabel)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               (IsPropertyEnabled(symbol, DemoOperationalLabelProperty)
                   ? new RouteSettingFailure { Message = $"{symbol.Name} ist ein Demo-Hinweis mit betrieblicher Sperre." }
                   : null);
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer TextLabel hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer TextLabel hier einbauen.
        base.Release(context, symbol, result);
    }
}
