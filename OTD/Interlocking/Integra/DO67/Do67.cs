using System;
using System.Collections.Generic;

// Stub: Damit der Code kompilierfähig ist.
public class Stellwerk { }

// Dummy-Klassen für fehlende Baugruppen-Referenzen im Do67 Manager.
// Diese können später natürlich durch echte Klassen ersetzt werden.
public class TMN504_KSP { }
public class XRA10_HSP { public SpurStecker sk_A = new SpurStecker(); public SpurStecker sk_E = new SpurStecker(); public bool p_ESig; public bool p_Links; }
public class TMN500_4_WUR { }

public class Do67 : Stellwerk
{
    // C# Instanzierung der Signale (entspricht den C++ Objekten)
    public Output sl_TR_RF = new Output();
    public Output sl_TR_ZS = new Output();
    public Output sl_GT_RF = new Output();
    public Output sl_TR_ZF = new Output();
    public Output sl_TR_HS = new Output();
    public Output sl_TR_W = new Output();

    public Output sl_TRFU_RF = new Output();
    public Output sl_TRFU_ZF = new Output();
    public Output sl_TRFU_ZS = new Output();
    public Output sl_GTFU_ZF = new Output();
    public Output sl_TRFU_HS = new Output();
    public Output sl_TRFU_W = new Output();

    // JGB 14/4
    public Output sl_ST_GT = new Output();
    public Output sl_WIU_GT = new Output();
    public Output sl_AM_GT = new Output();
    public Output sl_EV_E = new Output(); 
    public Output sl_EV_A = new Output();
    public Output sl_WA = new Output();

    public Output sl_SP = new Output();
    public Output sl_SPL = new Output();
    public Output sl_VP = new Output();
    public Output sl_NA = new Output();
    public Output sl_BA = new Output();
    public Output sl_NH = new Output();
    public Output sl_BS = new Output();
    public Output sl_HI = new Output();
    public Output sl_SIU = new Output();

    public Output sl_NAV = new Output();

    public Output sl_FG_F = new Output(); 
    public Output sl_SSPA = new Output();
    public Output sl_SSPE = new Output();

    public Output sl_GSE = new Output();
    public Output sl_GSA = new Output();

    public Output sl_FG_BA = new Output();

    // JGB 14/5
    public Output sl_AA_FB = new Output(); 
    public Output sl_SSA_FBZ = new Output();
    public Output sl_AA_FBZ = new Output();

    // JGB 14/6-2
    public Output o_SS = new Output();
    public Output o_BLS = new Output();
    public Output o_ANFS = new Output();
    public Output o_AUFG = new Output();

    // JGB 100/11
    public Output sl_AAUF = new Output();

    public Output sl_ML = new Output();
    public Output sl_BLI = new Output();
    public Output sl_HLA = new Output();

    // Interne Listen für das Manager-Pattern
    protected List<TMN500_WSR> m_wsr_list = new List<TMN500_WSR>();
    protected List<TMN501_HSR> m_hsr_list = new List<TMN501_HSR>();
    protected List<TMN502_ZGR> m_zgr_list = new List<TMN502_ZGR>();
    protected List<TMN503_ZSR> m_zsr_list = new List<TMN503_ZSR>();
    protected List<TMN504_KSP> m_ksp_list = new List<TMN504_KSP>();
    protected List<TMN817_GRS> m_grs_list = new List<TMN817_GRS>();
    protected List<XRA10_HSP> m_hsp_list = new List<XRA10_HSP>();
    protected List<TMN500_4_WUR> m_wur_list = new List<TMN500_4_WUR>();
    protected List<Taste> m_gst_list = new List<Taste>();

    public Do67() { }

    public void Add(TMN500_WSR wsr) => m_wsr_list.Add(wsr);
    public void Add(TMN501_HSR hsr) => m_hsr_list.Add(hsr);
    public void Add(TMN502_ZGR zgr) => m_zgr_list.Add(zgr);
    public void Add(TMN503_ZSR zsr) => m_zsr_list.Add(zsr);
    public void Add(TMN504_KSP ksp) => m_ksp_list.Add(ksp);
    public void Add(TMN817_GRS grs) => m_grs_list.Add(grs);
    public void Add(XRA10_HSP hsp) => m_hsp_list.Add(hsp);
    public void Add(TMN500_4_WUR wur) => m_wur_list.Add(wur);
    public void AddGST(Taste t) => m_gst_list.Add(t);

    protected void Sk(SpurStecker s1, SpurStecker s2)
    {
        SpurStecker.Connect(s1, s2);
    }

    protected void Sk(TMN501_HSR hsr, TMN502_ZGR zgr)
    {
        SpurStecker.Connect(hsr.sk_A, zgr.sk_AH);
        SpurStecker.Connect(hsr.sk_E, zgr.sk_EH);
    }

    protected void Sk(TMN501_HSR hsr, TMN503_ZSR zsr)
    {
        SpurStecker.Connect(hsr.sk_A, zsr.sk_AH);
        SpurStecker.Connect(hsr.sk_E, zsr.sk_EH);
    }

    protected void Sk(XRA10_HSP hsp, TMN502_ZGR zgr)
    {
        SpurStecker.Connect(hsp.sk_A, zgr.sk_AH);
        SpurStecker.Connect(hsp.sk_E, zgr.sk_EH);
        hsp.p_ESig = false;
        hsp.p_Links = zgr.p_Links.Value;
    }

    protected void Sk(XRA10_HSP hsp, TMN503_ZSR zsr)
    {
        SpurStecker.Connect(hsp.sk_A, zsr.sk_AH);
        SpurStecker.Connect(hsp.sk_E, zsr.sk_EH);
        hsp.p_ESig = true;
        hsp.p_Links = zsr.p_Links.Value;
    }
}
