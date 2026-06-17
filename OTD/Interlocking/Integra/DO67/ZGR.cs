using System;

/// <summary>
/// Modellbahnfreundlicher Zwergsignal-Gruppenrelais-Satz mit festem 24-Spur-Vertrag.
/// Die Speicher-Spuren 1/2 werden bei umgekehrter Einbaurichtung logisch getauscht.
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
    public Relais BL = new Relais();     public Relais TR = new Relais();
    public Relais VM = new Relais();     // Fahrt mit Vorsicht, schraeger Fahrbegriff

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
    private Zwergsignal? m_zsig;

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
    
    public Zwergsignal? GetZwerg() { return m_zsig; }

    public bool Zv_zsr() { return (!RM1.Value && !RM2.Value) || VP.Value; }

    public override void Update()
    {
        UpdateVereinfacht();
    }

    private void UpdateVereinfacht()
    {
        sk_A.Reset();
        sk_E.Reset();
        sk_AH.Reset();
        sk_EH.Reset();

        var st = (m_zv.sl_TR_ZS.Value && (t_ST.Value || t_SZT.Value))
                 || (m_zv.sl_TRFU_ZS.Value && (tfu_ST.Value || tfu_SZT.Value));
        var zt = (m_zv.sl_TR_ZS.Value && (t_ZT.Value || t_SZT.Value))
                 || (m_zv.sl_TRFU_ZS.Value && (tfu_ZT.Value || tfu_SZT.Value));

        UpdateSignalRueckmeldung();
        UpdateSpeicher(st, zt);
        UpdateVerschlussUndFlankenschutz();
        UpdateFahrwegUndFahrbegriff(st);
        UpdateAufloesung(zt);
        UpdateFestlegung();
        UpdateFreieSpuren();
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

        var startImpuls = st;
        var zielImpuls = zt;
        var anfang1 = sk_A.IsP(pin1) || sk_AH.IsP(pin1) || startImpuls;
        var ende1 = sk_E.IsP(pin1) || sk_EH.IsP(pin1) || zielImpuls;
        var anfang2 = sk_A.IsP(pin2) || sk_AH.IsP(pin2) || startImpuls;
        var ende2 = sk_E.IsP(pin2) || sk_EH.IsP(pin2) || zielImpuls;

        A1Input = anfang1;
        E1Input = ende1;
        A2Input = anfang2;
        E2Input = ende2;

        TS.Value = (TS.Value || anfang1 || sk_AH.IsP(pin1)) && !TZ.Value && !Z.Value;
        TZ.Value = (TZ.Value || ende1 || sk_EH.IsP(pin1)) && !TS.Value && !S.Value;
        S.Value = (S.Value || (st && (TS.Value || anfang1 || anfang2))) && !Z.Value;
        Z.Value = (Z.Value || (zt && (TZ.Value || ende1 || ende2))) && !S.Value;
        BL.Value = (anfang1 || ende1) && (anfang2 || ende2);
        TR.Value = BL.Value && !S.Value && !Z.Value;

        if (m_zv.sl_SPL.Value && zt)
            Z.Value = false;

        sk_A.SetP(pin1, S.Value || TS.Value || ende1);
        sk_E.SetP(pin1, Z.Value || TZ.Value || anfang1);
        sk_A.SetP(pin2, S.Value || ende2);
        sk_E.SetP(pin2, Z.Value || anfang2);
        sk_AH.SetP(pin1, TS.Value);
        sk_EH.SetP(pin1, TZ.Value);
        sk_AH.SetP(pin2, S.Value);
        sk_EH.SetP(pin2, Z.Value);
    }

    private void UpdateVerschlussUndFlankenschutz()
    {
        var angefordert = S.Value || Z.Value || TS.Value || TZ.Value || TR.Value;
        var flankenkonflikt = Do67Spurplan.HatKonflikt(sk_AH, sk_EH);
        var verschlussFreigabe = sk_AH.IsP(Do67Spurplan.Verschluss)
                                 || sk_EH.IsP(Do67Spurplan.Verschluss)
                                 || m_zv.sl_VP.Value;

        SV.Value = flankenkonflikt;
        VP.Value = angefordert && verschlussFreigabe && !SV.Value;
        ZV1.Value = VP.Value;
        ZV2.Value = VP.Value;
        DV.Value = angefordert && !VP.Value;
        DSA_ZR.Value = SV.Value;

        sk_A.SetP(Do67Spurplan.Verschluss, VP.Value);
        sk_E.SetP(Do67Spurplan.Verschluss, VP.Value);
        sk_AH.SetP(Do67Spurplan.Verschluss, VP.Value);
        sk_EH.SetP(Do67Spurplan.Verschluss, VP.Value);

        for (var spur = Do67Spurplan.FlankenschutzVon; spur <= Do67Spurplan.FlankenschutzBis; spur++)
        {
            sk_A.SetIf(spur, sk_EH.Get(spur), angefordert);
            sk_E.SetIf(spur, sk_AH.Get(spur), angefordert);
            sk_AH.SetIf(spur, sk_E.Get(spur), angefordert);
            sk_EH.SetIf(spur, sk_A.Get(spur), angefordert);
        }
    }

    private void UpdateFahrwegUndFahrbegriff(bool st)
    {
        var ueberwachung = sk_AH.Get(Do67Spurplan.Fahrwegueberwachung);
        if (ueberwachung == 0)
            ueberwachung = sk_EH.Get(Do67Spurplan.Fahrwegueberwachung);
        if (ueberwachung == 0 && VP.Value && !i_IS.Value)
            ueberwachung = -1;

        var istZiel = Z.Value && !TR.Value;
        var vorsichtVonA = sk_A.IsP(Do67Spurplan.SenkrechterFahrbegriff);
        var vorsichtVonE = sk_E.IsP(Do67Spurplan.SenkrechterFahrbegriff);
        var vorsichtAuftrag = p_Links.Value ? vorsichtVonA : vorsichtVonE;

        FU.Value = VP.Value && ueberwachung != 0 && !SV.Value && !istZiel;
        MR.Value = FU.Value && ueberwachung < 0;
        MZ.Value = FU.Value && ueberwachung > 0;
        MRZ_ZR.Value = MR.Value || MZ.Value;
        VFU.Value = FU.Value;
        DFU.Value = !FU.Value && (S.Value || Z.Value);
        FV.Value = VP.Value && !FU.Value;

        VM.Value = FU.Value && vorsichtAuftrag;
        SF.Value = FU.Value && !VM.Value;
        if (m_zv.sl_NH.Value && st)
            SF.Value = false;

        sk_A.Set(Do67Spurplan.Fahrwegueberwachung, ueberwachung);
        sk_E.Set(Do67Spurplan.Fahrwegueberwachung, ueberwachung);
        sk_AH.Set(Do67Spurplan.Fahrwegueberwachung, ueberwachung);
        sk_EH.Set(Do67Spurplan.Fahrwegueberwachung, ueberwachung);
        // Das Ziel sendet den Vorsichtauftrag nur an den unmittelbar davorliegenden ZGR.
        sk_A.SetP(Do67Spurplan.SenkrechterFahrbegriff, istZiel && !p_Links.Value);
        sk_E.SetP(Do67Spurplan.SenkrechterFahrbegriff, istZiel && p_Links.Value);
        sk_AH.SetP(Do67Spurplan.SenkrechterFahrbegriff, SF.Value);
        sk_EH.SetP(Do67Spurplan.SenkrechterFahrbegriff, SF.Value);
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

        AS.Value = aufloesen;
        A.Value = aufloesen && VP.Value;
        AH.Value = aufloesen && !FU.Value;

        sk_A.SetP(Do67Spurplan.Aufloesung1, aufloesen);
        sk_E.SetP(Do67Spurplan.Aufloesung1, aufloesen);
        sk_A.SetP(Do67Spurplan.Aufloesung2, aufloesen);
        sk_E.SetP(Do67Spurplan.Aufloesung2, aufloesen);

        if (!aufloesen)
            return;

        S.Value = false;
        Z.Value = false;
        TS.Value = false;
        TZ.Value = false;
        VP.Value = false;
        ZV1.Value = false;
        ZV2.Value = false;
        DV.Value = false;
        FV.Value = false;
        FU.Value = false;
        SF.Value = false;
    }

    private void UpdateFestlegung()
    {
        var festgelegt = VP.Value && FU.Value && !SV.Value && (!i_IS.Value || v_15_SIU.Value) && v_15_GS.Value;
        if (!festgelegt)
            return;

        var vonA = sk_A.IsAny(Do67Spurplan.FestlegungZugfahrstrasse) || sk_AH.IsAny(Do67Spurplan.FestlegungZugfahrstrasse);
        var vonE = sk_E.IsAny(Do67Spurplan.FestlegungZugfahrstrasse) || sk_EH.IsAny(Do67Spurplan.FestlegungZugfahrstrasse);
        sk_A.SetP(Do67Spurplan.FestlegungZugfahrstrasse, vonE);
        sk_AH.SetP(Do67Spurplan.FestlegungZugfahrstrasse, vonE);
        sk_E.SetP(Do67Spurplan.FestlegungZugfahrstrasse, vonA);
        sk_EH.SetP(Do67Spurplan.FestlegungZugfahrstrasse, vonA);
    }

    private void UpdateFreieSpuren()
    {
        int[] belegt =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 15
        };
        Do67Spurplan.Durchschalten(sk_A, sk_E, belegt);
        Do67Spurplan.Durchschalten(sk_AH, sk_EH, belegt);
    }

    public override void Output()
    {
        l_BEL.Value = !DSA_ZR.Value && !SV.Value && !DFU.Value && !FU.Value && !ZV1.Value;

        l_RM.Value = (m_zv.sl_ML.Value && MRZ_ZR.Value && !RM1.Value && !RM2.Value) || (m_zv.sl_BLI.Value && !VP.Value && (RM1.Value || RM2.Value));
        l_ws.Value = m_zv.sl_ML.Value && (DV.Value || ZV1.Value) && !i_IS.Value;
        l_rt.Value = m_zv.sl_ML.Value && i_IS.Value;
        l_ni.Value = m_zv.sl_ML.Value && (DV.Value || ZV1.Value);
        l_ST.Value = m_zv.sl_BLI.Value && (S.Value || (BL.Value && Global.g_Blinker));
        l_ZT.Value = m_zv.sl_BLI.Value && (Z.Value || (BL.Value && Global.g_Blinker));
        l_SZT.Value = m_zv.sl_BLI.Value && (S.Value || Z.Value || (BL.Value && Global.g_Blinker));
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
