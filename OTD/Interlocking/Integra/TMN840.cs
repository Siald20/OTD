using System;

/// <summary>
/// TMN840_BS: Vollständige 1:1 Übersetzung des Streckenblock-Satzes.
/// Basierend auf der gelieferten TMN840_BS.cpp und .h Logik.
/// </summary>
public interface ITMNBlock
{
    // Falls deine Engine ein ITMNBlock Interface für Streckenblöcke nutzt.
}

public class TMN840_BS : RelaisSatz, ITMNBlock
{
    // --- Externe Referenzen / Pointer ---
    public TMN841_SP m_sp;
    public TMN825_AS_AE m_as;

    // --- Relais-Instanzen (Exakt aus C++ Logik) ---
    public Relais E = new Relais();
    public Relais F = new Relais();
    public Relais RF = new Relais();
    public Relais S1 = new Relais();
    public Relais A1 = new Relais();
    public Relais SS1 = new Relais();
    public Relais SP = new Relais();
    public Relais A = new Relais();
    public Relais S2 = new Relais();
    public Relais A2 = new Relais();
    public Relais V = new Relais();
    public Relais SS2 = new Relais();
    public Relais BLU = new Relais();

    // --- Inputs (Projektierungs-Brücken) ---
    public Input c_RF_RM = new Input();   // 19.11 / 4g.1
    public Input c_FA_FAN = new Input();  // 14.11 - 15.11 / 4b.1 - 4c.1
    public Input c_FA_FFZ = new Input();  // 5d.3 - 6d.3 / 7e.3 - 7f.3
    public Input c_FFZ = new Input();     // 2d.3 - 3d.3 / 7b.3 - 7c.3
    public Input c_FFZ_h = new Input();   // 2d.3 - 4d.3 / 7b.3 - 7d.3
    public Input c_A1_RM = new Input();   // 17.11 - 18.11 / 4e.1 - 4f.1
    public Input c_BLI_FAN = new Input(); // 7d.3 - 8d.3 / 7g.3 - 7h.3
    public Input c_SS1_F = new Input();   // 9l.1 - 9m.1
    public Input c_SS1_A = new Input();   // 4g.1 - 4i.1
    public Input c_BLW = new Input();     // 8i.1 - 6i.1 / 10m.1 - 10k.1
    public Input c_BLW_h = new Input();   // 8i.1 - 7i.1 / 10m.1 - 10l.1

    // Interne Block-Inputs (aus TMN840_BS.h)
    public Input i_A = new Input();
    public Input i_BLU = new Input();
    public Input i_BLU_h = new Input();
    public Input i_V_SP = new Input();
    public Input i_V_nA = new Input();
    public Input i_V_h = new Input();
    public Input i_V_dir = new Input();
    public Input i_nA = new Input();
    public Input i_RF_A1_o = new Input();
    public Input i_A1_vA2 = new Input();
    public Input i_A1_oA2 = new Input();
    public Input i_SS1_vA1 = new Input();
    public Input i_F_FA = new Input();
    public Input i_E_vF = new Input();

    // --- Konfigurationen (Statische Bools) ---
    public bool c_SV;
    public bool c_S2;
    public bool c_S1;
    public bool c_SP;
    public bool c_SP_L;
    public bool c_E;
    public bool c_F;

    // --- SL Inputs ---
    public bool sl_PWR;
    public bool sl_ML;
    public bool sl_ML_BLI;

    // --- Lampen ---
    public Lampe l_w_ab = new Lampe();
    public Lampe l_S2 = new Lampe();
    public Lampe l_S1 = new Lampe();
    public Lampe l_A = new Lampe();
    public Lampe l_SP = new Lampe();
    public Lampe l_E_F = new Lampe();
    public Lampe l_RF_A1 = new Lampe();

    // --- Hardware / Spezialkomponenten ---
    public Kontakt k_BLI = new Kontakt();
    public Kontakt k_BLW = new Kontakt();
    public Schleife m_schleife = new Schleife();

    public TMN840_BS()
    {
        m_sp = null;
        m_as = null;

        // Timer initialisieren
        k_BLI.Init(25, 20, 0);
        k_BLW.Init(50, 1, 0);

        // Hardware-Defaults
        c_SV = true;
        c_S2 = true;
        c_S1 = true;
        c_SP = true;
        c_SP_L = true;

        sl_PWR = true;
        sl_ML = true;
        sl_ML_BLI = false;

        c_E = true;
        c_F = true;
        c_SS1_F.Value = true;
        c_SS1_A.Value = true;
    }

    public bool V_sa(bool c_sa_is = true)
    {
        return (!F.Value && !A.Value && SP.Value && c_sa_is && (m_sp != null ? !m_sp.SP.Value : true) && (m_as != null ? m_as.Gsk() : true)) || BLU.Value;
    }

    public bool V_ss2(bool c_ss2_zu = true)
    {
        return (A.Value && !SP.Value && c_ss2_zu) || BLU.Value;
    }

