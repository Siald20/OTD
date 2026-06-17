using System;

/// <summary>
/// Vereinfachter Hauptsignal-Relaissatz mit relaisbasierten Speicher-, Verschluss-,
/// Fahrwegueberwachungs-, Fahrbegriffs- und Aufloesungsstufen.
/// </summary>
public class TMN501_HSR : RelaisSatz
{
    public enum FBgr
    {
        Immer,
        F_RM,
        FF,
        F2,
        F3,
        F5,
        F1
    }

    public Lampe l_H_RM = new Lampe();
    public Lampe l_NH_RM = new Lampe();
    public Lampe l_F_RM = new Lampe();
    public Lampe l_ST = new Lampe();
    public Lampe l_ZT = new Lampe();
    public Lampe l_SZT = new Lampe();
    public Lampe l_BesE = new Lampe();
    public Lampe l_BEL = new Lampe();

    public Taste t_ST = new Taste();
    public Taste t_ZT = new Taste();
    public Taste t_SZT = new Taste();
    
    public Input tfu_ST = new Input();
    public Input tfu_ZT = new Input();
    public Input tfu_SZT = new Input();
    public Input i_BS = new Input();

    public SpurStecker sk_A = new SpurStecker();
    public SpurStecker sk_E = new SpurStecker();

    public Verbindung v_1_BLK = new Verbindung();
    public Verbindung v_8_BLK = new Verbindung();
    public Verbindung v_15_VP = new Verbindung();
    public Verbindung v_15_FV = new Verbindung();
    public Verbindung v_15_FU = new Verbindung();
    public Verbindung v_23_ZT = new Verbindung();

    public Verbindung v_pr_RM3 = new Verbindung();
    public Verbindung v_pr_RL = new Verbindung();
    public Verbindung v_pr_RM5 = new Verbindung();
    public Verbindung p_FB6 = new Verbindung();

    // Signalverbindungen
    public sbyte o_VSR_N_1;
    public sbyte o_VSR_N_2;
    public Output o_VSR_N_DS_F6 = new Output();
    public Output o_VSR_N_DS_fs = new Output();
    public Verbindung v_VSR_DS = new Verbindung();

    public sbyte o_VSR_V_1;
    public sbyte o_VSR_V_2;
    public Output o_SVP = new Output();
    public Output o_BSS = new Output();

    // Relais
    public Relais ST = new Relais();
    public Relais ZT = new Relais();
    public Relais AA = new Relais();
    public Relais S = new Relais();
    public Relais Z = new Relais();
    public Relais TS = new Relais();
    public Relais BES_E = new Relais();
    public Relais FU = new Relais();
    public Relais MZ = new Relais();
    public Relais FW = new Relais();
    public Relais BA = new Relais();
    public Relais FF = new Relais();
    public Relais F1 = new Relais();
    public Relais F2 = new Relais();
    public Relais F3 = new Relais();
    public Relais F5 = new Relais();
    public Relais FV = new Relais();
    public Relais VP = new Relais();
    public Relais PR = new Relais();
    public Relais BL = new Relais();

    // Flachrelais
    public Flachrelais H_RM1 = new Flachrelais();
    public Flachrelais H_RM2 = new Flachrelais();
    public Flachrelais NH_RM = new Flachrelais();
    public Flachrelais F_RM1 = new Flachrelais();
    public Flachrelais F_RM2 = new Flachrelais();
    public Flachrelais F_RM3 = new Flachrelais();
    public Flachrelais F_RM4 = new Flachrelais();
    public Flachrelais F_RM5 = new Flachrelais();

    // Kondensatoren
    public Kondensator k_VP = new Kondensator();
    public Kondensator k_PR = new Kondensator();
    public Kondensator k_FRM2 = new Kondensator();
    public Kondensator k_FRM4 = new Kondensator();

    private Do67 m_zv;
    private bool p_ESig;
    private bool p_Links;
    private bool p_Transit;
    private int p_FB_S18, p_FB_S19, p_FB_S20;
    
    // Dummy-Objekte für Signale / VSR
    private Hauptsignal? m_hsig;
    private TMN814_VSR? m_vsr_this;
    private TMN814_VSR? m_vsr_next;
    public TMN814_VSR VSR => m_vsr_this!;

