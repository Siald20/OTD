namespace OTD.TrackPlan.Interlocking.Elements;

public class SignalInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoHoldRedProperty = "Demo.HoldRed";

    public SignalInterlockingLogic()
        : base(TrackSymbolKind.Signal)
    {
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        if (context.Request.RouteType is not RouteType.Train)
        {
            return;
        }

        // TODO: Einstelllogik fuer Signal hier erweitern.
        // Nur das Startsignal der Fahrstrasse wird freigegeben.
        // Zielsignale liegen zwar im Fahrweg, duerfen dadurch aber nicht automatisch
        // auf Fahrt gehen. Demo.HoldRed simuliert eine Bedingung, die das Signal trotz
        // gestellter Fahrstrasse auf Halt laesst.
        if (symbol.Id != context.Request.Route.StartSignal.Id)
        {
            return;
        }

        result.LockSymbol(symbol.Id);
        if (!IsPropertyEnabled(symbol, DemoHoldRedProperty))
        {
            result.AddGreenSignal(symbol.Id);
        }
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Signal hier einbauen.
        base.Release(context, symbol, result);
    }
}
