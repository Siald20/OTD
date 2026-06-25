/// <summary>
/// Schleife, fuer Block, Streckensperre und dergleichen.
/// Spannungen, Minus, Plus etc beziehen sich immer auf die Leitung A1 (Seite Relais).
/// </summary>
public class Schleife
{
    public enum Spannung
    {
        Minus_Niederohmig = -2,
        Minus_Hochohmig = -1,
        Aus = 0,
        Plus_Hochohmig = 1,
        Plus_Niederohmig = 2
    }

    public enum Leitwert
    {
        Unterbruch,
        Hochohmig,
        Niederohmig
    }

    private Schleife? m_other;
    private Spannung m_spannung;
    private Leitwert m_leitwert_minus;
    private Leitwert m_leitwert_plus;

    public Schleife()
    {
        m_other = null;
        m_spannung = Spannung.Aus;
        m_leitwert_minus = Leitwert.Unterbruch;
        m_leitwert_plus = Leitwert.Unterbruch;
    }

    /// <summary>
    /// Verbindet zwei Schleifen miteinander (entspricht Schleife::connect in C++).
    /// </summary>
    public static void Connect(Schleife? a, Schleife? b)
    {
        if (a != null && b != null)
        {
            a.m_other = b;
            b.m_other = a;
        }
    }

    public bool IsValid()
    {
        return m_other != null;
    }

    // --- Sender ---
    public void SetSpannung(Spannung sp)
    {
        if (m_other != null)
            m_other.m_spannung = sp;
    }

    // --- Empfänger ---
    public Spannung GetSpannung()
    {
        return m_spannung;
    }

    // --- Empfänger ---
    public void SetLeitwert(Leitwert lw_minus, Leitwert lw_plus)
    {
        if (m_other != null)
        {
            m_other.m_leitwert_minus = lw_minus;
            m_other.m_leitwert_plus = lw_plus;
        }
    }

    // --- Sender ---
    public Leitwert GetLeitwertMinus()
    {
        return m_leitwert_minus;
    }

    // --- Sender ---
    public Leitwert GetLeitwertPlus()
    {
        return m_leitwert_plus;
    }
}
