using System;

/// <summary>
/// TMN502_ZGR: Vollständige 1:1 Übersetzung des Zwergsignal-Gruppenrelais-Satzes.
/// </summary>
public class TMN502_ZGR : RelaisSatz
{
    public bool A1Input { get; private set; }
    public bool E1Input { get; private set; }
    public bool A2Input { get; private set; }
    public bool E2Input { get; private set; }

    // --- Relais-Instanzen (Vollständig, abgeleitet aus der C++ Logik) ---
    public Relais RM1 = new Relais();    public Relais RM2 = new Relais();
    public Relais VP = new Relais();     public Relais MZ = new Relais();
    public Relais MR = new Relais();     public Relais SF = new Relais();
    public Relais FU = new Relais();     public Relais D = new Relais();
    public Relais S = new Relais();      public Relais TS = new Relais();
    public Relais TZ = new Relais();     public Relais Z = new Relais();
    public Relais ZV1 = new Relais();    public Relais ZV2 = new Relais();
    public Relais DV = new Relais();     public Relais DSA_ZR = new Relais();
    public Relais SV = new Relais();     public Relais FV = new Relais();
    public Relais DFU = new Relais();    public Relais FU_ZR = new Relais();
    public Relais MRZ_ZR = new Relais(); public Relais AS = new Relais();
    public Relais A = new Relais();      public Relais AH = new Relais();
    public Relais VFU = new Relais();    public Relais BAD = new Relais();
    public Relais BAH = new Relais();    public Relais NA = new Relais();

    // --- Lampen ---
    public Lampe l_RM = new Lampe();
    public Lampe l_ws = new Lampe(); // 13.1
    public Lampe l_rt = new Lampe(); // 12.1
    public Lampe l_ni = new Lampe(); // 8.4
    public Lampe l_ST = new Lampe();
    public Lampe l_ZT = new Lampe();
    public Lampe l_SZT = new Lampe();
    public Lampe l_BEL = new Lampe();

    // --- Tasten & Inputs ---
    public Taste t_ST = new Taste();
    public Taste t_ZT = new Taste();
    public Taste t_SZT = new Taste();
    public Input tfu_ST = new Input();
    public Input tfu_ZT = new Input();
    public Input tfu_SZT = new Input();
    public Input i_IS = new Input();

    // --- Spurstecker ---
    public SpurStecker sk_A = new SpurStecker();
    public SpurStecker sk_E = new SpurStecker();
    public SpurStecker sk_AH = new SpurStecker();
    public SpurStecker sk_EH = new SpurStecker();

    // --- Projektierung / Verbindungen ---
    public Verbindung p_Links = new Verbindung();
    public Verbindung p_Transit = new Verbindung();
    public Verbindung p_ZV_bei_SV = new Verbindung();
    public Verbindung p_Rueckaufloesung = new Verbindung();
    public Verbindung p_Frueh_Halt = new Verbindung();
    public Verbindung v_15_GS = new Verbindung();
    public Verbindung v_4_IS = new Verbindung(); // Angenommen aus Spur 4 Logik
    public Verbindung v_15_SIU = new Verbindung(); // Angenommen aus Spur 15 Logik

    private Do67 m_zv;
    private Zwergsignal m_zsig;

    public TMN502_ZGR(Do67 z, bool nach_links)
    {
        m_zv = z;
        m_zv.Add(this);
        m_zsig = null;
        
        if (Global.ENABLE_DUMMY_ZSIG)
        {
            m_zsig = new Zwergsignal();
        }

        p_Links.Value = nach_links;
        p_Transit.Value = true;
        p_ZV_bei_SV.Value = true;
        p_Rueckaufloesung.Value = false;
        p_Frueh_Halt.Value = false;
        v_15_GS.Value = true;
        l_BEL.Value = false;

        if (Global.INIT_IS_FREI)
        {
            i_IS.Value = true;
        }

        SpurStecker.Connect(sk_EH, sk_AH);
    }

    public void SetZS(Zwergsignal zs) { m_zsig = zs; }
    
