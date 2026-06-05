using System;


public class Relais
{
    public string Name { get; private set; }
    
    // Die konfigurierte Anzugsverzögerung in Millisekunden
    private readonly double _anzugsVerzoegerungMs;
    
    // Der interne Timer für die Anzugsphase
    private double _anzugsTimer;

    // Gibt an, ob der Relaiskontakt aktuell physikalisch geschlossen ist
    public bool IstAngezogen { get; private set; }

    // Gibt an, ob aktuell Strom an der Spule anliegt
    public bool HatSpannung { get; private set; }

    /// <summary>
    /// Erstellt ein neues simuliertes Relais mit definierter Anzugsverzögerung.
    /// </summary>
    /// <param name="name">Name des Relais</param>
    /// <param name="anzugsVerzoegerungMs">Zeit zum Anziehen in Millisekunden (Standard 20ms)</param>
    public Relais(string name, double anzugsVerzoegerungMs = 20.0)
    {
        Name = name;
        _anzugsVerzoegerungMs = anzugsVerzoegerungMs;
        _anzugsTimer = anzugsVerzoegerungMs;
        
        IstAngezogen = false;
        HatSpannung = false;
    }

    /// <summary>
    /// Legt Spannung an die Relaisspule an oder schaltet sie ab.
    /// </summary>
    public void SetzeSpannung(bool unterSpannung)
    {
        HatSpannung = unterSpannung;
    }

    /// <summary>
    /// Berechnet die physikalische Bewegung des Relaisankers.
    /// </summary>
    /// <param name="deltaMs">Vergangene Millisekunden seit dem letzten Simulationsschritt</param>
    public void UpdateSimulation(double deltaMs)
    {
        if (HatSpannung)
        {
            if (!IstAngezogen)
            {
                // Das Relais zieht verzögert an
                _anzugsTimer -= deltaMs;
                if (_anzugsTimer <= 0)
                {
                    IstAngezogen = true;
                }
            }
        }
        else
        {
            // OHNE ABFALLVERZÖGERUNG: Relais fällt sofort ab, wenn der Strom weg ist
            IstAngezogen = false;
            
            // Timer für das nächste Mal wieder auf den vollen Wert aufladen
            _anzugsTimer = _anzugsVerzoegerungMs;
        }
    }

    /// <summary>
    /// Zwingt das Relais sofort zurück in die stromlose Grundstellung.
    /// </summary>
    public void Grundstellung()
    {
        HatSpannung = false;
        IstAngezogen = false;
        _anzugsTimer = _anzugsVerzoegerungMs;
    }
}