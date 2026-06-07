using System;

public class TMN500_WSR : RelaisSatz
{
    public Lampe l_WV = new();
    public Lampe l_zl_ws = new();
    public Lampe l_zl_rt = new();
    public Lampe l_zr_ws = new();
    public Lampe l_zr_rt = new();
    public Lampe l_l_ws = new();
    public Lampe l_l_rt = new();
    public Lampe l_r_ws = new();
    public Lampe l_r_rt = new();
    public Lampe l_s_ws = new();
    public Lampe l_s_rt = new();

    public Taste t_WT = new();
    public Input tfu_WT = new();
    public Input i_IS = new();

    public SpurStecker sk_S = new SpurStecker();
    public SpurStecker sk_R = new SpurStecker();
    public SpurStecker sk_L = new SpurStecker();

    public Verbindung v_Sp_FAR = new();
    public Verbindung v_Sp_FAL = new();

    public Verbindung v_Sp_S_S = new();
    public Verbindung v_Sp_S_Q = new();
    public Verbindung v_Sp_R_S = new();
    public Verbindung v_Sp_R_Q = new();
    public Verbindung v_Sp_L_S = new();
    public Verbindung v_Sp_L_Q = new();

    public Output o_Sp_S_S = new();
    public Output o_Sp_S_Q = new();

    public Verbindung v_15_SIU = new();
    public Verbindung v_15_GS_S = new();
    public Verbindung v_15_GS_R = new();
    public Verbindung v_15_GS_L = new();

    public Output o_23 = new();

    public Relais AH = new();
    public Relais ARL = new();
    public Relais SV = new();
    public Relais SUR = new();
    public RelaisInv IS = new();
    public Relais A = new();
    public Relais AS = new();
    public Relais SUL = new();

    public Relais SAR = new();
    public RelaisInv U = new();
    public Relais AM = new();
    public Relais L1 = new();
    public Relais S1 = new();
    public Relais S2 = new();
    public Relais SAL = new();
    public Relais EV = new();
    public Relais WT = new();

    public Relais L3 = new();
    public Relais L2 = new();
    public Relais DSV = new();
    public Relais WV = new();
    public Relais FAR = new();
    public Relais FURL = new();
    public Relais DSU = new();
    public Relais FAL = new();
    public Relais FUS = new();

    public Relais S2_ZR = new();
    public Relais L4 = new();
    public Relais AA = new();

    private readonly Flachrelais FUS_ZR = new();
    private readonly Flachrelais FUL_ZR = new();
    private readonly Flachrelais FUR_ZR = new();

    private readonly Lampe l_BEL = new();
    private readonly Kondensator k_S2_ZR = new();

    private readonly Verbindung p_Spitze_Rechts = new();
    private readonly Verbindung p_R_18 = new();
    private readonly Verbindung p_R_19 = new();
    private readonly Verbindung p_R_20 = new();
    private readonly Verbindung p_L_18 = new();
    private readonly Verbindung p_L_19 = new();
    private readonly Verbindung p_L_20 = new();

    private Weichenantrieb? m_antrieb;
    private readonly Do67 m_zv;

    public TMN500_WSR(Do67 z, bool spitze_rechts)
    {
        m_zv = z;
        m_zv.Add(this);

        // ENABLE_DUMMY_WEICHE logic would go here if needed

        k_S2_ZR.Init(200, 175, 0);

        p_L_18.Value = true;
        p_L_19.Value = true;
        p_L_20.Value = true;
        p_R_18.Value = true;
        p_R_19.Value = true;
        p_R_20.Value = true;
        l_BEL.Value = false;

        p_Spitze_Rechts.Value = spitze_rechts;

        // INIT_IS_FREI logic
        i_IS.Value = true;

        v_Sp_FAR.Value = true;
        v_Sp_FAL.Value = true;

        v_Sp_S_S.Value = true;
        v_Sp_S_Q.Value = true;
        v_Sp_R_S.Value = true;
        v_Sp_R_Q.Value = true;
        v_Sp_L_S.Value = true;
        v_Sp_L_Q.Value = true;

        v_15_GS_S.Value = true;
        v_15_GS_R.Value = true;
        v_15_GS_L.Value = true;
    }

    public void SetWeiche(Weichenantrieb w) => m_antrieb = w;

