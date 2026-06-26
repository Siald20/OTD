# Feedback Usage Example

Dieses Beispiel ist funktional durch `FeedbackTests.RunSingleModuleAsync(...)` abgedeckt und wird daher als Doku abgelegt (statt als separate Test-Datei im Codepfad).

## Beispiel

```csharp
using System;
using System.Threading.Tasks;
using OTD.HardwareControl;

// Start direct feedback facade for one module
using var feedback = new Feedback(Guid.Parse("eea1ea06-5c31-428f-8992-1c1d160f1130"));
await feedback.ConnectAsync();
await feedback.EnsureOperationalAsync();
Console.WriteLine($"Operational: {feedback.IsOperational}");

// Subscribe to state changes
feedback.SensorStateChanged += (_, e) =>
{
    Console.WriteLine($"Sensor {e.SensorNumber:D4} (Module {e.ProviderUid}): {e.State}");
};

// Query all sensors of this module
var states = await feedback.QueryAllSensorsAsync();
Console.WriteLine($"Queried {states.Count} sensor states for module {feedback.UniqueId}");

// Get sensor count of this module
Console.WriteLine($"Module {feedback.UniqueId}: {feedback.SensorCount} sensors");

// Clean shutdown
await feedback.DisconnectAsync();
```

## Konfiguration (`OTD/AppData/deviceconfig.xml`)

```xml
<feedbackmodules>
  <feedbackmodule uid="eea1ea06-5c31-428f-8992-1c1d160f1130" driver="lodi-s88-commander">
    <connection ip="192.168.1.51" port="11092" />
    <startup queryOnStartup="true" subscribeOnStartup="true" />
    <modules>
      <module address="1" />
      <module address="2" />
    </modules>
  </feedbackmodule>
</feedbackmodules>
```

## Hinweis

Für interaktive Ausführung bitte `RunTests` verwenden und dort den Eintrag `Feedback Einzelmodul` auswählen.
