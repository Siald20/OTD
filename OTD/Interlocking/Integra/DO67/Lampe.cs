using System;

public class Lampe
{
    private bool m_an;

    public Lampe()
    {
        m_an = false;
    }

    // Ersetzt "operator bool() const" und "operator=(bool)"
    public bool Value
    {
        get => m_an;
        set => m_an = value;
    }

    public bool Ein()
    {
        return m_an;
    }

    // Wenn du den globalen g_Blinker einbauen möchtest, 
    // müsstest du ihn hier über eine statische Klasse referenzieren.
    public void Set(bool leucht, bool blink = false)
    {
        // Beispiel: m_an = leucht || (blink && Global.g_Blinker);
        m_an = leucht; 
    }
}