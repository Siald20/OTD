using System;

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
    private Hauptsignal m_hsig;
    private TMN814_VSR m_vsr_this;
    private TMN814_VSR m_vsr_next;

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

        m_vsr_this = null;
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
        bool t;
        bool t_PR = false;

        sk_A.Reset();
        sk_E.Reset();

        // Tasten & Zentralauswertung
        ST.Value = (m_zv.sl_TR_HS.Value && (t_ST.Value || t_SZT.Value)) || (m_zv.sl_TRFU_HS.Value && (tfu_ST.Value || tfu_SZT.Value));
        ZT.Value = (m_zv.sl_TR_HS.Value && (t_ZT.Value || t_SZT.Value)) || (m_zv.sl_TRFU_HS.Value && (tfu_ZT.Value || tfu_SZT.Value));

        AA.Value = (AA.Value || m_zv.sl_AA_FBZ.Value || m_zv.sl_SSA_FBZ.Value) && ((H_RM2.Value && !MZ.Value) || (!F_RM2.Value && MZ.Value));

        // Spur 1 / 2
        if (p_ESig)
            S.Value = !TS.Value && ((S.Value && (!FU.Value || !VP.Value) && sk_A.IsP(2)) || (!VP.Value && !BES_E.Value && ST.Value && sk_A.IsP(1)));
        else
            S.Value = !TS.Value && ((S.Value && (!FU.Value || !VP.Value) && sk_A.IsP(2)) || (!VP.Value && !BES_E.Value && ST.Value && (sk_A.IsP(5) || (Z.Value && sk_A.IsP(1))))); 

        Z.Value = (Z.Value || (ZT.Value && v_1_BLK.Value)) && ((sk_E.IsP(1) && Z.Value) || (sk_E.IsP(2) && !TS.Value));
        
        if (m_zv.sl_SPL.Value && ZT.Value && Z.Value) Z.Value = false;

        // Bediengrenze / ZSR -> deaktiviert
        TS.Value = p_Transit && !Z.Value && !S.Value && (TS.Value || (!FU.Value && !FF.Value && !ST.Value && !FV.Value)) && (sk_A.IsP(1) || (sk_E.IsP(1) && TS.Value));

        sk_E.SetP(1, (m_zv.sl_SP.Value && ZT.Value && !S.Value && !TS.Value) || (Z.Value && !TS.Value && sk_E.IsP(2)) || (sk_A.IsP(1) && TS.Value));
        sk_A.SetP(1, S.Value || (sk_E.IsP(1) && TS.Value));
        sk_E.SetP(2, (TS.Value && sk_A.IsP(2)) || (sk_E.IsP(1) && Z.Value && !TS.Value));
        sk_A.SetP(2, (sk_A.IsP(1) && ST.Value && !BES_E.Value && !VP.Value && S.Value) || (sk_E.IsP(2) && TS.Value));

        // BesE
        t = i_BS.Value && ZT.Value && !BES_E.Value;
        sk_A.SetP(7, t);
        if (t && !FV.Value) BES_E.Value = true;
        if (!ZT.Value && BES_E.Value && !Z.Value && !FV.Value) BES_E.Value = false;

        // Spur 3
        if (p_ESig)
        {
            sk_A.SetP(6, false);
            sk_E.SetP(3, m_zv.sl_VP.Value && !FW.Value && Z.Value && !ZT.Value);
            o_SVP.Value = sk_A.IsP(3) && S.Value;
        }
        else
        {
            SpurStecker.ConnIf(sk_E, sk_A, 3, TS.Value);
            o_SVP.Value = (sk_A.IsP(3) || (sk_E.IsP(3) && TS.Value)) && S.Value;
            sk_A.SetP(6, m_zv.sl_VP.Value && !FW.Value && Z.Value && !ZT.Value);
        }

        // Spur 8
        sk_E.SetP(8, FV.Value && v_8_BLK.Value);
        FU.Value = sk_A.IsP(8);

        t = FU.Value && (((VP.Value || TS.Value) && !S.Value) || MZ.Value);
        MZ.Value = (sk_A.IsP(8) && t) || sk_A.IsP(11);
        if (m_zv.sl_NH.Value && ST.Value && MZ.Value) MZ.Value = false;

        // Spur 9
        SpurStecker.ConnIf(sk_A, sk_E, 9, (MZ.Value || !FV.Value) && (!F_RM2.Value || FV.Value));

        // Spur 10
        sk_A.SetP(10, m_zv.sl_AAUF.Value && MZ.Value);
        sk_E.Set0(10);

        // Spur 11
        if (sk_E.IsP(11) && FW.Value)
        {
            BES_E.Value = false;
            FV.Value = false;
        }

        // Spur 12
        if (p_ESig)
            sk_E.SetP(12, (m_zv.sl_BA.Value && ZT.Value && (!FV.Value || BA.Value)) || (m_zv.sl_NA.Value && ZT.Value));
        else
            sk_E.SetP(12, BA.Value);

        // Spur 15
        SpurStecker.ConnIf2(sk_E, 4, 5, BES_E.Value);

        bool s2_PR = TS.Value && MZ.Value;
        if (s2_PR) FV.Value = true;

        // Kondensatoren / PR-Logik
        k_PR.Laden(25, (VP.Value || (MZ.Value && PR.Value)) && ((!NH_RM.Value && H_RM2.Value && F_RM2.Value && F_RM5.Value && F_RM3.Value) || (!F_RM2.Value && !F_RM5.Value && !F_RM3.Value)));
        k_PR.Entladen(1, true);
        PR.Value = k_PR.Value || s2_PR;

        // Spuren 16 - 20 Auswertung
        FF.Value = sk_A.IsAny(16);
        F2.Value = sk_A.IsAny(17);
        F3.Value = sk_A.IsP(18);
        F5.Value = sk_A.IsP(19);
        F1.Value = sk_A.IsP(20);

        // Relais F_RM Auswertung (Zeitverzögerung)
        k_FRM2.Laden(25, !F_RM3.Value);
        k_FRM2.Entladen(1, F_RM3.Value);
        k_FRM4.Laden(25, F_RM3.Value);
        k_FRM4.Entladen(1, true);

        H_RM2.Value = !H_RM1.Value;
        F_RM1.Value = t_PR && (FF.Value || v_pr_RL.Value) && PR.Value && !VP.Value && MZ.Value;
        F_RM2.Value = F_RM1.Value || (F_RM3.Value && k_FRM2.Value); 
        F_RM3.Value = (FF.Value || v_pr_RM3.Value) && !VP.Value && PR.Value && (FF.Value || v_pr_RL.Value);
        F_RM4.Value = F_RM3.Value || k_FRM4.Value; 
        F_RM5.Value = t_PR;
    }

    public override void Output()
    {
        // Hier folgen die optischen Anzeigen für den Stelltisch
        l_ST.Value = ST.Value;
        l_ZT.Value = ZT.Value;
        l_BesE.Value = BES_E.Value;
        
        // Signalstellung am Dummy-Signal (falls vorhanden)
        if (m_hsig != null)
        {
            // Beispielhaft anhand der C++ Datei:
            // m_hsig->H = !F_RM3 && !F_RM5; ... 
        }
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
