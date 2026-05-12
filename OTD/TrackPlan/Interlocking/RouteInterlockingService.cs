using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OTD.TrackPlan.Interlocking;

/// <summary>
/// Zentrale Schicht fuer das Stellen einer bereits gefundenen Fahrstrasse.
/// Wichtig: Diese Klasse sucht keine Fahrstrassen. Sie bekommt ein RouteResult aus
/// dem RouteBuilder und prueft dann, ob diese Fahrstrasse betrieblich gestellt werden darf.
/// </summary>
public sealed class RouteInterlockingService
{
    private readonly IInterlockingProfile _profile;

    public RouteInterlockingService(IInterlockingProfile profile)
    {
        _profile = profile;
    }

    public RouteSettingResult TrySetRoute(RouteSettingRequest request)
    {
        // Erst einfache globale Stellwerksbedingungen pruefen. Spaeter koennen hier
        // z. B. feindliche Fahrstrassen, Flankenschutz oder Domino-67-spezifische
        // Start-/Zielabhaengigkeiten zentral ergaenzt werden.
        var context = new RouteSettingContext(request);
        var routeFailure = _profile.ValidateRoute(context);
        if (routeFailure is not null)
        {
            return RouteSettingResult.Failed(routeFailure.Message);
        }

        // Phase 1: Nur pruefen, nichts veraendern.
        // Dadurch wird verhindert, dass z. B. eine Weiche schon gestellt wurde,
        // obwohl ein spaeterer Block die Fahrstrasse noch ablehnt.
        foreach (var symbol in request.Route.Symbols)
        {
            var failure = _profile.GetLogic(symbol.Kind).Validate(context, symbol);
            if (failure is not null)
            {
                return RouteSettingResult.Failed(failure.Message);
            }
        }

        // Phase 2: Alle Pruefungen waren erfolgreich, jetzt duerfen Zustandsaenderungen
        // gesammelt bzw. angewendet werden.
        var resultBuilder = new RouteSettingResultBuilder();
        foreach (var symbol in request.Route.Symbols)
        {
            _profile.GetLogic(symbol.Kind).Apply(context, symbol, resultBuilder);
        }

        _profile.ApplyRoute(context, resultBuilder);
        ScheduleDelayedActions(context, resultBuilder.DelayedActions);

        return RouteSettingResult.Success(resultBuilder, $"Fahrstrasse nach {_profile.Name} gestellt.");
    }

    public RouteSettingResult ReleaseRoute(RouteSettingRequest request)
    {
        var context = new RouteSettingContext(request);
        var resultBuilder = new RouteSettingResultBuilder();

        foreach (var symbol in request.Route.Symbols)
        {
            _profile.GetLogic(symbol.Kind).Release(context, symbol, resultBuilder);
        }

        _profile.ReleaseRoute(context, resultBuilder);
        return RouteSettingResult.Success(resultBuilder, $"Fahrstrasse nach {_profile.Name} aufgeloest.");
    }

    private static void ScheduleDelayedActions(RouteSettingContext context, IReadOnlyList<DelayedAction> delayedActions)
    {
        foreach (var delayedAction in delayedActions)
        {
            _ = RunDelayedActionAsync(context, delayedAction);
        }
    }

    private static async Task RunDelayedActionAsync(RouteSettingContext context, DelayedAction delayedAction)
    {
        await Task.Delay(delayedAction.Delay);
        var delayedResult = new RouteSettingResultBuilder();
        delayedAction.Action(context, delayedResult);
        context.Request.DelayedActionApplied?.Invoke(context, delayedResult);
    }
}
