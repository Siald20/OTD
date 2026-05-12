namespace OTD.TrackPlan.Interlocking.Elements;

public class SwitchInterlockingLogic : DefaultElementInterlockingLogic
{
    private const string DemoLockedPositionProperty = "Demo.LockedPosition";

    public SwitchInterlockingLogic()
        : base(TrackSymbolKind.Switch)
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
            // Eine Weiche darf nur gestellt werden, wenn die Suche eine konkrete Lage
            // fuer den Weg durch diese Weiche berechnet hat. Fehlt diese Lage, ist die
            // Route zwar graphisch verbunden, aber stellwerkstechnisch unvollstaendig.
            return new RouteSettingFailure { Message = $"{symbol.Name} hat keine gueltige Weichenlage fuer diese Fahrstrasse." };
        }

        // Demo-Beispiel: Die Weiche ist in einer Lage mechanisch/elektrisch verschlossen.
        // Fuer echte Logik waere das der Ort fuer Weichenverschluss, Ortsbetrieb,
        // Rueckmeldung "Endlage erreicht" oder Aufschneideueberwachung.
        var lockedPosition = GetProperty(symbol, DemoLockedPositionProperty);
        return !string.IsNullOrWhiteSpace(lockedPosition) &&
               !string.Equals(lockedPosition, command.Position.ToString(), System.StringComparison.OrdinalIgnoreCase)
            ? new RouteSettingFailure { Message = $"{symbol.Name} ist in Demo-Lage {lockedPosition} verschlossen." }
            : null;
    }

    public override void Apply(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Einstelllogik fuer Switch hier erweitern.
        base.Apply(context, symbol, result);

        if (!context.TryGetSwitchCommand(symbol.Id, out var command))
        {
            return;
        }

        var drawnSymbol = context.FindDrawnSymbol(symbol.Id);
        if (drawnSymbol is not null)
        {
            // Die Fahrstrassensuche hat die benoetigte Lage berechnet.
            // Beim Stellen wird diese Lage auf das Dokument-Symbol uebertragen,
            // damit die Bedienansicht die neue Weichenlage anzeigen kann.
            drawnSymbol.CurrentSwitchPosition = command.Position;
        }

        result.AddSwitchCommand(command);
    }

    public override void Release(RouteSettingContext context, TrackSymbol symbol, RouteSettingResultBuilder result)
    {
        // TODO: Aufloeselogik fuer Switch hier einbauen.
        base.Release(context, symbol, result);
    }
}