    public Zwergsignal GetZwerg() { return m_zsig; }

    public bool Zv_zsr() { return (!RM1.Value && !RM2.Value) || VP.Value; }

    public override void Update()
    {
        bool t;

        sk_A.Reset();
        sk_E.Reset();
        sk_AH.Reset();
        sk_EH.Reset();

        // --- Seite 1 ---
        if (m_zsig != null) 
        {
            m_zsig.L1 = !SF.Value;
            m_zsig.L2 = SF.Value || !FU.Value;
            m_zsig.L3 = FU.Value && (MR.Value || MZ.Value);

            RM1.Value = !(VP.Value && !MZ.Value && !MR.Value) && ((!SF.Value && m_zsig.L1) || (SF.Value && m_zsig.L2));
            RM2.Value = !(VP.Value && !MZ.Value && !MR.Value) && ((!FU.Value && m_zsig.L2) || (FU.Value && (MR.Value || MZ.Value) && m_zsig.L3));
        }
        else 
        {
            RM1.Value = false;
            RM2.Value = false;
        }

        bool IS = i_IS.Value;

        bool ST = (m_zv.sl_TR_ZS.Value && (t_ST.Value || t_SZT.Value)) || (m_zv.sl_TRFU_ZS.Value && (tfu_ST.Value || tfu_SZT.Value));
        bool ZT = (m_zv.sl_TR_ZS.Value && (t_ZT.Value || t_SZT.Value)) || (m_zv.sl_TRFU_ZS.Value && (tfu_ZT.Value || tfu_SZT.Value));

        // --- Seite 2: Spur 1/2 ---
        {
            bool a1i, e1i, a2i, e2i;
            bool a1o = false, e1o = false, a2o = false, e2o = false;

            if (!p_Links.Value)
            {
                a1i = sk_A.IsP(1); e1i = sk_E.IsP(1);
                a2i = sk_A.IsP(2); e2i = sk_E.IsP(2);
            }
            else
            {
                a1i = sk_A.IsP(2); e1i = sk_E.IsP(2);
                a2i = sk_A.IsP(1); e2i = sk_E.IsP(1);
            }

            A1Input = a1i;
            E1Input = e1i;
            A2Input = a2i;
            E2Input = e2i;

            D.Value = (e2i && !TZ.Value && !Z.Value && !TS.Value && !S.Value && (!ZT || D.Value)) || (a2i && D.Value);
            S.Value = (( !D.Value && !TS.Value) && ST && !VP.Value && ((!MR.Value && !FU.Value) || ((MR.Value || MZ.Value) && FU.Value))) ||
                      ((sk_AH.IsP(2) || a2i) && S.Value && (!FU.Value || !VP.Value));
            TS.Value = ((a1i && !D.Value) || (sk_AH.IsP(1) && TS.Value)) &&
                       !S.Value && (TS.Value || !ST);
            TZ.Value = (sk_EH.IsP(1) && !D.Value && !Z.Value && (TZ.Value || p_Transit.Value)) ||
                       (TZ.Value && !D.Value && e1i);
            Z.Value = (Z.Value || ZT) && (( !TZ.Value && !D.Value) || (Z.Value && ((!TZ.Value && ZT && (!RM1.Value && !RM2.Value) && m_zv.sl_SP.Value) || (e1i && !D.Value))));
            
            if (ZT && Z.Value && m_zv.sl_SPL.Value) Z.Value = false;

            a1o = (e1i && D.Value) || (!D.Value && !TS.Value && S.Value) || (sk_AH.IsP(1) && TS.Value && !D.Value);
            e1o = (sk_EH.IsP(1) && !D.Value && !Z.Value && TZ.Value) || ((m_zv.sl_SP.Value && (!RM1.Value || !RM2.Value) && ZT && !TZ.Value) && !D.Value) || (a1i && D.Value) || (e2i && Z.Value && !D.Value);
            a2o = sk_AH.IsP(2) || (e2i && !TZ.Value && !Z.Value && !TS.Value && !S.Value && D.Value) || (S.Value && (!FU.Value || !VP.Value || (RM1.Value && RM2.Value)) && ST);
            e2o = (TZ.Value && sk_EH.IsP(2)) || (!TZ.Value && ((!Z.Value && !TS.Value && !S.Value && D.Value && a2i) || (Z.Value && !D.Value && e1i)));

            sk_AH.SetP(1, a1i && !D.Value && TS.Value);
            sk_AH.SetP(2, a2i);
            sk_AH.SetP(4, ((a1i && !D.Value && TS.Value) || sk_AH.IsP(1)) && (ZV1.Value || IS));
            sk_EH.SetP(1, !Z.Value && !D.Value && TZ.Value && e1i); 
            sk_EH.SetP(2, TZ.Value && e2i);

            if (!p_Links.Value) {
                sk_A.SetP(1, a1o); sk_A.SetP(2, a2o);
                sk_E.SetP(1, e1o); sk_E.SetP(2, e2o);
            } else {
                sk_A.SetP(1, a2o); sk_A.SetP(2, a1o);
                sk_E.SetP(1, e2o); sk_E.SetP(2, e1o);
            }
        }

        // --- Seite 3: Spur 3 ---
        t = sk_E.IsP(3) && !ZV1.Value && !VFU.Value && !NA.Value && !SV.Value && D.Value;
        if (t && !BAD.Value) DV.Value = true;
        bool s2_AS = t && !BAD.Value && !A.Value && AS.Value;

        sk_A.SetP(3, t);

        t = (FU.Value && TS.Value) || (TZ.Value && !TS.Value) || ZV1.Value;
        if (((((sk_EH.IsP(3) && TZ.Value) && t) || sk_AH.IsP(7) || (m_zv.sl_VP.Value && (p_ZV_bei_SV.Value || !SV.Value) && !FV.Value && !ZT && Z.Value)) && !DV.Value && !DFU.Value && !AS.Value)) {
            ZV1.Value = true;
            ZV2.Value = true;
        }

        sk_E.SetP(3, (sk_EH.IsP(3) && TZ.Value && !DV.Value) || (sk_AH.IsP(7) && t && !DV.Value) || (m_zv.sl_VP.Value && !FV.Value && !ZT && Z.Value && t && !DV.Value));

        DSA_ZR.Value = sk_A.IsP(7);

        VP.Value = sk_A.IsP(3) && !DV.Value && !DSA_ZR.Value && (TS.Value || S.Value);
        sk_AH.SetP(3, TS.Value && VP.Value && (!ZV1.Value || FU.Value));

        // --- Spur 4-7 ---
        sk_E.SetM(4, true);
        sk_A.SetIf(4, sk_E.Get(5), !IS || v_4_IS.Value);

        SV.Value = sk_A.IsP(5) && (SV.Value || !RM1.Value || !RM2.Value) && (p_ZV_bei_SV.Value || !ZV1.Value);

        sk_A.SetP(6, SV.Value); 
        sk_E.SetP(6, sk_A.IsP(7) && DSA_ZR.Value);

        // --- Seite 4: Spur 8 ---
        if (sk_EH.IsP(8) && TZ.Value && ZV1.Value && !FV.Value)
            FV.Value = true;

        t = (!AS.Value || (A.Value && (!AH.Value || (!p_Frueh_Halt.Value && IS))));
        bool t2 = t && ZV1.Value && ZV2.Value;

        sk_E.SetMP(8, t2 && !FV.Value, sk_EH.IsP(8) && t2 && FV.va());

        t2 = t && !ZV1.Value && !ZV2.Value;
        DFU.Value = sk_E.IsAny(8) && t2;
        sk_A.SetIf(8, sk_E.Get(8), t2 && DFU.Value && DV.Value);

        t = !DV.Value && FU.Value && !BAH.Value;
        MR.Value = t && sk_A.IsM(8);
        MZ.Value = t && sk_A.IsP(8);
        FU_ZR.Value = sk_A.IsAny(8) && !DV.Value && (FU.Value || (RM1.Value && RM2.Value && !SF.Value && VP.Value && !MZ.Value));
        
        if (m_zv.sl_NH.Value && FU.Value && ST && MR.Value) FU_ZR.Value = false;
        sk_AH.SetP(8, MZ.Value);

        FU.Value = FU_ZR.Value && VFU.Value;
        MRZ_ZR.Value = MR.Value || MZ.Value;

        // --- Spur 9 ---
        sk_A.SetP(9, sk_E.IsP(9) && DV.Value);
        sk_E.SetP(9, sk_EH.IsP(9));
        SF.Value = sk_A.IsP(9) && FU.Value;
        sk_AH.SetP(9, FU.Value); 

        // --- Seite 5: Spur 10 ---
        t = m_zv.sl_AAUF.Value && (ZV1.Value || FV.Value || DV.Value);
        AS.Value = ((t && (IS || !A.Value || AH.Value) && (AS.Value || (!AH.Value && A.Value))) || (p_Rueckaufloesung.Value && sk_E.IsP(4) && !FV.Value)) && !s2_AS;
        if (sk_E.IsP(3) && !ZV1.Value && !VFU.Value && !NA.Value && !SV.Value && D.Value && !A.Value && AS.Value) AS.Value = false;
        sk_E.SetP(5, p_Rueckaufloesung.Value && AS.Value && !A.Value && S.Value);
        t = t && DV.Value && AS.Value;
        A.Value = ((t || sk_E.IsP(10)) && ZV1.Value) || (sk_A.IsP(10) && DV.Value); 
        sk_E.SetP(10, t);

        sk_A.SetP(10, sk_AH.IsP(10) || (m_zv.sl_AAUF.Value && FU.Value && !MZ.Value)); 
        sk_EH.SetP(10, m_zv.sl_AAUF.Value && FU.Value && (!FV.Value || AS.Value));

        // --- Spur 11 ---
        sk_E.SetP(11, AS.Value && ZV1.Value);
        sk_A.SetP(11, AS.Value && DV.Value);
        sk_EH.SetP(11, !FV.Value); 

        if (((AS.Value && DV.Value) || sk_A.IsP(11)) && VFU.Value) VFU.Value = false;
        if (FU_ZR.Value) VFU.Value = true; 

        AH.Value = (sk_E.IsP(11) && DV.Value) || (sk_A.IsP(11) && FU.Value && AS.Value);

        if (AS.Value && !A.Value) {
            ZV1.Value = false; ZV2.Value = false;
        }
        if (AS.Value && !A.Value && AH.Value) DV.Value = false;
        if (!ZV1.Value && FV.Value) FV.Value = false;

        // --- Spur 12 ---
        t = (m_zv.sl_BA.Value && ZT && !FV.Value) || NA.Value; 
        if (t) {
            ZV1.Value = false; ZV2.Value = false;
        }

        t = t && !BAD.Value && !DV.Value;
        sk_E.SetP(12, t && !ZV1.Value);

        t = sk_E.IsP(12) && !VFU.Value && !ZT && !NA.Value;
        if (t) DV.Value = false;
        BAD.Value = t;
        t = t && BAD.Value;
        sk_A.SetP(12, t);

        BAH.Value = t || sk_A.IsP(12);
        if ((t || sk_A.IsP(12)) && VFU.Value && BAH.Value)
            VFU.Value = false;

        // --- Seite 6: Spur 14 ---
        sk_AH.SetIf(14, sk_A.Get(14), TS.Value);
        sk_E.SetIf(14, sk_EH.Get(14), TZ.Value);
        sk_A.SetIf(14, sk_E.Get(14), D.Value);

        // --- Spur 15 ---
        sk_A.Set0(15); sk_AH.Set0(15);
        sk_E.Set0(15); sk_EH.Set0(15);

        if ((!RM1.Value || !RM2.Value) && MR.Value)
            SpurStecker.Conn(sk_A, sk_AH, 15);

        t = !FV.Value && !NA.Value && (!IS || v_15_SIU.Value) && v_15_GS.Value;

        if (t)
        {
            if (DFU.Value) SpurStecker.Conn(sk_E, sk_A, 15);
            else SpurStecker.Conn(sk_E, sk_EH, 15);
        }

        // --- Spur 16 - 20 ---
        if (!DV.Value) {
            sk_AH.Set(16, sk_A.Get(16));
            sk_AH.Set(17, sk_A.Get(17));
            sk_AH.SetP(18, sk_A.IsP(18));
            sk_AH.SetP(19, sk_A.IsP(19)); 
            sk_AH.SetP(20, sk_A.IsP(20));
        }

        if (FV.Value) {
            sk_E.Set(16, sk_EH.Get(16));
            sk_E.Set(17, sk_EH.Get(17));
        }

        if (ZV2.Value) {
            sk_E.SetP(18, sk_EH.IsP(18));
            sk_E.SetP(19, sk_EH.IsP(19)); 
            sk_E.SetP(20, sk_EH.IsP(20));
        }

        if (DFU.Value) {
            sk_A.Set(16, sk_E.Get(16));
            sk_A.Set(17, sk_E.Get(17));
            sk_A.SetP(18, sk_E.IsP(18));
            sk_A.SetP(19, sk_E.IsP(19)); 
            sk_A.SetP(20, sk_E.IsP(20));
        }

        // --- Spur 21 ---
        sk_A.SetP(21, sk_E.IsP(21) && BAD.Value);
        sk_E.SetP(21, sk_EH.IsP(21) && (FV.Value || !ZV1.Value) && NA.Value);
        sk_AH.SetP(21, sk_A.IsP(21) && !BAD.Value);
        NA.Value = sk_EH.IsP(21) && (FV.Value || !ZV1.Value);

        // --- Spur 23 ---
        bool o_23_E = sk_EH.IsP(23) || sk_E.IsP(23); // Falls du diese exportieren musst
        bool o_23_A = sk_A.IsP(23) || sk_AH.IsP(23);
        SpurStecker.Conn(sk_A, sk_AH, 23);
        SpurStecker.Conn(sk_E, sk_EH, 23);
    }