    public TMN501_HSR(Do67 z, bool zsr, bool nl, bool trans)
    {
        m_zv = z;
        m_zv.Add(this);
        
        m_hsig = null;
        if (Global.ENABLE_DUMMY_HSIG)
            m_hsig = new Hauptsignal(); // Dummy-Initialisierung

        k_VP.Init(25, 20, 0);
        k_PR.Init(32, 1, 0);
        k_FRM2.Init(25, 20, 0);
        k_FRM4.Init(25, 20, 0);

        l_BEL.Value = false;
        p_ESig = zsr;
        p_Links = nl;
        p_Transit = trans;

        v_1_BLK.Value = true;
        v_8_BLK.Value = true;
        v_15_FV.Value = true;
        v_15_VP.Value = true;
        if (p_ESig) v_15_FU.Value = true;

        v_pr_RM3.Value = true;
        v_pr_RL.Value = false;
        v_pr_RM5.Value = true;

        p_FB_S18 = 0;
        p_FB_S19 = 0;
        p_FB_S20 = 0;

        p_FB6.Value = false;

        o_VSR_N_1 = 0;
        o_VSR_N_2 = 0;
        o_VSR_V_1 = 0;
        o_VSR_V_2 = 0;

        m_vsr_this = new TMN814_VSR();
        m_vsr_next = null;
    }

    public void InitFb(FBgr s18, FBgr s19, FBgr s20)
    {
        p_FB_S18 = (int)s18;
        p_FB_S19 = (int)s19;
        p_FB_S20 = (int)s20;
    }

    public void SetHS(Hauptsignal hs) => m_hsig = hs;
    public void SetVSRforThis(TMN814_VSR vsr) => m_vsr_this = vsr;
    public void SetVSRforNext(TMN814_VSR vsr) => m_vsr_next = vsr;

    public override void Update()
    {
        sk_A.Reset();
        sk_E.Reset();

        UpdateBedienung();
        UpdateSpeicher();
        UpdateVerschluss();
        UpdateFahrwegUndSignal();
        UpdateAufloesung();
        UpdateSpurplan();
    }

    private void UpdateBedienung()
    {
        ST.Value = (m_zv.sl_TR_HS.Value && (t_ST.Value || t_SZT.Value))
                   || (m_zv.sl_TRFU_HS.Value && (tfu_ST.Value || tfu_SZT.Value));
        ZT.Value = (m_zv.sl_TR_HS.Value && (t_ZT.Value || t_SZT.Value))
                   || (m_zv.sl_TRFU_HS.Value && (tfu_ZT.Value || tfu_SZT.Value));
        AA.Value = AA.Value || m_zv.sl_AA_FBZ.Value || m_zv.sl_SSA_FBZ.Value;
    }

    private void UpdateSpeicher()
    {
        var pin1 = Do67Spurplan.SpeicherPin(Do67Spurplan.Speicher1, p_Links);
        var pin2 = Do67Spurplan.SpeicherPin(Do67Spurplan.Speicher2, p_Links);
        var anfang = sk_A.IsP(pin1) || sk_A.IsP(pin2) || ST.Value;
        var ende = sk_E.IsP(pin1) || sk_E.IsP(pin2) || ZT.Value;

        TS.Value = p_Transit && (TS.Value || anfang || ende) && !S.Value && !Z.Value;
        S.Value = (S.Value || (ST.Value && anfang && !BES_E.Value)) && !Z.Value;
        Z.Value = (Z.Value || (ZT.Value && v_1_BLK.Value && ende)) && !S.Value;
        BL.Value = anfang && ende;
        if (m_zv.sl_SPL.Value && ZT.Value)
            Z.Value = false;

        sk_A.SetP(pin1, S.Value || TS.Value || ZT.Value);
        sk_E.SetP(pin1, Z.Value || TS.Value || ST.Value);
        sk_A.SetP(pin2, S.Value || ZT.Value);
        sk_E.SetP(pin2, Z.Value || ST.Value);

        BES_E.Value = (BES_E.Value || (i_BS.Value && ZT.Value)) && !BA.Value;
        sk_A.SetP(7, BES_E.Value);
    }

    private void UpdateVerschluss()
    {
        var angefordert = S.Value || Z.Value || TS.Value;
        VP.Value = angefordert
                   && (sk_A.IsP(Do67Spurplan.Verschluss) || sk_E.IsP(Do67Spurplan.Verschluss) || m_zv.sl_VP.Value)
                   && !BES_E.Value;
        FV.Value = angefordert && !VP.Value;
        FW.Value = Do67Spurplan.HatKonflikt(sk_A, sk_E);

        SpurStecker.ConnIf(sk_A, sk_E, Do67Spurplan.Verschluss, VP.Value && !FW.Value);
        for (var spur = Do67Spurplan.FlankenschutzVon; spur <= Do67Spurplan.FlankenschutzBis; spur++)
            SpurStecker.ConnIf(sk_A, sk_E, spur, angefordert && !FW.Value);
        o_SVP.Value = VP.Value && S.Value;
    }

