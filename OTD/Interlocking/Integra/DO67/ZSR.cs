using System;

/// <summary>
/// Zwergsignal-Satz mit vollständigem 24-Spur-Plan.
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
    public Relais BL = new Relais();     public Relais TR = new Relais();

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
    private Zwergsignal? m_zsig;

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
        sk_A.Reset();
        sk_AH.Reset();
        sk_EH.Reset();

        var st = (m_zv.sl_TR_ZS.Value && t_ST.Value) || (m_zv.sl_TRFU_ZS.Value && tfu_ST.Value);
        var zt = (m_zv.sl_TR_ZS.Value && t_ZT.Value) || (m_zv.sl_TRFU_ZS.Value && tfu_ZT.Value);

        UpdateSignalRueckmeldung();
        UpdateSpeicher(st, zt);
        UpdateVerschlussUndFlankenschutz();
        UpdateFahrweg(st);
        UpdateAufloesung(zt);
        UpdateSpurplan();
    }

    private void UpdateSignalRueckmeldung()
    {
        if (m_zsig == null)
        {
            RM1.Value = false;
            RM2.Value = false;
            return;
        }

        m_zsig.L1 = !SF.Value;
        m_zsig.L2 = SF.Value || !FU.Value;
        m_zsig.L3 = FU.Value && (MR.Value || MZ.Value);
        RM1.Value = m_zsig.L1 || m_zsig.L2;
        RM2.Value = m_zsig.L2 || m_zsig.L3;
    }

    private void UpdateSpeicher(bool st, bool zt)
    {
        var pin1 = Do67Spurplan.SpeicherPin(Do67Spurplan.Speicher1, p_Links.Value);
        var pin2 = Do67Spurplan.SpeicherPin(Do67Spurplan.Speicher2, p_Links.Value);
        var vonStart1 = sk_A.IsP(pin1) || sk_AH.IsP(pin1);
        var vonZiel1 = sk_EH.IsP(pin1);
        var vonStart2 = sk_A.IsP(pin2) || sk_AH.IsP(pin2);
        var vonZiel2 = sk_EH.IsP(pin2);
        var speicher1 = vonStart1 || vonZiel1;
        var speicher2 = vonStart2 || vonZiel2;

        TS.Value = (TS.Value || sk_AH.IsP(pin1)) && !TZ.Value && !Z.Value;
        TZ.Value = (TZ.Value || sk_EH.IsP(pin1)) && !TS.Value && !S.Value;
        S.Value = (S.Value || (st && (speicher1 || speicher2))) && !Z.Value;
        Z.Value = (Z.Value || (zt && v_1_Z.Value && (speicher1 || speicher2))) && !S.Value;
        BL.Value = speicher1 && speicher2;
        TR.Value = BL.Value && (sk_AH.IsAny(Do67Spurplan.FestlegungZugfahrstrasse)
                                || sk_EH.IsAny(Do67Spurplan.FestlegungZugfahrstrasse));
        if (m_zv.sl_SPL.Value && zt)
            Z.Value = false;

        sk_A.SetP(pin1, S.Value || Z.Value || vonZiel1);
        sk_A.SetP(pin2, S.Value || Z.Value || vonZiel2);
        sk_AH.SetP(pin1, TS.Value || S.Value);
        sk_EH.SetP(pin1, TZ.Value || Z.Value);
    }

    private void UpdateVerschlussUndFlankenschutz()
    {
        var angefordert = S.Value || Z.Value || TS.Value || TZ.Value;
        SV.Value = Do67Spurplan.HatKonflikt(sk_AH, sk_EH);
        VP.Value = angefordert
                   && (sk_AH.IsP(Do67Spurplan.Verschluss) || sk_EH.IsP(Do67Spurplan.Verschluss) || m_zv.sl_VP.Value)
                   && !SV.Value;
        ZV.Value = VP.Value;

        sk_A.SetP(Do67Spurplan.Verschluss, VP.Value);
        sk_AH.SetP(Do67Spurplan.Verschluss, VP.Value && TS.Value);
        sk_EH.SetP(Do67Spurplan.Verschluss, VP.Value && TZ.Value);
        for (var spur = Do67Spurplan.FlankenschutzVon; spur <= Do67Spurplan.FlankenschutzBis; spur++)
            SpurStecker.ConnIfW(sk_A, sk_AH, sk_EH, spur, angefordert && TS.Value, angefordert && TZ.Value);
    }

    private void UpdateFahrweg(bool st)
    {
        var ueberwachung = sk_AH.Get(Do67Spurplan.Fahrwegueberwachung);
        if (ueberwachung == 0)
            ueberwachung = sk_EH.Get(Do67Spurplan.Fahrwegueberwachung);

        FU.Value = ZV.Value && ueberwachung != 0 && !SV.Value;
        VFU.Value = FU.Value;
        MR.Value = FU.Value && ueberwachung < 0;
        MZ.Value = FU.Value && ueberwachung > 0;
        MRZ.Value = MR.Value || MZ.Value;
        FV.Value = ZV.Value && !FU.Value;
        SF.Value = FU.Value && (sk_AH.IsP(Do67Spurplan.SenkrechterFahrbegriff)
                                || sk_EH.IsP(Do67Spurplan.SenkrechterFahrbegriff));
        if (m_zv.sl_NH.Value && st)
            SF.Value = false;

        sk_A.Set(Do67Spurplan.Fahrwegueberwachung, ueberwachung);
        sk_A.SetP(Do67Spurplan.SenkrechterFahrbegriff, SF.Value);
    }

    private void UpdateAufloesung(bool zt)
    {
        var aufloesen = m_zv.sl_AAUF.Value
                        || sk_AH.IsP(Do67Spurplan.Aufloesung1)
                        || sk_EH.IsP(Do67Spurplan.Aufloesung1)
                        || sk_AH.IsP(Do67Spurplan.Aufloesung2)
                        || sk_EH.IsP(Do67Spurplan.Aufloesung2)
                        || sk_AH.IsP(Do67Spurplan.Betriebsaufloesung)
                        || sk_EH.IsP(Do67Spurplan.Betriebsaufloesung)
                        || m_zv.sl_BA.Value;

        A.Value = aufloesen && ZV.Value;
        AS.Value = aufloesen;
        SH.Value = aufloesen && i_IS2.Value;
        ZE.Value = aufloesen && i_IS1.Value && i_IS2.Value;
        BAH.Value = m_zv.sl_BA.Value;

        sk_A.SetP(Do67Spurplan.Aufloesung1, aufloesen);
        sk_A.SetP(Do67Spurplan.Aufloesung2, aufloesen);
        if (!aufloesen)
            return;

        S.Value = false;
        Z.Value = false;
        TS.Value = false;
        TZ.Value = false;
        VP.Value = false;
        ZV.Value = false;
        FU.Value = false;
        FV.Value = false;
        SF.Value = false;
    }

    private void UpdateSpurplan()
    {
        var festgelegt = Z.Value && ZV.Value && FU.Value && !i_IS1.Value && !i_IS2.Value;
        SpurStecker.ConnIfW(sk_A, sk_AH, sk_EH, Do67Spurplan.FestlegungZugfahrstrasse, TS.Value && festgelegt, TZ.Value && festgelegt);

        int[] belegt = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 15 };
        for (var spur = 1; spur <= Do67Spurplan.SpurAnzahl; spur++)
        {
            if (Array.IndexOf(belegt, spur) < 0)
                SpurStecker.ConnIfW(sk_A, sk_AH, sk_EH, spur, TS.Value, TZ.Value);
        }
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
        l_ST.Value = m_zv.sl_BLI.Value && (S.Value || (BL.Value && Global.g_Blinker));
        l_ZT.Value = m_zv.sl_BLI.Value && (Z.Value || (BL.Value && Global.g_Blinker));
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