    public override void Update()
    {
        bool t;
        bool t2;

        // --- Block 606/1 ---
        if (SP.Value && i_A.Value) A.Value = true;
        t = m_sp != null ? m_sp.SP.Value : true;
        BLU.Value = (i_BLU.Value && t) || (BLU.Value && i_BLU_h.Value);
        V.Value = ((((i_V_SP.Value && SP.Value) || (i_V_nA.Value && !A.Value)) || (i_V_h.Value && (V.Value || BLU.Value))) && !S2.Value && !F.Value && !E.Value && !RF.Value) || i_V_dir.Value;
        
        if (i_nA.Value) A.Value = false;

        k_BLW.Update(c_BLW.Value, c_BLW_h.Value);
        k_BLI.Update(c_BLI_FAN.Value && !k_BLW.O(), false);

        // --- Block 606/2 ---
        t = V_sa() && !V_ss2();
        A2.Value = (((S1.Value && !A1.Value) || A2.Value) && A.Value && t) || BLU.Value;

        t2 = m_as != null ? m_as.IsAus() : true;
        S2.Value = (((A2.Value && (!SS2.Value || c_FFZ.Value)) || S2.Value) && A.Value && t && t2) || BLU.Value;

        t = c_FFZ_h.Value ? c_FFZ.Value : true;
        S1.Value = ((((S2.Value && t2) || S1.Value) && A.Value && !A1.Value && !SS1.Value && !SS2.Value && t) || BLU.Value) && c_S1;

        A1.Value = (((A2.Value && i_A1_vA2.Value) || (S2.Value && i_A1_oA2.Value) || A1.Value) && A.Value && (!c_A1_RM.Value || !c_RF_RM.Value) && !SS2.Value) || BLU.Value;

        SS2.Value = (((c_FA_FFZ.Value && c_FA_FAN.Value) || SS2.Value) && A.Value) || BLU.Value;

        SP.Value = (((SS2.Value && A1.Value) || SP.Value) && !V.Value && !S2.Value && !S1.Value && !RF.Value && !F.Value && !E.Value && !SS1.Value) || BLU.Value;

        SS1.Value = (((A1.Value && c_SS1_F.Value && i_SS1_vA1.Value) || (S1.Value && c_SS1_F.Value) || (A2.Value && c_SS1_A.Value) || SS1.Value) && A.Value && !c_RF_RM.Value) || BLU.Value;

        RF.Value = (((SS1.Value && c_RF_RM.Value) || RF.Value) && A.Value) || BLU.Value;
        if (RF.Value && !c_RF_RM.Value && i_RF_A1_o.Value) A1.Value = false;

        // --- Block 606/3 ---
        F.Value = ((((RF.Value && !c_RF_RM.Value) || F.Value) && A.Value && i_F_FA.Value) || BLU.Value) && c_F;
        E.Value = ((((F.Value && !i_F_FA.Value) || E.Value) && A.Value && i_E_vF.Value) || BLU.Value) && c_E;

        // --- Schleifen-Ausgabe (Schienen-Elektrik) ---
        Schleife.Spannung sp = Schleife.Spannung.Aus;

        if (A1.Value && c_S2) sp = Schleife.Spannung.Plus_Niederohmig;
        
        if (A2.Value) 
        {
            if (RF.Value && c_S1) {
                if ((SS1.Value || A2.Value) && !SS2.Value) sp = Schleife.Spannung.Minus_Niederohmig;
                else sp = Schleife.Spannung.Minus_Hochohmig;
            }
            if (V.Value && c_SP) {
                if (A.Value && c_SP_L) sp = Schleife.Spannung.Minus_Niederohmig;
                else sp = Schleife.Spannung.Minus_Hochohmig;
            }
        }

        m_schleife.SetSpannung(sp);

        Schleife.Leitwert lwm = Schleife.Leitwert.Unterbruch;
        Schleife.Leitwert lwp = Schleife.Leitwert.Unterbruch;

        if (SS1.Value && c_S2) // S2
            lwm = Schleife.Leitwert.Niederohmig;
        else if (F.Value && !A.Value && !SS1.Value && c_SV) // SV
            lwm = E.Value ? Schleife.Leitwert.Hochohmig : Schleife.Leitwert.Niederohmig;

        if (!E.Value && !A1.Value && !A2.Value && c_S1) // S1
            lwp = (SS1.Value && !SS2.Value) ? Schleife.Leitwert.Niederohmig : Schleife.Leitwert.Hochohmig;

        m_schleife.SetLeitwert(lwm, lwp);
    }

    public override void Output()
    {
        // Output und Melde-Lampen
        l_w_ab.Value = (sl_ML && !F.Value && !SS2.Value && (!A.Value || V.Value)) || (sl_ML_BLI && (A.Value && !V.Value && !SS2.Value));
        l_S2.Value = sl_ML && ((SS1.Value && !A1.Value) || (!SS1.Value && S2.Value));
        l_S1.Value = sl_ML && ((A1.Value && !SS2.Value) || (SS1.Value && !SS2.Value) || (!A1.Value && !SS1.Value && S1.Value));
        l_A.Value = sl_ML && A.Value && !V.Value;
        l_SP.Value = sl_ML && SP.Value;
        l_E_F.Value = sl_ML && (E.Value || F.Value);
        l_RF_A1.Value = sl_ML && (RF.Value || A1.Value);
    }
}