    private void UpdateFahrwegUndSignal()
    {
        FU.Value = VP.Value && sk_A.IsAny(Do67Spurplan.Fahrwegueberwachung) && v_8_BLK.Value && !FW.Value;
        MZ.Value = FU.Value && sk_A.IsP(Do67Spurplan.Fahrwegueberwachung);
        PR.Value = FU.Value && (S.Value || TS.Value);

        F2.Value = sk_A.IsAny(17);
        F3.Value = sk_A.IsP(18);
        F5.Value = sk_A.IsP(19);
        F1.Value = sk_A.IsP(20);
        FF.Value = sk_A.IsAny(16) || (FU.Value && !F1.Value && !F2.Value && !F3.Value && !F5.Value);

        H_RM1.Value = !FU.Value;
        H_RM2.Value = !FU.Value;
        NH_RM.Value = m_zv.sl_NH.Value && ST.Value;
        F_RM1.Value = FU.Value && !NH_RM.Value;
        F_RM2.Value = F_RM1.Value;
        F_RM3.Value = F_RM1.Value && (FF.Value || F1.Value || F2.Value || F3.Value || F5.Value || v_pr_RM3.Value);
        F_RM4.Value = F_RM3.Value;
        F_RM5.Value = F_RM1.Value && v_pr_RM5.Value;

        if (m_vsr_this != null)
        {
            m_vsr_this.i_VS12 = FF.Value || F1.Value ? (sbyte)1 : (sbyte)0;
            m_vsr_this.i_VS34 = F3.Value ? (sbyte)1 : F2.Value ? (sbyte)-1 : (sbyte)0;
            m_vsr_this.i_VS5.Value = F5.Value;
            m_vsr_this.Update();
            m_vsr_this.CommitRelais();
        }

        sk_E.SetP(Do67Spurplan.Fahrwegueberwachung, FU.Value);
        SpurStecker.ConnIf(sk_A, sk_E, Do67Spurplan.SenkrechterFahrbegriff, FU.Value);
    }

    private void UpdateAufloesung()
    {
        BA.Value = m_zv.sl_NA.Value
                   || sk_A.IsP(Do67Spurplan.Notaufloesung)
                   || sk_E.IsP(Do67Spurplan.Notaufloesung);
        var aufloesen = BA.Value
                        || sk_A.IsP(Do67Spurplan.Aufloesung1)
                        || sk_E.IsP(Do67Spurplan.Aufloesung1)
                        || sk_A.IsP(Do67Spurplan.Aufloesung2)
                        || sk_E.IsP(Do67Spurplan.Aufloesung2);

        sk_A.SetP(Do67Spurplan.Aufloesung1, aufloesen);
        sk_E.SetP(Do67Spurplan.Aufloesung2, aufloesen);
        if (!aufloesen)
            return;

        S.Value = false;
        Z.Value = false;
        TS.Value = false;
        BES_E.Value = false;
        VP.Value = false;
        FV.Value = false;
        FU.Value = false;
        MZ.Value = false;
        PR.Value = false;
    }

    private void UpdateSpurplan()
    {
        var festgelegt = (S.Value || Z.Value) && VP.Value && FU.Value;
        sk_A.SetP(Do67Spurplan.FestlegungZugfahrstrasse, festgelegt);
        sk_E.SetP(Do67Spurplan.FestlegungZugfahrstrasse, festgelegt);

        int[] belegt = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 15, 16, 17, 18, 19, 20 };
        for (var spur = 1; spur <= Do67Spurplan.SpurAnzahl; spur++)
        {
            if (Array.IndexOf(belegt, spur) < 0)
                SpurStecker.Conn(sk_A, sk_E, spur);
        }
    }

    public override void Output()
    {
        l_ST.Value = ST.Value || (BL.Value && Global.g_Blinker);
        l_ZT.Value = ZT.Value || (BL.Value && Global.g_Blinker);
        l_SZT.Value = ST.Value || ZT.Value || (BL.Value && Global.g_Blinker);
        l_BesE.Value = BES_E.Value;
        l_H_RM.Value = H_RM1.Value || H_RM2.Value;
        l_NH_RM.Value = NH_RM.Value;
        l_F_RM.Value = F_RM1.Value || F_RM2.Value || F_RM3.Value || F_RM4.Value || F_RM5.Value;
        l_BEL.Value = !S.Value && !Z.Value && !TS.Value && !VP.Value && !FU.Value && !BES_E.Value;
        m_vsr_this?.Output();
    }

    public override bool UpdateWire()
    {
        sk_A.Commit();
        sk_E.Commit();
        return sk_A.HasChanged() || sk_E.HasChanged();
    }

    public void Tp(ref bool l1, ref bool l2, ref bool l3, bool y)
    {
        // Interne Tastenlogik aus der .cpp
    }

    public bool Zv_ssr() => (!MZ.Value && !H_RM2.Value) || PR.Value || AA.Value;
    public bool V_srh() => !MZ.Value && (!H_RM2.Value || NH_RM.Value);
    public bool V_bese() => !FV.Value && BES_E.Value && ZT.Value;
}
