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
}
