namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class BufferStopInterlockingLogic : DefaultElementInterlockingLogic
{
    public BufferStopInterlockingLogic()
        : base(TrackSymbolKind.BufferStop)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               new RouteSettingFailure { Message = $"{symbol.Name} kann nicht Teil einer Fahrstrasse sein." };
    }
}
