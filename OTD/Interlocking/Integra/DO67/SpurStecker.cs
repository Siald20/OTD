using System;

public enum Ersatzstecker
{
    NichtVerbunden,
    WSR,
    KSP_WUR
}

public class SpurStecker
{
    private SpurStecker? m_other;
    private Ersatzstecker m_ersst;

    private readonly sbyte[] m_ein = new sbyte[24];
    private readonly sbyte[] m_aus = new sbyte[24];
    private bool m_changed;
    private bool m_updated;

    public SpurStecker()
    {
        m_ersst = Ersatzstecker.NichtVerbunden;
    }

    public bool IsConnected => m_other != null;

    public SpurStecker? ConnectedTo => m_other;

    public void SetErsatzstecker(Ersatzstecker st)
    {
        m_ersst = st;
    }

    public void Commit()
    {
        // Physische Spurstecker-Kreuzung: nur 4/5 und 6/7 sind vertauscht.
        // Alle anderen Spuren, insbesondere 1 und 2, laufen gerade durch.
        int[] idx = { 0, 1, 2, 4, 3, 6, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23 };
        m_changed = false;

        if (m_other == null)
        {
            Array.Clear(m_ein, 0, m_ein.Length);

            // Flankenschutz-Ersatzstecker
            switch (m_ersst)
            {
                case Ersatzstecker.WSR:
                    m_ein[6] = m_aus[3];
                    break;
                case Ersatzstecker.KSP_WUR:
                    m_ein[11] = m_aus[4]; // SAL -> L1
                    m_ein[12] = m_aus[3]; // SAR -> L1
                    m_ein[19] = m_aus[15]; // IS -> IS
                    break;
            }
            return;
        }

        for (int i = 0; i < 24; i++)
        {
            if (m_other.m_aus[i] != m_ein[idx[i]])
                m_changed = true;
                
            m_ein[idx[i]] = m_other.m_aus[i];
        }

        if (m_changed) m_updated = true;
    }

    public sbyte Get(int n) => m_ein[n - 1];

    public sbyte GetOutput(int n) => m_aus[n - 1];
    
    public void Set(int n, sbyte p) => m_aus[n - 1] = p;
    
    public void Set0(int n) => m_aus[n - 1] = 0;
    
    public void SetP(int n, bool p) => m_aus[n - 1] = (sbyte)(p ? 1 : 0);
    
    public void SetM(int n, bool m) => m_aus[n - 1] = (sbyte)(m ? -1 : 0);

    public void SetMP(int n, bool m, bool p)
    {
        if (m && !p) m_aus[n - 1] = -1;
        else if (p && !m) m_aus[n - 1] = 1;
        else m_aus[n - 1] = 0;
    }

    public void SetIf(int n, sbyte p, bool cond) => m_aus[n - 1] = (sbyte)(cond ? p : 0);

    public bool IsP(int n) => m_ein[n - 1] == 1;
    public bool IsM(int n) => m_ein[n - 1] == -1;
    public bool IsAny(int n) => m_ein[n - 1] != 0;

    public bool HasChanged() => m_changed;
    
    public bool NeedsUpdate()
    {
        bool r = m_updated;
        m_updated = false;
        return r;
    }

    public void Reset()
    {
        Array.Clear(m_aus, 0, m_aus.Length);
    }

    // --- Static Connection Helpers ---

    public static void Connect(SpurStecker s1, SpurStecker s2)
    {
        if (ReferenceEquals(s1, s2))
            throw new ArgumentException("Ein Spurstecker kann nicht mit sich selbst verbunden werden.");

        if (s1.m_other != null) s1.m_other.m_other = null;
        if (s2.m_other != null) s2.m_other.m_other = null;

        s1.m_other = s2;
        s2.m_other = s1;
    }

    public static void Disconnect(SpurStecker stecker)
    {
        if (stecker.m_other == null) return;

        var other = stecker.m_other;
        stecker.m_other = null;
        other.m_other = null;
    }

    public static void Conn(SpurStecker s1, SpurStecker s2, int n)
    {
        n--;
        s1.m_aus[n] = s2.m_ein[n];
        s2.m_aus[n] = s1.m_ein[n];
    }

    public static void ConnX(SpurStecker s1, int n1, SpurStecker s2, int n2)
    {
        n1--;
        n2--;
        s1.m_aus[n1] = s2.m_ein[n2];
        s2.m_aus[n2] = s1.m_ein[n1];
    }

    public static void ConnIf(SpurStecker s1, SpurStecker s2, int n, bool cond)
    {
        n--;
        s1.m_aus[n] = (sbyte)(cond ? s2.m_ein[n] : 0);
        s2.m_aus[n] = (sbyte)(cond ? s1.m_ein[n] : 0);
    }

    public static void ConnIfW(SpurStecker s, SpurStecker r, SpurStecker l, int n, bool condR, bool condL)
    {
        n--;
        r.m_aus[n] = (sbyte)(condR ? s.m_ein[n] : 0);
        l.m_aus[n] = (sbyte)(condL ? s.m_ein[n] : 0);
        
        if (condR && !condL) s.m_aus[n] = r.m_ein[n];
        else if (condL && !condR) s.m_aus[n] = l.m_ein[n];
        else s.m_aus[n] = 0;
    }

    public static void ConnIf2(SpurStecker s, int n1, int n2, bool cond)
    {
        n1--;
        n2--;
        s.m_aus[n1] = (sbyte)(cond ? s.m_ein[n2] : 0);
        s.m_aus[n2] = (sbyte)(cond ? s.m_ein[n1] : 0);
    }
}
