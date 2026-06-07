using System;

/// <summary>
/// TMN841_SP: Vollständige 1:1 Übersetzung des Streckensperre-Moduls.
/// </summary>
public class TMN841_SP : RelaisSatz
{
    // --- Referenzen ---
    private TMN840_BS m_bs;
    public Schleife m_schleife = new Schleife();
    public Kontakt k_BLI = new Kontakt();

    // --- Relais-Instanzen ---
    public Relais SP1 = new Relais();
    public Relais SP2 = new Relais();
    public Relais SP3 = new Relais();
    public Relais SP = new Relais();
    public Relais SP_H = new Relais();
    public Relais A1 = new Relais();
    public Relais A2 = new Relais();
    public Relais SS = new Relais();
    public Relais SHI = new Relais();
    public Relais SPS = new Relais();
    public Relais SPA = new Relais();
    public Relais SPE = new Relais();

    // --- Lampen & Inputs ---
    public Lampe l_sp = new Lampe();
    public Input sl_PWR = new Input();
    public Input sl_ML = new Input();
    public Input sl_ML_BLI = new Input();

    public Input c_SPE_T = new Input();
    public Input c_SPE_2 = new Input();
    public Input c_SPA_T = new Input();
    public Input c_SPA_2 = new Input();
    public Input c_BLI = new Input();
    public Input c_A1 = new Input();

    public TMN841_SP()
    {
        m_bs = null;
        k_BLI.Init(30, 1, 0);

        // Standard-Konfigurationen aus dem Konstruktor
        c_BLI.Value = true;
        c_A1.Value = true;
        
        sl_PWR.Value = true;
        sl_ML.Value = false;
        sl_ML_BLI.Value = false;
    }

    public void SetBS(TMN840_BS bs) { m_bs = bs; }
    
    public void SetSPNB(TMN841_SP sp) 
    { 
        Schleife.Connect(m_schleife, sp.m_schleife); 
    }

    public bool V_sp_aus() 
    { 
        return !SP1.Value && !SS.Value && !SHI.Value && !SP.Value; 
    }

    public override void Update()
    {
        if (m_bs == null) return;

        bool t, t2;
        Schleife.Leitwert lwm = m_schleife.GetLeitwertMinus();
        Schleife.Leitwert lwp = m_schleife.GetLeitwertPlus();
        Schleife.Spannung sp = m_schleife.GetSpannung();

        // Zustandslogik SP1, SP2, SP3
        SP1.Value = (sp > Schleife.Spannung.Aus) && !SPS.Value && !A1.Value && !A2.Value;
        
        if (!A1.Value && !A2.Value) 
        {
            SP2.Value = SPS.Value && (SS.Value || SHI.Value) && (sp == Schleife.Spannung.Plus_Niederohmig);
            SP3.Value = SPS.Value && SHI.Value && (sp == Schleife.Spannung.Minus_Niederohmig);
        }
        else 
        {
            // Logik bei aktiven Fahrten
            SP2.Value = SP2.Value; 
            SP3.Value = SP3.Value;
        }

        // Steuerung A1, A2
        t = (c_SPE_T.Value || (c_SPE_2.Value && SPE.Value));
        t2 = (c_SPA_T.Value || (c_SPA_2.Value && SPA.Value));
        
        A1.Value = !A2.Value && !SP3.Value && (!SS.Value || (c_A1.Value && !m_bs.c_FFZ.Value)) && 
                   ((!m_bs.F.Value && t) || (SS.Value && t2));
        
        // Zustandsübergänge Sperre
        if (Global.CONFIG_SP_E && SP_H.Value && SPE.Value && ((SP2.Value && A1.Value) || (SP3.Value && SHI.Value && !SS.Value))) 
            SP.Value = true;
        
        if (SPA.Value && SHI.Value && ((A2.Value && SP3.Value) || (SP_H.Value && SP2.Value)) && Global.CONFIG_SP_A) 
            SP.Value = false;
        
        if ((((A1.Value && SP2.Value) || (!SS.Value && SHI.Value && SP3.Value)) && SPE.Value && !SPA.Value) || 
            (SP.Value && !A1.Value && SPA.Value && !SPE.Value && !A2.Value && !SP2.Value && SP3.Value)) 
            SP_H.Value = true;
            
        if ((SPA.Value && A1.Value) || (!SP.Value && SP_H.Value)) 
            SP_H.Value = false;
            
        A2.Value = SP.Value && !A1.Value && ((SPA.Value && SHI.Value) || SPE.Value) && !SP2.Value && SS.Value;
    }

    public override void Output()
    {
        if (m_bs == null) return;

        Schleife.Spannung sp = Schleife.Spannung.Aus;

        if (A1.Value) {
            if ((m_bs.F.Value || !SS.Value) && !SHI.Value && SPS.Value) sp = Schleife.Spannung.Plus_Hochohmig;
            if ((!m_bs.F.Value || SHI.Value) && (SS.Value || SHI.Value) && SPS.Value) sp = Schleife.Spannung.Minus_Hochohmig;
        }
        
        m_schleife.SetSpannung(sp);
    }
}
