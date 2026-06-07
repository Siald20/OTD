using System;

public enum Stellstrom
{
    Links = -1,
    Aus = 0,
    Rechts = 1
}

public class Weichenantrieb
{
    private int m_cnt;
    private int m_cnt_links;
    private int m_cnt_rechts;
    private Stellstrom m_strom;
    private Weichenantrieb? m_weiche_2;

    // Globale Variable für Zyklen pro Sekunde aus C++ (g_cycles_per_sec) 
    // Hier für das Beispiel auf 50 Ticks gesetzt
    private const int g_cycles_per_sec = 50; 

    public Weichenantrieb()
    {
        m_cnt_links = -g_cycles_per_sec;
        m_cnt_rechts = g_cycles_per_sec;
        m_cnt = m_cnt_rechts;

        m_strom = Stellstrom.Aus;
    }

    public void Init(int ticks, bool plus_ist_links)
    {
        ticks /= 2;
        m_cnt_links = -ticks;
        m_cnt_rechts = ticks;
        if (plus_ist_links)
            m_cnt = m_cnt_links;
        else
            m_cnt = m_cnt_rechts;
    }

    public void SetWeiche2(Weichenantrieb w)
    {
        m_weiche_2 = w;
    }

    public void SetStrom(Stellstrom k)
    {
        m_strom = k;
    }

    public void Commit()
    {
        switch (m_strom)
        {
            case Stellstrom.Links:
                if (m_cnt > m_cnt_links)
                    m_cnt--;
                else if (m_weiche_2 != null) {
                    if (m_weiche_2.m_cnt > m_weiche_2.m_cnt_links)
                        m_weiche_2.m_cnt--;
                }
                break;
            case Stellstrom.Rechts:
                if (m_cnt < m_cnt_rechts)
                    m_cnt++;
                else if (m_weiche_2 != null) {
                    if (m_weiche_2.m_cnt < m_weiche_2.m_cnt_rechts)
                        m_weiche_2.m_cnt++;
                }
                break;
            default:
                break;
        }
    }

    public bool elk_r()
    {
        if (m_weiche_2 != null)
            return m_cnt == m_cnt_rechts && m_weiche_2.m_cnt == m_weiche_2.m_cnt_rechts;
        else
            return m_cnt == m_cnt_rechts;
    }

    public bool elk_nr()
    {
        if (m_weiche_2 != null)
            return m_cnt != m_cnt_rechts || m_weiche_2.m_cnt != m_weiche_2.m_cnt_rechts;
        else
            return m_cnt != m_cnt_rechts;
    }

    public bool elk_l()
    {
        if (m_weiche_2 != null)
            return m_cnt == m_cnt_links && m_weiche_2.m_cnt == m_weiche_2.m_cnt_links;
        else
            return m_cnt == m_cnt_links;
    }

    public bool elk_nl()
    {
        if (m_weiche_2 != null)
            return m_cnt != m_cnt_links || m_weiche_2.m_cnt != m_weiche_2.m_cnt_links;
        else
            return m_cnt != m_cnt_links;
    }
}