    public void InitBegriff(int r, int l)
    {
        p_R_18.Value = (r >= 1);
        p_R_19.Value = (r >= 2);
        p_R_20.Value = (r >= 3);
        p_L_18.Value = (l >= 1);
        p_L_19.Value = (l >= 2);
        p_L_20.Value = (l >= 3);
    }

    public override void Update()
    {
        bool t;
        bool tl;
        bool tr;

        sk_S.Reset();
        sk_R.Reset();
        sk_L.Reset();

        // Seite 1
        AA.Value = (m_zv.sl_AA_FB.Value || AA.Value) && U.Value;

        t = !S2.Value && U.Value && AM.Value; // Evtl. noch v_WU

        if (t && S1.Value && SAL.Value) L1.Value = true;
        if (t && S1.Value && SAR.Value) L1.Value = false;
        
        L2.Value = L1.Value;
        L3.Value = L1.Value;
        L4.Value = L1.Value;

        WT.Value = (m_zv.sl_TR_W.Value && t_WT.Value) || tfu_WT.Value;

        if (m_zv.sl_EV_E.Value && WT.Value) EV.Value = true;
        if (m_zv.sl_EV_A.Value && WT.Value) EV.Value = false;

        // Seite 2
        if (m_antrieb != null)
            AM.Value = (U.Value && AM.Value && !S2.Value) || (S1.Value && !S2.Value) || (!S1.Value && !S2.Value && m_antrieb.elk_nr() && m_antrieb.elk_nl());
        else
            AM.Value = (U.Value && AM.Value && !S2.Value) || (S1.Value && !S2.Value);

        t = !EV.Value && !SV.Value && !WV.Value && ((!WT.Value && !AM.Value && !IS.Value) || (WT.Value && ((AM.Value && m_zv.sl_AM_GT.Value) || ((!AM.Value || S1.Value) && ((!IS.Value && m_zv.sl_ST_GT.Value) || (IS.Value && m_zv.sl_WIU_GT.Value))))));

        bool s1_SAR = t && WT.Value && (L1.Value || SAR.Value) && !SAL.Value;
        bool s1_SAL = t && WT.Value && (!L1.Value || SAL.Value) && !SAR.Value;

        bool t_S1 = t && ((SAL.Value && !L1.Value) || (SAR.Value && L1.Value));
        if (t_S1 && k_S2_ZR.Value) S1.Value = true; 

        k_S2_ZR.Laden(25, t_S1 || (t && WT.Value && ((SAR.Value && !L1.Value && !L2.Value && !L3.Value) || (SAL.Value && L1.Value && L2.Value && L3.Value)) && S1.Value));

        if (((!SAL.Value && !L1.Value && !L2.Value && !L3.Value) || (!SAR.Value && L1.Value && L2.Value && L3.Value)) && S1.Value) {
            S2_ZR.Value = k_S2_ZR.Value;
            k_S2_ZR.Entladen(1, true);
        }
        else {
            S2_ZR.Value = false;
        }

        S2.Value = S2_ZR.Value;

        if (m_antrieb != null) {
            U.Value = !S2.Value && !S1.Value && !AM.Value && ((L1.Value && m_antrieb.elk_l()) || (!L1.Value && m_antrieb.elk_r()));
            if (((!L1.Value && m_antrieb.elk_r()) || (L1.Value && m_antrieb.elk_l())) && !AM.Value && S2.Value)
                S1.Value = false;
        }
        else {
            U.Value = false;
        }

        // Seite 3 - Spur 1/2
        {
            bool s1e, r1e, l1e, s2e, r2e, l2e;
            bool s1a = false, r1a = false, l1a = false, s2a = false, r2a = false, l2a = false;

            if (!p_Spitze_Rechts.Value)
            {
                s1e = sk_S.IsP(1) && v_Sp_S_S.Value;
                r1e = sk_R.IsP(1) && v_Sp_R_S.Value;
                l1e = sk_L.IsP(1) && v_Sp_L_S.Value;
                s2e = sk_S.IsP(2) && v_Sp_S_Q.Value;
                r2e = sk_R.IsP(2) && v_Sp_R_Q.Value;
                l2e = sk_L.IsP(2) && v_Sp_L_Q.Value;
            }
            else
            {
                s1e = sk_S.IsP(2) && v_Sp_S_S.Value;
                r1e = sk_R.IsP(2) && v_Sp_R_S.Value;
                l1e = sk_L.IsP(2) && v_Sp_L_S.Value;
                s2e = sk_S.IsP(1) && v_Sp_S_Q.Value;
                r2e = sk_R.IsP(1) && v_Sp_R_Q.Value;
                l2e = sk_L.IsP(1) && v_Sp_L_Q.Value;
            }

            FAR.Value = ((s1e && FAR.Value) || r1e) && (FAR.Value || (v_Sp_FAR.Value && (WV.Value || (!FURL.Value && !FUS.Value)))) && !FAL.Value;
            FAL.Value = ((s1e && FAL.Value) || l1e) && (FAL.Value || (v_Sp_FAL.Value && (WV.Value || (!FURL.Value && !FUS.Value)))) && !FAR.Value;

            s2a = (l2e && !FAR.Value) || (r2e && !FAL.Value);
            l2a = s2e && !FAR.Value;
            r2a = s2e && !FAL.Value;
            s1a = (l1e && FAL.Value) || (r1e && FAR.Value);
            r1a = s1e && FAR.Value;
            l1a = s1e && FAL.Value;

            o_Sp_S_S.Value = s1a || s1e;

            if (!p_Spitze_Rechts.Value)
            {
                sk_S.SetP(1, s1a && v_Sp_S_S.Value);
                sk_R.SetP(1, r1a && v_Sp_R_S.Value);
                sk_L.SetP(1, l1a && v_Sp_L_S.Value);
                sk_S.SetP(2, s2a && v_Sp_S_Q.Value);
                sk_R.SetP(2, r2a && v_Sp_R_Q.Value);
                sk_L.SetP(2, l2a && v_Sp_L_Q.Value);
                o_Sp_S_Q.Value = sk_S.IsP(2) || (s2a && v_Sp_S_Q.Value);
            }
            else
            {
                sk_S.SetP(1, s2a && v_Sp_S_S.Value);
                sk_R.SetP(1, r2a && v_Sp_R_S.Value);
                sk_L.SetP(1, l2a && v_Sp_L_S.Value);
                sk_S.SetP(2, s1a && v_Sp_S_Q.Value);
                sk_R.SetP(2, r1a && v_Sp_R_Q.Value);
                sk_L.SetP(2, l1a && v_Sp_L_Q.Value);
                o_Sp_S_Q.Value = sk_S.IsP(1) || (s2a && v_Sp_S_Q.Value);
            }
        }

        // Spur 3
        tr = FAR.Value && !L2.Value && SAR.Value && !DSU.Value;
        tl = FAL.Value && L2.Value && SAL.Value && !DSU.Value;

        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 3, tr, tl);

