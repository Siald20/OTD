using System;

public class Verbindung
{
    private bool m_verbunden;

    // Standardmäßig ist die Verbindung oft getrennt, kann aber im Konstruktor 
    // direkt initialisiert werden (z.B. Verbindung v = new Verbindung(true); )
    public Verbindung(bool verbunden = false)
    {
        m_verbunden = verbunden;
    }

    // Ersetzt das C++ "operator bool() const" (Lesen) und "operator=(bool)" (Schreiben)
    public bool Value
    {
        get => m_verbunden;
        set => m_verbunden = value;
    }
}