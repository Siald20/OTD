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

        // Ein belegter Block darf fuer eine normale Fahrstrasse nicht neu eingestellt werden.
        return context.IsOccupied(symbol.Id)
            ? new RouteSettingFailure { Message = $"{symbol.Name} ist belegt." }
            : null;
    }
}
