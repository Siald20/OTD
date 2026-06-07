using System;

/// <summary>
/// TMN503_ZSR: Vollständige 1:1 Übersetzung des Zwergsignal-Satzes.
/// Basierend auf der gelieferten TMN503_ZSR.cpp Logik.
/// </summary>
public class TMN503_ZSR : RelaisSatz
{
    // --- Relais-Instanzen (Exakt aus C++ Logik) ---
    public Relais RM1 = new Relais();    public Relais RM2 = new Relais();
    public Relais VP = new Relais();     public Relais MZ = new Relais();
    public Relais MR = new Relais();     public Relais SF = new Relais();
    public Relais FU = new Relais();     public Relais S = new Relais();
    public Relais Z = new Relais();      public Relais TS = new Relais();
    public Relais TZ = new Relais();     public Relais ZV = new Relais();
    public Relais SV = new Relais();     public Relais FV = new Relais();
    public Relais FU_ZR = new Relais();  public Relais MRZ = new Relais();
    public Relais AS = new Relais();     public Relais A = new Relais();
    public Relais SH = new Relais();     public Relais ZE = new Relais();
    public Relais VFU = new Relais();    public Relais BAH = new Relais();

    // --- Lampen ---
    public Lampe l_BEL = new Lampe();
    public Lampe l_RM = new Lampe();
    public Lampe l_ws_1 = new Lampe();
    public Lampe l_rt_1 = new Lampe();
    public Lampe l_ws_2 = new Lampe();
    public Lampe l_rt_2 = new Lampe();
    public Lampe l_ws_ni = new Lampe();
    public Lampe l_ST = new Lampe();
    public Lampe l_ZT = new Lampe();

    // --- Tasten & Inputs ---
    public Taste t_ST = new Taste();
    public Taste t_ZT = new Taste();
    public Input tfu_ST = new Input();
    public Input tfu_ZT = new Input();
    public Input i_IS1 = new Input();
    public Input i_IS2 = new Input();
    
    // (Aus Spur 10 abgeleitet)
    public Input i_8_ZE_HIS = new Input(); 
    public Input i_8_ZE_h = new Input();

    // --- Spurstecker ---
    public SpurStecker sk_A = new SpurStecker();
    public SpurStecker sk_AH = new SpurStecker();
    public SpurStecker sk_EH = new SpurStecker();

    // --- Projektierung / Verbindungen ---
    public Verbindung p_Links = new Verbindung();
    public Verbindung v_1_Z = new Verbindung();

    private Do67 m_zv;
    private Zwergsignal m_zsig;

    public TMN503_ZSR(Do67 z, bool nl)
    {
        m_zv = z;
        m_zv.Add(this);
        m_zsig = null;

        if (Global.ENABLE_DUMMY_ZSIG)
        {
            m_zsig = new Zwergsignal();
        }

        p_Links.Value = nl;
        l_BEL.Value = false;

        if (Global.INIT_IS_FREI)
        {
            i_IS1.Value = true;
            i_IS2.Value = true;
        }

        v_1_Z.Value = true;
    }

    public void SetZS(Zwergsignal zs) { m_zsig = zs; }

