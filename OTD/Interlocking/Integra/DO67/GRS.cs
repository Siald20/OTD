using System;

/// <summary>
/// Gleisrelais-Satz mit vollständigem 24-Spur-Plan.
/// </summary>
public class TMN817_GRS : RelaisSatz
{
    // --- Verbindungen (Projektierung) ---
    public Verbindung v_1 = new Verbindung(); // 6.9 - 14.9 / 4c.1 - 6f.1
    public Verbindung v_3 = new Verbindung(); // 23.11 - 24.11 / 7c.1 - 8c.1
    public Verbindung v_8 = new Verbindung(); // 18.11 - 19.11 / 2a.1 - 3a.1
    public Verbindung v_15_SIU = new Verbindung();
    public Verbindung v_15_GS = new Verbindung();

    public Verbindung p_RueckAufl_L = new Verbindung();
    public Verbindung p_RueckAufl_R = new Verbindung();

    // --- Ausgänge (Outputs) ---
    public Output o_1 = new Output(); // 4c.1
    public Output o_2 = new Output(); // 6f.1

    // --- Lampen ---
    public Lampe l_ws = new Lampe();
    public Lampe l_rt = new Lampe();

    // --- Inputs ---
    public Input i_IS = new Input();

    // --- Spurstecker ---
    public SpurStecker sk_L = new SpurStecker();
    public SpurStecker sk_R = new SpurStecker();

    // --- Relais-Instanzen (Exakt aus C++ Logik) ---
    public RelaisInv IS = new RelaisInv();
    public Relais A1 = new Relais();
    public Relais A2 = new Relais();
    public Relais A3 = new Relais();
    public Relais AH = new Relais();
    public Relais AS = new Relais();
    public Relais VR = new Relais();
    public Relais AA = new Relais();
    public Relais FUL = new Relais();
    public Relais FUR = new Relais();

    // --- Interne Referenz ---
    private Do67 m_zv;

    public TMN817_GRS(Do67 z)
    {
        m_zv = z;
        m_zv.Add(this);

        p_RueckAufl_L.Value = false;
        p_RueckAufl_R.Value = false;

        if (Global.INIT_IS_FREI)
        {
            i_IS.Value = true;
        }

        v_1.Value = true;
        v_3.Value = true;
        v_8.Value = true;
        v_15_GS.Value = true;
    }

    public override void Update()
    {
        sk_L.Reset();
        sk_R.Reset();

        UpdateBelegung();
        UpdateVerschluss();
        UpdateFahrweg();
        UpdateAufloesung();
        UpdateSpurplan();
    }

    private void UpdateBelegung()
    {
        IS.Value = i_IS.Value;
        o_1.Value = sk_L.IsP(Do67Spurplan.Speicher1) || sk_R.IsP(Do67Spurplan.Speicher1);
        o_2.Value = sk_L.IsP(Do67Spurplan.Speicher2) || sk_R.IsP(Do67Spurplan.Speicher2);

        SpurStecker.ConnIf(sk_L, sk_R, Do67Spurplan.Speicher1, v_1.Value);
        SpurStecker.Conn(sk_L, sk_R, Do67Spurplan.Speicher2);
    }

    private void UpdateVerschluss()
    {
        FUL.Value = sk_L.IsAny(Do67Spurplan.Fahrwegueberwachung) && !FUR.Value;
        FUR.Value = sk_R.IsAny(Do67Spurplan.Fahrwegueberwachung) && !FUL.Value;
        var verschlussAnforderung = sk_L.IsP(Do67Spurplan.Verschluss)
                                    || sk_R.IsP(Do67Spurplan.Verschluss);

        AA.Value = verschlussAnforderung && (FUL.Value || FUR.Value);
        VR.Value = (VR.Value || verschlussAnforderung) && !AS.Value;
        SpurStecker.ConnIf(sk_L, sk_R, Do67Spurplan.Verschluss, v_3.Value && !AS.Value);
    }

    private void UpdateFahrweg()
    {
        var ueberwacht = VR.Value && v_8.Value && (FUL.Value || FUR.Value);
        SpurStecker.ConnIf(sk_L, sk_R, Do67Spurplan.Fahrwegueberwachung, ueberwacht);
        SpurStecker.Conn(sk_L, sk_R, Do67Spurplan.SenkrechterFahrbegriff);

        sk_L.SetP(6, sk_R.IsP(7) && !IS.Value);
        sk_R.SetP(6, sk_L.IsP(7) && !IS.Value);
    }

    private void UpdateAufloesung()
    {
        var links = sk_L.IsP(Do67Spurplan.Aufloesung1) || sk_L.IsP(Do67Spurplan.Aufloesung2);
        var rechts = sk_R.IsP(Do67Spurplan.Aufloesung1) || sk_R.IsP(Do67Spurplan.Aufloesung2);
        var rueckaufloesung = (p_RueckAufl_L.Value && sk_L.IsP(4))
                              || (p_RueckAufl_R.Value && sk_R.IsP(4));
        var aufloesen = m_zv.sl_AAUF.Value && (links || rechts || rueckaufloesung);

        A1.Value = links || rechts;
        A2.Value = aufloesen && FUR.Value;
        A3.Value = aufloesen && FUL.Value;
        var verschlussAnforderung = sk_L.IsP(Do67Spurplan.Verschluss) || sk_R.IsP(Do67Spurplan.Verschluss);
        AS.Value = (AS.Value || aufloesen) && verschlussAnforderung;
        AH.Value = aufloesen && !IS.Value;

        sk_L.SetP(Do67Spurplan.Aufloesung1, aufloesen && FUR.Value);
        sk_R.SetP(Do67Spurplan.Aufloesung1, aufloesen && FUL.Value);
        sk_L.SetP(Do67Spurplan.Aufloesung2, aufloesen && FUR.Value);
        sk_R.SetP(Do67Spurplan.Aufloesung2, aufloesen && FUL.Value);

        if (aufloesen)
            VR.Value = false;
    }

    private void UpdateSpurplan()
    {
        if (!p_RueckAufl_L.Value && !p_RueckAufl_R.Value)
        {
            sk_R.Set(4, sk_L.Get(5));
            sk_L.Set(4, sk_R.Get(5));
        }

        SpurStecker.Conn(sk_L, sk_R, 12);
        SpurStecker.Conn(sk_L, sk_R, 13);
        SpurStecker.Conn(sk_L, sk_R, 14);
        SpurStecker.ConnIf(sk_L, sk_R, Do67Spurplan.FestlegungZugfahrstrasse, v_15_GS.Value && (!IS.Value || v_15_SIU.Value));
        for (var spur = 16; spur <= Do67Spurplan.SpurAnzahl; spur++)
            SpurStecker.Conn(sk_L, sk_R, spur);
    }

    public override void Output()
    {
        l_ws.Value = VR.Value && !IS.Value;
        l_rt.Value = IS.Value;
    }

    public override bool UpdateWire()
    {
        sk_L.Commit();
        sk_R.Commit();

        return sk_L.HasChanged() || sk_R.HasChanged();
    }
}
