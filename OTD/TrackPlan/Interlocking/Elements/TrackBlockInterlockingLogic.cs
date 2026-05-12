namespace OTD.TrackPlan.Interlocking.Elements;

public class TrackBlockInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoReserveOnlyProperty = "Demo.ReserveOnly";

    public TrackBlockInterlockingLogic()
        : base(TrackSymbolKind.TrackBlock)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var baseFailure = base.Validate(context, symbol);
        if (baseFailure is not null)
        {
            return baseFailure;
        }

        if (IsPropertyEnabled(symbol, DemoReserveOnlyProperty))
        {
            // Demo-Beispiel fuer eine betrieblich eingeschraenkte Gleisfreimeldung.
            // Spaeter koennte hier zwischen Rangierfahrstrasse, Zugfahrstrasse,
            // Schutzabschnitt usw. unterschieden werden.
            return new RouteSettingFailure { Message = $"{symbol.Name} ist nur fuer Reservemanoever freigegeben." };
        }

        // Bei Rangierfahrstrassen darf in besetzte Gleise eingestellt werden.
        if (context.Request.RouteType is RouteType.Shunting)
        {
            return null;
        }

        return context.IsOccupied(symbol.Id)
            ? new RouteSettingFailure { Message = $"{symbol.Name} ist belegt." }
            : null;
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer TrackBlock hier einbauen.
        base.Apply(context, symbol, result);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer TrackBlock hier einbauen.
        base.Release(context, symbol, result);
    }
}