    public override void Update()
    {
        bool t;

        sk_A.Reset();
        sk_AH.Reset();
        sk_EH.Reset();

        bool IS1 = i_IS1.Value;
        bool IS2 = i_IS2.Value;

        bool ST = (m_zv.sl_TR_ZS.Value && t_ST.Value) || (m_zv.sl_TRFU_ZS.Value && tfu_ST.Value);
        bool ZT = (m_zv.sl_TR_ZS.Value && t_ZT.Value) || (m_zv.sl_TRFU_ZS.Value && tfu_ZT.Value);

        // --- Seite 1 ---
        if (m_zsig != null) {
            m_zsig.L1 = !SF.Value;
            m_zsig.L2 = SF.Value || !FU.Value;
            m_zsig.L3 = FU.Value && (MR.Value || MZ.Value);

            RM1.Value = !(VP.Value && !MZ.Value && !MR.Value) && ((!SF.Value && m_zsig.L1) || (SF.Value && m_zsig.L2));
            RM2.Value = !(VP.Value && !MZ.Value && !MR.Value) && ((!FU.Value && m_zsig.L2) || (FU.Value && (MR.Value || MZ.Value) && m_zsig.L3));
        } else {
            RM1.Value = false;
            RM2.Value = false;
        }

        // --- Seite 2: Spur 1/2 ---
        {
            bool a1e, a2e;
            bool a1a, a2a;

            if (p_Links.Value) {
                a1e = sk_A.IsP(2);
                a2e = sk_A.IsP(1);
            } else {
                a1e = sk_A.IsP(1);
                a2e = sk_A.IsP(2);
            }

            S.Value = ((a1e && !Z.Value && !TZ.Value && ST && !VP.Value && ((!MR.Value && !FU.Value) || (MR.Value && FU.Value))) || (a2e && S.Value && (!VP.Value || !FU.Value || (RM1.Value && RM2.Value)))) && !TS.Value;
            Z.Value = ((a1e && !S.Value && !TZ.Value && !TS.Value) || (m_zv.sl_SP.Value && ZT && !TZ.Value && Z.Value) || (a2e && !S.Value && !TS.Value && Z.Value)) && ((ZT && v_1_Z.Value) || Z.Value);
            
            if (m_zv.sl_SPL.Value && ZT && Z.Value) Z.Value = false;

            // Kleiner Timing-Hack aus der CPP
            t = ((sk_AH.IsP(1) && TS.Value) || (a1e && !Z.Value && !TZ.Value)) && !S.Value && (TS.Value || (!ST && !ZT));
            TZ.Value = (sk_EH.IsP(1) && !S.Value && !TS.Value && !Z.Value) || (a2e && !S.Value && !TS.Value && TZ.Value);
            TS.Value = t;

            a1a = (((sk_AH.IsP(1) && TS.Value) || S.Value) && !TZ.Value && !Z.Value) || (sk_EH.IsP(2) && TZ.Value && !S.Value) || (!S.Value && !TZ.Value && !TS.Value && Z.Value && a2e);
            a2a = (a1e && !Z.Value && !TZ.Value && ST && !VP.Value && ((!MR.Value && !FU.Value) || (MR.Value && FU.Value)) && (!VP.Value || !FU.Value || (RM1.Value && RM2.Value)) && S.Value)
            || (sk_AH.IsP(2) && TS.Value) || (sk_EH.IsP(1) && !S.Value && !TS.Value && !Z.Value && TZ.Value) || (m_zv.sl_SP.Value && ZT && !TZ.Value && !TS.Value && !S.Value);

            sk_EH.SetP(1, !S.Value && !TS.Value && !Z.Value && TZ.Value && a2e);
            sk_EH.SetP(2, a1e && !S.Value && TZ.Value);
            sk_AH.SetP(1, TS.Value && (S.Value || (!Z.Value && !TZ.Value && a1e)));
            sk_AH.SetP(2, (a2a || a2e) && TS.Value);

            if (p_Links.Value) {
                sk_A.SetP(2, a1a);
                sk_A.SetP(1, a2a);
            } else {
                sk_A.SetP(1, a1a);
                sk_A.SetP(2, a2a);
            }
        }

        // --- Spur 3 ---
        if (((m_zv.sl_VP.Value && !FV.Value && !ZT && Z.Value) || (sk_EH.IsP(3) && TZ.Value)) && !VFU.Value && !SV.Value && !AS.Value) ZV.Value = true;
        sk_A.SetP(3, ZV.Value && ((sk_EH.IsP(3) && TZ.Value) || (m_zv.sl_VP.Value && !FV.Value && !ZT && Z.Value)));
        VP.Value = sk_A.IsP(3) && !ZV.Value && (S.Value || TS.Value);
        sk_AH.SetP(3, VP.Value && TS.Value);

        // --- Seite 3: Spur 4 ---
        sk_A.SetM(4, true);

        // --- Spur 5 ---
        SV.Value = sk_A.IsP(5) && (SV.Value || !RM1.Value || !RM2.Value);

        // --- Spur 6 ---
        sk_A.SetP(6, SV.Value);

        // --- Spur 7 ---
        sk_A.SetP(7, TZ.Value || FV.Value);

        // --- Spur 8 ---
        if (sk_EH.IsP(8) && TZ.Value && !FV.Value) FV.Value = true;

        if (ZV.Value && (!AS.Value || (IS1 && A.Value)))
        {
            if (FV.Value) sk_A.Set(8, sk_EH.Get(8));
            else sk_A.SetM(8, true);
        }

        MZ.Value = sk_A.IsP(8) && !ZV.Value && FU.Value && !BAH.Value;
        MR.Value = sk_A.IsM(8) && !ZV.Value && FU.Value && !BAH.Value;
        FU_ZR.Value = sk_A.IsAny(8) && (FU.Value || (RM1.Value && RM2.Value && !SF.Value && VP.Value && !MZ.Value));
        
        if (m_zv.sl_NH.Value && ST && FU.Value && MR.Value) FU_ZR.Value = false;

        sk_AH.SetP(8, (!AS.Value || !SH.Value || !ZE.Value) && MZ.Value);

        MRZ.Value = MR.Value || MZ.Value;
        bool s1_VFU = FU_ZR.Value;
        if (s1_VFU) VFU.Value = true;
        FU.Value = VFU.Value && FU_ZR.Value;

        // --- Spur 9 ---
        SF.Value = (sk_A.IsP(9) || FV.Value) && FU.Value;
        sk_A.SetP(9, FV.Value);

        // --- Seite 4: Spur 10 ---
        A.Value = (((m_zv.sl_AAUF.Value && ((MZ.Value && !A.Value && AS.Value) || MR.Value)) || sk_A.IsP(10)) && ZV.Value) || sk_AH.IsP(10);
        AS.Value = m_zv.sl_AAUF.Value && (ZV.Value || MZ.Value) && (AS.Value || (A.Value && IS1));
        sk_A.SetP(10, m_zv.sl_AAUF.Value && ((MZ.Value && !A.Value && AS.Value) || MR.Value));

        SH.Value = MZ.Value && A.Value && (SH.Value || IS2);
        ZE.Value = (MZ.Value && AS.Value && SH.Value) || (i_8_ZE_HIS.Value && IS1 && IS2) || (i_8_ZE_h.Value && ZE.Value);

        // --- Spur 11 ---
        if (!ZV.Value && FV.Value) FV.Value = false;
        if (((AS.Value && ZV.Value) || sk_A.IsP(11)) && VFU.Value && !s1_VFU) VFU.Value = false;
        sk_EH.SetP(11, !ZV.Value);
        sk_A.SetP(11, AS.Value && ZV.Value);

        // --- Spur 12 ---
        if (AS.Value && !A.Value) ZV.Value = false;
        t = (m_zv.sl_BA.Value && ZT && !FV.Value) || (sk_EH.IsP(12) && (!ZV.Value || FV.Value)) || (sk_A.IsP(12) && !VFU.Value && !ZV.Value);
        if (t) ZV.Value = false;
        
        sk_EH.SetP(12, (!ZV.Value || FV.Value) && ((m_zv.sl_BA.Value && ZT && !FV.Value) || (sk_A.IsP(12) && ZT && !FV.Value)));
        sk_A.SetP(12, (!VFU.Value && !ZV.Value) && ((m_zv.sl_BA.Value && ZT && !FV.Value) || (sk_EH.IsP(12) && (!ZV.Value || FV.Value))));
        
        BAH.Value = (sk_EH.IsP(12) && (!ZV.Value || FV.Value)) || (m_zv.sl_BA.Value && ZT && !FV.Value) || (sk_A.IsP(12));
        if (BAH.Value && VFU.Value && BAH.Value) VFU.Value = false; // "BAH.spule()" ist i.d.R. identisch mit BAH.Value in dieser C# Logik.

        // --- Spur 13 --- (Leer in der .cpp)

        // --- Spur 14 ---
        SpurStecker.ConnIfW(sk_A, sk_AH, sk_EH, 14, TS.Value, TZ.Value);

        // --- Spur 15 ---
        t = !FV.Value && !SH.Value && !IS1 && !IS2; 
        SpurStecker.ConnIfW(sk_A, sk_AH, sk_EH, 15, t && (!RM1.Value || !RM2.Value) && MR.Value && VP.Value, t && ZV.Value);

        // --- Spur 16 - 20 ---
        sk_A.Set(16, sk_EH.Get(16));
        sk_A.Set(17, sk_EH.Get(17));
        sk_A.Set(18, sk_EH.Get(18));
        sk_A.Set(19, sk_EH.Get(19)); 
        sk_A.Set(20, sk_EH.Get(20));

        if (!FV.Value)
        {
            sk_AH.Set(16, sk_A.Get(16));
            sk_AH.Set(17, sk_A.Get(17));
            sk_AH.Set(18, sk_A.Get(18));
            sk_AH.Set(19, sk_A.Get(19)); 
            sk_AH.Set(20, sk_A.Get(20));
        }

        // --- Spur 21 ---
        sk_A.SetP(21, sk_EH.IsP(21) && !ZV.Value && !FU.Value);

        // --- Spur 22 --- (Leer in .cpp)

        // --- Spur 23 ---
        bool o_23 = sk_EH.IsP(23) || sk_A.IsP(23);
        SpurStecker.Conn(sk_EH, sk_A, 23);

        // --- Spur 24 --- (Leer in .cpp)
    }