        if (((sk_S.IsP(3) && !DSU.Value && ((FAL.Value && L3.Value) || (FAR.Value && !L3.Value))) || 
             (sk_L.IsP(3) && !DSU.Value && FAL.Value && SAL.Value && L2.Value) || 
             (sk_R.IsP(3) && !DSU.Value && FAR.Value && SAR.Value && !L2.Value)) && 
             !U.Value && !SUR.Value && !SUL.Value) WV.Value = true;

        // Spur 4 / 5
        SAR.Value = s1_SAR || (sk_S.IsP(3) && !DSU.Value && FAR.Value && !FURL.Value) || (sk_R.IsP(3) && !DSU.Value && FAR.Value && !FUS.Value) || (sk_L.IsP(5) && !SAL.Value && (L2.Value || (!SV.Value && SAR.Value)) && (!SV.Value || (!S1.Value && DSU.Value)));
        SAL.Value = s1_SAL || (sk_S.IsP(3) && !DSU.Value && FAL.Value && !FURL.Value) || (sk_L.IsP(3) && !DSU.Value && FAL.Value && !FUS.Value) || (sk_R.IsP(5) && !SAR.Value && (!L2.Value || (!SV.Value && SAL.Value)) && (!SV.Value || (!S1.Value && DSU.Value)));

