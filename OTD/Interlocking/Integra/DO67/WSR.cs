using System;

/// <summary>
/// Vereinfachter Weichen-Relaissatz mit relaisbasierter Fahrwegwahl,
/// Weichenstellung, Flankenschutzueberwachung und Aufloesung.
/// </summary>
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
        sk_S.Reset();
        sk_R.Reset();
        sk_L.Reset();

        UpdateBedienungUndAntrieb();
        UpdateSpeicherUndFahrwegwahl();
        UpdateVerschlussUndFlankenschutz();
        UpdateFahrwegueberwachung();
        UpdateAufloesung();
        UpdateSpurplan();
    }

    private void UpdateBedienungUndAntrieb()
    {
        WT.Value = (m_zv.sl_TR_W.Value && t_WT.Value) || tfu_WT.Value;
        if (m_zv.sl_EV_E.Value && WT.Value)
            EV.Value = true;
        if (m_zv.sl_EV_A.Value && WT.Value)
            EV.Value = false;

        IS.Value = i_IS.Value;
        var rechtsGemeldet = m_antrieb?.elk_r() ?? !L1.Value;
        var linksGemeldet = m_antrieb?.elk_l() ?? L1.Value;
        U.Value = !rechtsGemeldet && !linksGemeldet;
        AM.Value = rechtsGemeldet && linksGemeldet;

        var darfStellen = WT.Value && !WV.Value && !SV.Value && !EV.Value && !IS.Value;
        S1.Value = darfStellen && ((SAL.Value && !linksGemeldet) || (SAR.Value && !rechtsGemeldet));
        S2.Value = S1.Value;
        S2_ZR.Value = S2.Value;

        if (S2.Value && SAL.Value)
            L1.Value = true;
        if (S2.Value && SAR.Value)
            L1.Value = false;
        L2.Value = L1.Value;
        L3.Value = L1.Value;
        L4.Value = L1.Value;
        AA.Value = (AA.Value || m_zv.sl_AA_FB.Value) && U.Value;
    }

    private void UpdateSpeicherUndFahrwegwahl()
    {
        var geradePin = Do67Spurplan.SpeicherPin(Do67Spurplan.Speicher1, p_Spitze_Rechts.Value);
        var abzweigPin = Do67Spurplan.SpeicherPin(Do67Spurplan.Speicher2, p_Spitze_Rechts.Value);
        var rechtsAnforderung = sk_R.IsP(geradePin) || sk_R.IsP(abzweigPin);
        var linksAnforderung = sk_L.IsP(geradePin) || sk_L.IsP(abzweigPin);

        FAR.Value = (FAR.Value || rechtsAnforderung) && !FAL.Value;
        FAL.Value = (FAL.Value || linksAnforderung) && !FAR.Value;
        SAR.Value = FAR.Value && !L1.Value;
        SAL.Value = FAL.Value && L1.Value;

        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, geradePin, !FAL.Value, !FAR.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, abzweigPin, !FAL.Value, !FAR.Value);
        o_Sp_S_S.Value = sk_S.IsP(geradePin) || FAR.Value || FAL.Value;
        o_Sp_S_Q.Value = sk_S.IsP(abzweigPin) || FAR.Value || FAL.Value;
    }

    private void UpdateVerschlussUndFlankenschutz()
    {
        var angefordert = FAR.Value || FAL.Value;
        var verschluss = sk_S.IsP(Do67Spurplan.Verschluss)
                         || sk_R.IsP(Do67Spurplan.Verschluss)
                         || sk_L.IsP(Do67Spurplan.Verschluss);
        WV.Value = (WV.Value || (angefordert && verschluss)) && !AS.Value && !ARL.Value;
        DSU.Value = sk_S.IsP(7) && !IS.Value;
        SUR.Value = sk_R.IsP(7);
        SUL.Value = sk_L.IsP(7);
        SV.Value = Do67Spurplan.HatKonflikt(sk_R, sk_L);
        DSV.Value = angefordert && SV.Value;

        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, Do67Spurplan.Verschluss, WV.Value && FAR.Value, WV.Value && FAL.Value);
        for (var spur = Do67Spurplan.FlankenschutzVon; spur <= Do67Spurplan.FlankenschutzBis; spur++)
            SpurStecker.ConnIfW(sk_S, sk_R, sk_L, spur, angefordert && FAR.Value, angefordert && FAL.Value);
    }

    private void UpdateFahrwegueberwachung()
    {
        FUS.Value = WV.Value && sk_S.IsAny(Do67Spurplan.Fahrwegueberwachung);
        FURL.Value = WV.Value && (sk_R.IsAny(Do67Spurplan.Fahrwegueberwachung) || sk_L.IsAny(Do67Spurplan.Fahrwegueberwachung));
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, Do67Spurplan.Fahrwegueberwachung, FUS.Value && FAR.Value, FUS.Value && FAL.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, Do67Spurplan.SenkrechterFahrbegriff, FAR.Value, FAL.Value);
    }

    private void UpdateAufloesung()
    {
        var aufloesen = m_zv.sl_AAUF.Value
                        || m_zv.sl_BA.Value
                        || sk_S.IsP(Do67Spurplan.Betriebsaufloesung)
                        || sk_R.IsP(Do67Spurplan.Betriebsaufloesung)
                        || sk_L.IsP(Do67Spurplan.Betriebsaufloesung)
                        || sk_S.IsP(Do67Spurplan.Aufloesung1)
                        || sk_R.IsP(Do67Spurplan.Aufloesung1)
                        || sk_L.IsP(Do67Spurplan.Aufloesung1)
                        || sk_S.IsP(Do67Spurplan.Aufloesung2)
                        || sk_R.IsP(Do67Spurplan.Aufloesung2)
                        || sk_L.IsP(Do67Spurplan.Aufloesung2);

        A.Value = aufloesen && WV.Value;
        AS.Value = aufloesen && FAL.Value;
        ARL.Value = aufloesen && FAR.Value;
        AH.Value = aufloesen && !IS.Value;

        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, Do67Spurplan.Aufloesung1, aufloesen && FAR.Value, aufloesen && FAL.Value);
        SpurStecker.ConnIfW(sk_S, sk_R, sk_L, Do67Spurplan.Aufloesung2, aufloesen && FAR.Value, aufloesen && FAL.Value);
        if (!aufloesen)
            return;

        WV.Value = false;
        FAR.Value = false;
        FAL.Value = false;
        FUS.Value = false;
        FURL.Value = false;
    }

    private void UpdateSpurplan()
    {
        var festgelegt = WV.Value && v_15_GS_S.Value;
        SpurStecker.ConnIfW(
            sk_S,
            sk_R,
            sk_L,
            Do67Spurplan.FestlegungZugfahrstrasse,
            festgelegt && FAR.Value && v_15_GS_R.Value,
            festgelegt && FAL.Value && v_15_GS_L.Value);

        int[] belegt = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 15 };
        for (var spur = 1; spur <= Do67Spurplan.SpurAnzahl; spur++)
        {
            if (Array.IndexOf(belegt, spur) < 0)
                SpurStecker.ConnIfW(sk_S, sk_R, sk_L, spur, FAR.Value, FAL.Value);
        }

        o_23.Value = sk_S.IsP(23) || sk_R.IsP(23) || sk_L.IsP(23);
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
