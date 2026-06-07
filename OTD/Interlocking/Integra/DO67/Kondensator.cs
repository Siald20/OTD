using System;

public class Kondensator
{
    private int m_ladung;
    private int m_laden;
    private int m_entladen;
    private int m_kapazitaet;
    private int m_an;
    private int m_aus;
    private bool m_trigger;

    public Kondensator()
    {
        m_ladung = 0;
        m_laden = 0;
        m_entladen = 0;
        m_kapazitaet = 0;
        m_an = 0;
        m_aus = 0;
        m_trigger = false;
    }

    public void Init(int kapazitaet, int an, int aus)
    {
        m_kapazitaet = kapazitaet;
        m_an = an;
        m_aus = aus;
    }

    // Ersetzt "operator bool()"
    public bool Value
    {
        get => m_trigger;
    }

    // Ladebedingung
    public void Laden(int schritt, bool bedingung)
    {
        if (bedingung)
        {
            m_laden = schritt;
        }
    }

    // Entladebedingung
    public void Entladen(int schritt, bool bedingung)
    {
        if (bedingung)
        {
            m_entladen = schritt;
        }
    }

    // Laden und Entladen (z.B. parallel zu Spule als Zeitverzögerung)
    public void Set(bool bedingung, int schrittLaden = 25, int schrittEntladen = 1)
    {
        if (bedingung)
        {
            m_laden = schrittLaden;
        }
        else
        {
            m_entladen = schrittEntladen;
        }
    }

    // Optionale Methode, falls du das Update-Verhalten nicht zentral von außen 
    // überschreibst (früher durch LogikManager).
    public void Update()
    {
        // Beispielhafte Logik zur Veranschaulichung:
        if (m_laden > 0)
        {
            m_ladung += m_laden;
            m_laden = 0; // Rücksetzen nach dem Tick
        }
        else if (m_entladen > 0)
        {
            m_ladung -= m_entladen;
            if (m_ladung < 0) m_ladung = 0;
            m_entladen = 0;
        }

        // Trigger-Logik
        if (m_ladung >= m_kapazitaet) 
            m_trigger = true;
        else if (m_ladung <= 0) 
            m_trigger = false;
    }
}