        // Seite 4 - Spur 4 / 5
        t = DSU.Value || (!IS.Value && SV.Value);
        sk_S.SetMP(4, t && ((!SAR.Value && !L2.Value && sk_R.IsM(5)) || (!SAL.Value && L2.Value && sk_R.IsM(5))), t && ((!SAR.Value && ((SAL.Value && !SV.Value) || !L2.Value) && sk_R.IsP(5)) || (!SAL.Value && ((SAR.Value && !SV.Value) || L2.Value) && sk_L.IsP(5))));

        sk_R.SetMP(4, !DSV.Value && !WV.Value, (WV.Value && L1.Value) || (DSV.Value && !IS.Value));
        sk_L.SetMP(4, !DSV.Value && !WV.Value, (WV.Value && !L1.Value) || (DSV.Value && !IS.Value));

        if (sk_S.IsM(5)) DSV.Value = false;
        if (sk_S.IsP(5)) DSV.Value = true;

        if (((sk_L.IsP(5) && !L2.Value) || (sk_R.IsP(5) && L2.Value)) && !U.Value && !S1.Value) SV.Value = true;
        if ((sk_L.IsM(5) && !L2.Value) || (sk_R.IsM(5) && L2.Value)) SV.Value = false;

        // Spur 6 / 7
        sk_R.SetP(6, DSU.Value || (L1.Value && !U.Value && SV.Value));
        sk_L.SetP(6, DSU.Value || (!L1.Value && !U.Value && SV.Value));
        sk_S.SetP(6, !IS.Value && SUL.Value && SUR.Value);

        DSU.Value = sk_S.IsP(7) && !IS.Value;
        SUR.Value = sk_R.IsP(7);
        SUL.Value = sk_L.IsP(7);

        // Seite 5 - Spur 8
        FUS_ZR.Value = sk_S.IsAny(8) && !FURL.Value;
        FUR_ZR.Value = sk_R.IsAny(8) && !FUS.Value;
        FUL_ZR.Value = sk_L.IsAny(8) && !FUS.Value;

        if (FUS_ZR.Value) FUS.Value = true;
        if (FUL_ZR.Value || FUR_ZR.Value) FURL.Value = true;