    public override void Output()
    {
        l_BEL.Value = !DSA_ZR.Value && !SV.Value && !DFU.Value && !FU.Value && !ZV1.Value;

        l_RM.Value = (m_zv.sl_ML.Value && MRZ_ZR.Value && !RM1.Value && !RM2.Value) || (m_zv.sl_BLI.Value && !VP.Value && (RM1.Value || RM2.Value));
        l_ws.Value = m_zv.sl_ML.Value && (DV.Value || ZV1.Value) && !i_IS.Value;
        l_rt.Value = m_zv.sl_ML.Value && i_IS.Value;
        l_ni.Value = m_zv.sl_ML.Value && (DV.Value || ZV1.Value);
        l_ST.Value = m_zv.sl_BLI.Value && S.Value;
        l_ZT.Value = m_zv.sl_BLI.Value && Z.Value;
        l_SZT.Value = m_zv.sl_BLI.Value && (S.Value || Z.Value);
    }

    public override bool UpdateWire()
    {
        sk_A.Commit();
        sk_E.Commit();
        sk_AH.Commit();
        sk_EH.Commit();

        return sk_A.HasChanged() || sk_E.HasChanged() || sk_AH.HasChanged() || sk_EH.HasChanged();
    }

    public void Tp(ref bool l1, ref bool l2, ref bool l3, bool y)
    {
        // Greift auf die lokale Tasten-Variable zurück (die in Update berechnet wird). 
        // Falls ST global sein soll, kann sie oben als public bool ST abgelegt werden.
        bool ST = (m_zv.sl_TR_ZS.Value && (t_ST.Value || t_SZT.Value)) || (m_zv.sl_TRFU_ZS.Value && (tfu_ST.Value || tfu_SZT.Value));
        
        if (ST && (y == p_Links.Value)) {
            l3 = l2;
            l2 = l1;
            l1 = false;
        }
    }
}
