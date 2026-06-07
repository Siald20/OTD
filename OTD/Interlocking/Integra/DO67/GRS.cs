using System;

/// <summary>
/// TMN817_GRS: Vollständige 1:1 Übersetzung des Gleisrelais-Satzes.
/// Basierend auf der gelieferten TMN817_GRS.cpp und .h Logik.
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
        bool t;

        sk_L.Reset();
        sk_R.Reset();

        IS.Value = i_IS.Value;

        // --- Spur 1 ---
        SpurStecker.ConnIf(sk_L, sk_R, 1, v_1.Value);
        SpurStecker.Conn(sk_L, sk_R, 2);

        o_1.Value = sk_R.IsP(1) || (sk_L.IsP(1) && v_1.Value);
        o_2.Value = sk_R.IsP(2) || sk_L.IsP(2);

        // --- Spur 3 ---
        AA.Value = ((!FUL.Value || AA.Value) && sk_R.IsP(3)) || ((!FUR.Value || AA.Value) && sk_L.IsP(3) && v_3.Value);
        t = ((!FUR.Value || AA.Value) && sk_L.IsP(3)) || ((!FUL.Value || AA.Value) && sk_R.IsP(3) && v_3.Value);

        if (t)
            VR.Value = true;

        t = (!FUR.Value || AA.Value) && v_3.Value && (!FUL.Value || AA.Value);

        SpurStecker.ConnIf(sk_L, sk_R, 3, t);

        // --- Spur 4/5 ---
        // 4 und 5 später ... oder hier, falls keine Rückwärtsauflösung
        if (!p_RueckAufl_L.Value && !p_RueckAufl_R.Value)
        {
            sk_R.Set(4, sk_L.Get(5));
            sk_L.Set(4, sk_R.Get(5));
        }

        // --- Spur 6/7 ---
        sk_L.SetP(6, sk_R.IsP(7) && !IS.Value);
        sk_R.SetP(6, sk_L.IsP(7) && !IS.Value);
        // sk_L.SetP(7, sk_R.IsP(6) && !IS.Value);
        // sk_R.SetP(7, sk_L.IsP(6) && !IS.Value);

        // --- Spur 8 ---
        FUL.Value = sk_L.IsAny(8) && !FUR.Value;
        FUR.Value = sk_R.IsAny(8) && !FUL.Value;

        t = ((A1.Value && (IS.Value || !AH.Value)) || (!A3.Value && !A2.Value && !AS.Value)) && v_8.Value && VR.Value && (FUL.Value || FUR.Value);

        SpurStecker.ConnIf(sk_L, sk_R, 8, t);

        // --- Spur 9 ---
        SpurStecker.Conn(sk_L, sk_R, 9);

        // --- Spur 10 ---
        t = m_zv.sl_AAUF.Value && VR.Value && (A1.Value || !AA.Value);
        A1.Value = (sk_L.IsP(10) && !A3.Value) || (sk_R.IsP(10) && !A2.Value);
        sk_L.SetP(10, t && !A2.Value && A3.Value);
        sk_R.SetP(10, t && !A3.Value && A2.Value);
        AS.Value = (t && AS.Value) || ((p_RueckAufl_L.Value && sk_L.IsP(4)) || (p_RueckAufl_R.Value && sk_R.IsP(4)));

        sk_L.SetP(5, t && p_RueckAufl_L.Value && AS.Value && A3.Value);
        sk_R.SetP(5, t && p_RueckAufl_R.Value && AS.Value && A2.Value);

        // --- Spur 11 ---
        AH.Value = (t && A3.Value && A2.Value && AH.Value && !A1.Value) || (sk_L.IsP(11) && A3.Value) || (sk_R.IsP(11) && A2.Value);
        if (t && A3.Value && A2.Value && AH.Value && !A1.Value && !IS.Value)
            VR.Value = false;

        t = t && (AH.Value || IS.Value || AS.Value);
        A2.Value = (t && ((!AH.Value && A1.Value && FUR.Value) || A2.Value)) || (sk_L.IsP(11) && A3.Value && !A1.Value);
        A3.Value = (t && ((!AH.Value && A1.Value && FUL.Value) || A3.Value)) || (sk_R.IsP(11) && A2.Value && !A1.Value);
        if ((sk_L.IsP(12) || sk_R.IsP(12)) && !FUR.Value && !FUL.Value)
            VR.Value = false;
        
        sk_L.SetP(11, A2.Value && !A3.Value);
        sk_R.SetP(11, A3.Value && !A2.Value);

        SpurStecker.Conn(sk_L, sk_R, 12);

        // 13, 14 TODO
        SpurStecker.Conn(sk_L, sk_R, 14); // Verbindungen waeren auch noch da ...

        // --- Spur 15 ---
        SpurStecker.ConnIf(sk_L, sk_R, 15, v_15_GS.Value && (!IS.Value || v_15_SIU.Value));

        // --- Durchschaltung der restlichen Spuren ---
        SpurStecker.Conn(sk_L, sk_R, 16);
        SpurStecker.Conn(sk_L, sk_R, 17);
        SpurStecker.Conn(sk_L, sk_R, 18);
        SpurStecker.Conn(sk_L, sk_R, 19);
        SpurStecker.Conn(sk_L, sk_R, 20);
        SpurStecker.Conn(sk_L, sk_R, 21);
        // 22 TODO
        SpurStecker.Conn(sk_L, sk_R, 23);
        // conn(sk_L, sk_R, 24); - 24 TODO
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