        t = WV.Value && !U.Value && (FUS.Value || FURL.Value) && ((!ARL.Value && !AS.Value) || ((!AH.Value || IS.Value) && A.Value));
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 8, t && SUL.Value, t && SUR.Value && !DSV.Value);

        // Spur 9
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 9, SUL.Value, SUR.Value);

        // Spur 10
        A.Value = (!ARL.Value && sk_S.IsP(10)) || (!AS.Value && ((!L3.Value && sk_R.IsP(10)) || (L3.Value && sk_L.IsP(10))));
        t = WV.Value && (A.Value || ((!FAL.Value || !L3.Value || !SAL.Value) && (!FAR.Value || L3.Value || !SAR.Value))) && m_zv.sl_AAUF.Value;

        sk_S.SetP(10, t && !AS.Value && ARL.Value);
        sk_R.SetP(10, t && AS.Value && !ARL.Value && !L3.Value);
        sk_L.SetP(10, t && AS.Value && !ARL.Value && L3.Value);

        t = t || (sk_S.IsP(10) && ARL.Value && !AS.Value) || (AS.Value && !ARL.Value && ((L3.Value && sk_L.IsP(10)) || (!L3.Value && sk_R.IsP(10))));

        AS.Value = (t && (AH.Value || IS.Value) && (AS.Value || (!AH.Value && A.Value && FURL.Value))) || (sk_S.IsP(11) && ARL.Value && !A.Value);
        ARL.Value = (t && (AH.Value || IS.Value) && (ARL.Value || (!AH.Value && A.Value && FUS.Value))) || (((sk_L.IsP(11) && L3.Value) || (sk_R.IsP(11) && !L3.Value)) && AS.Value && !A.Value);

        t = t && AS.Value && ARL.Value && AH.Value;

        if (t && !IS.Value) WV.Value = false;
        if (t || !WV.Value) {
            FUS.Value = false;
            FURL.Value = false;
        }

        IS.Value = i_IS.Value;

        // Seite 6 - Spur 11
        AH.Value = t || ((sk_S.IsP(11) && ARL.Value) || (((sk_R.IsP(11) && !L3.Value) || (sk_L.IsP(11) && L3.Value)) && AS.Value));

        sk_S.SetP(11, !ARL.Value && AS.Value);
        sk_R.SetP(11, !AS.Value && ARL.Value && !L3.Value);
        sk_L.SetP(11, !AS.Value && ARL.Value && L3.Value);

        // Spur 12
        if (sk_S.IsP(12) && !FURL.Value) WV.Value = false;
        if (sk_R.IsP(12) && !L3.Value && !FUS.Value) WV.Value = false;
        if (sk_L.IsP(12) && L3.Value && !FUS.Value) WV.Value = false;

        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 12, !FURL.Value && !FUS.Value && !L3.Value, !FURL.Value && !FUS.Value && L3.Value);

        // Spur 14
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 14, FAR.Value, FAL.Value); 

        // Spur 15
        t = (!IS.Value || v_15_SIU.Value) && v_15_GS_S.Value;
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 15, SUL.Value && v_15_GS_R.Value && t, SUR.Value && v_15_GS_L.Value && t);

        // Spuren 16 - 23
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 16, SUL.Value, SUR.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 17, SUL.Value, SUR.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 18, !L3.Value && p_R_18.Value, L3.Value && p_L_18.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 19, !L2.Value && p_R_19.Value, L2.Value && p_L_19.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 20, !L2.Value && p_R_20.Value, L2.Value && p_L_20.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 21, !L2.Value, L2.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 22, !L2.Value, L2.Value);

        o_23.Value = sk_S.IsP(23) || (sk_R.IsP(23) && SUL.Value) || (sk_L.IsP(23) && SUR.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, 23, SUL.Value, SUR.Value);
    }

    public override void Output()
    {
        if (m_antrieb != null)
        {
            Stellstrom st = Stellstrom.Aus;

            if (S2.Value)
            {
                if (L1.Value) st = Stellstrom.Links;
                if (!L1.Value) st = Stellstrom.Rechts;
            }

            m_antrieb.SetStrom(st);
            m_antrieb.Commit();
        }

        l_BEL.Value = !WV.Value && !ARL.Value && !SV.Value && !SUL.Value && !SUR.Value && !EV.Value && !S2.Value && !DSV.Value;
        l_WV.Value = m_zv.sl_ML.Value && (SV.Value || EV.Value || WV.Value);

        bool t = (((m_zv.sl_ML.Value && (EV.Value || WV.Value || IS.Value)) || (m_zv.sl_WA.Value && !IS.Value && !EV.Value && !WV.Value)) && !U.Value) || (m_zv.sl_BLI.Value && U.Value);
        bool tl = t && (L4.Value || (!L4.Value && !S1.Value && AM.Value));
        bool tr = t && (!L4.Value || (L4.Value && !S1.Value && AM.Value));

        l_zr_ws.Value = tr && !IS.Value;
        l_zr_rt.Value = tr && IS.Value;
        l_zl_ws.Value = tl && !IS.Value;
        l_zl_rt.Value = tl && IS.Value;

        l_s_ws.Value = m_zv.sl_ML.Value && WV.Value && !IS.Value;
        l_s_rt.Value = m_zv.sl_ML.Value && IS.Value;
        l_l_ws.Value = m_zv.sl_ML.Value && L4.Value && !IS.Value;
        l_l_rt.Value = m_zv.sl_ML.Value && (L4.Value && IS.Value);
        l_r_ws.Value = m_zv.sl_ML.Value && !L4.Value && !IS.Value;
        l_r_rt.Value = m_zv.sl_ML.Value && (!L4.Value && IS.Value);
    }

    public override bool UpdateWire()
    {
        sk_S.Commit();
        sk_R.Commit();
        sk_L.Commit();

        return sk_S.HasChanged() || sk_R.HasChanged() || sk_L.HasChanged();
    }

    public void Wtp(ref bool ev, ref bool l1, ref bool l2)
    {
        ev = ev && !EV.Value;
        if (WT.Value) {
            l2 = l1;
            l1 = false;
        }
    }

    public bool Zv_Wsr() => !U.Value || S2.Value || AA.Value;
    public bool Zv_Am1() => AM.Value && !S1.Value;
    public bool Zv_Am2() => S2.Value || !S1.Value || S1.Value || !WT.Value;
}
