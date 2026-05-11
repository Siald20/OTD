namespace OTD.TrackPlan.Interlocking.Elements;

public sealed class SensorInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoSensorClearProperty = "Demo.SensorClear";

    public SensorInterlockingLogic()
        : base(TrackSymbolKind.Sensor)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        return base.Validate(context, symbol) ??
               ValidateRequiredProperty(symbol, DemoSensorClearProperty, $"{symbol.Name} meldet in der Demo nicht frei.");
    }
}