    public override void Output()
    {
        l_BEL.Value = !SV.Value && !FU.Value && !ZV.Value;

        l_RM.Value = (m_zv.sl_ML.Value && MRZ.Value && !RM1.Value && !RM2.Value) || (m_zv.sl_BLI.Value && !VP.Value && (RM1.Value || RM2.Value));
        l_ws_1.Value = m_zv.sl_ML.Value && (ZV.Value || MZ.Value) && !i_IS1.Value;
        l_rt_1.Value = m_zv.sl_ML.Value && i_IS1.Value;
        l_ws_2.Value = m_zv.sl_ML.Value && (ZV.Value || MZ.Value) && !i_IS2.Value;
        l_rt_2.Value = m_zv.sl_ML.Value && i_IS2.Value;
        l_ws_ni.Value = m_zv.sl_ML.Value && (ZV.Value || MZ.Value);
        l_ST.Value = m_zv.sl_BLI.Value && S.Value;
        l_ZT.Value = m_zv.sl_BLI.Value && Z.Value;
    }

    public override bool UpdateWire()
    {
        sk_A.Commit();
        sk_AH.Commit();
        sk_EH.Commit();

        return sk_A.HasChanged() || sk_AH.HasChanged() || sk_EH.HasChanged();
    }

    public void Tp(ref bool l1, ref bool l2, ref bool l3, bool y)
    {
        bool ST = (m_zv.sl_TR_ZS.Value && t_ST.Value) || (m_zv.sl_TRFU_ZS.Value && tfu_ST.Value);
        bool ZT = (m_zv.sl_TR_ZS.Value && t_ZT.Value) || (m_zv.sl_TRFU_ZS.Value && tfu_ZT.Value);

        if (ST && (y == p_Links.Value))
        {
            l3 = l2;
            l2 = l1;
            l1 = false;
        }

        if (ZT && (y != p_Links.Value))
        {
            l3 = l2;
            l2 = l1;
            l1 = false;
        }
    }
}