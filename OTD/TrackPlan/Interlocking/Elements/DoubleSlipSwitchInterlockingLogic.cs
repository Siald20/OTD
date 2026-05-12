namespace OTD.TrackPlan.Interlocking.Elements;

public class DoubleSlipSwitchInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoLockedPositionProperty = "Demo.LockedPosition";

    public DoubleSlipSwitchInterlockingLogic()
        : base(TrackSymbolKind.DoubleSlipSwitch)
    {
    }

    public override RouteSettingFailure? Validate(RouteSettingContext context, TrackSymbol symbol)
    {
        var baseFailure = base.Validate(context, symbol);
        if (baseFailure is not null)
        {
            return baseFailure;
        }

        if (!context.TryGetSwitchCommand(symbol.Id, out var command))
        {
            // Eine DKW hat mehrere moegliche Wege. Ohne berechnete Lage ist nicht klar,
            // welche der Slip-Verbindungen gestellt werden soll.
            return new RouteSettingFailure { Message = $"{symbol.Name} hat keine gueltige Kreuzungsweichenlage fuer diese Fahrstrasse." };
        }

        // Demo-Beispiel analog zur einfachen Weiche. Bei einer echten Domino-67-Logik
        // koennte hier zusaetzlich geprueft werden, ob beide Zungenpaare korrekt frei,
        // verschlossen und ueberwacht sind.
        var lockedPosition = GetProperty(symbol, DemoLockedPositionProperty);
        return !string.IsNullOrWhiteSpace(lockedPosition) &&
               !string.Equals(lockedPosition, command.Position.ToString(), System.StringComparison.OrdinalIgnoreCase)
            ? new RouteSettingFailure { Message = $"{symbol.Name} ist in Demo-Lage {lockedPosition} verschlossen." }
            : null;
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer DoubleSlipSwitch hier erweitern.
        base.Apply(context, symbol, result);

        if (!context.TryGetSwitchCommand(symbol.Id, out var command))
        {
            return;
        }

        var drawnSymbol = context.FindDrawnSymbol(symbol.Id);
        if (drawnSymbol is not null)
        {
            // Die aktuelle DKW-Lage wird im Dokument gespeichert und dadurch in der
            // Iltis-artigen Bedienansicht gruen hervorgehoben.
            drawnSymbol.CurrentSwitchPosition = command.Position;
        }

        result.AddSwitchCommand(command);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer DoubleSlipSwitch hier einbauen.
        base.Release(context, symbol, result);
    }
}
