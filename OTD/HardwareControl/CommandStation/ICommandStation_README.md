# ICommandStation - Herstellerunabhängiges Schnittstellen-Interface

## Überblick

Das `ICommandStation`-Interface definiert eine abstrakte Schnittstelle zur Ansteuerung von DCC-Kommandozentralen, unabhängig vom Hersteller. Dies ermöglicht es, verschiedene Kommandozentralen (LoDi-Rektor, Märklin Central, Roco z21, usw.) auszutauschen, ohne die Lok-Steuerungslogik zu ändern.

## Interface-Definition

```csharp
public interface ICommandStation : IDisposable
{
    // Verbindung
    bool IsConnected { get; }
    Task ConnectAsync(string address, int port, CancellationToken cancellationToken = default);
    Task DisconnectAsync();

    // Gleisversorgung
    Task SetPowerAsync(bool isOn, CancellationToken cancellationToken = default);
    Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default);

    // Lokomotivsteuerung
    Task SetLocoSpeedAsync(int address, int speedStep, LocoDirection direction, 
        CancellationToken cancellationToken = default);
    Task SetLocoFunctionAsync(int address, int functionNumber, bool isOn, 
        CancellationToken cancellationToken = default);
    Task EmergencyStopAsync(int address, CancellationToken cancellationToken = default);
    Task EmergencyStopAllAsync(CancellationToken cancellationToken = default);
}
```

## Aktuelle Implementierungen

### LoDiRektor
Vollständige Implementierung für das LoDi-Rektor DCC-Steuergerät von Lokstor Digital.

**Besonderheiten:**
- UDP-basierte Netzwerk-Kommunikation
- DCC126-Protokoll mit bis zu 126 Fahrstufen
- Echtzeitquittierungen (ACK/NACK) für Befehle
- Zubehördecoder- und CV-Programmierungs-Unterstützung

## Verwendungsbeispiel

### Einfache Verwendung mit Loco-Klasse

```csharp
// 1. Kommandozentrale erstellen
ICommandStation commandStation = new LoDiRektor();

// 2. Verbindung herstellen
await commandStation.ConnectAsync("192.168.1.100", 5550);

// 3. Lok-Instanz erstellen
var locoId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
var loco = new Loco(locoId, commandStation);

// 4. Lok konfigurieren
await loco.SetupAsync(
    direction: LocoDirection.Cab1,
    coupling: LocoCoupling.None,
    position: LocoPosition.FrontOrRear
);

// 5. Fahrbefehle senden
await loco.SetSpeedAsync(50);  // 50 km/h
await loco.SetFunctionAsync(0, LocoFunctionState.On);  // Licht an

// 6. Notstopp
await loco.EmergencyStopAsync();

// 7. Verbindung beenden
await commandStation.DisconnectAsync();
commandStation.Dispose();
```

## Erweiterung für neue Hersteller

Um eine neue Kommandozentrale hinzuzufügen, implementieren Sie einfach das `ICommandStation`-Interface:

```csharp
public sealed class MaerklinCentral : ICommandStation
{
    private bool _isConnected;
    
    public bool IsConnected => _isConnected;

    public async Task ConnectAsync(string address, int port, 
        CancellationToken cancellationToken = default)
    {
        // Märklin-spezifische Verbindungslogik
        _isConnected = true;
    }

    public async Task DisconnectAsync()
    {
        _isConnected = false;
    }

    public async Task SetPowerAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        // Märklin-spezifische Gleisversorgungssteuerung
    }

    public async Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default)
    {
        // Märklin-spezifische Gleisversorgungsabfrage
        return _isConnected;
    }

    public async Task SetLocoSpeedAsync(int address, int speedStep, 
        LocoDirection direction, CancellationToken cancellationToken = default)
    {
        // Märklin-spezifische Geschwindigkeitssteuerung
    }

    public async Task SetLocoFunctionAsync(int address, int functionNumber, 
        bool isOn, CancellationToken cancellationToken = default)
    {
        // Märklin-spezifische Funktionssteuerung
    }

    public async Task EmergencyStopAsync(int address, 
        CancellationToken cancellationToken = default)
    {
        // Märklin-spezifischer Nothalt
    }

    public async Task EmergencyStopAllAsync(CancellationToken cancellationToken = default)
    {
        // Märklin-spezifischer Gesamtnothalt
    }

    public void Dispose()
    {
        DisconnectAsync().Wait();
    }
}
```

## Architektur-Vorteile

1. **Herstellerunabhängigkeit**: Lok-Steuerungslogik ist nicht an eine bestimmte Zentrale gebunden
2. **Einfache Migration**: Austausch der Kommandozentrale ohne Änderung der Applikationslogik
3. **Testbarkeit**: Einfaches Erstellen von Mock-Implementierungen für Unit-Tests
4. **Wartbarkeit**: Klare Trennung der Verantwortlichkeiten
5. **Erweiterbarkeit**: Neue Hersteller können hinzugefügt werden, ohne existierenden Code zu ändern

## Fehlerbehandlung

Alle async-Methoden werfen folgende Exceptions:
- `ArgumentNullException`: Ungültige Parameter
- `TimeoutException`: Keine Antwort vom Gerät
- `InvalidOperationException`: Gerät nicht verbunden oder in ungültigem Zustand

Clients sollten diese Exceptions entsprechend handhaben.

