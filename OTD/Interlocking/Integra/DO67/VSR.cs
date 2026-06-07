using System;

/// <summary>
/// TMN814_VSR: Vollständige 1:1 Übersetzung des Vorsignal-Relaissatzes.
/// Basierend auf der gelieferten TMN814_VSR.cpp und .h Logik.
/// </summary>
public class TMN814_VSR : RelaisSatz
{
    // --- Inputs / Externe Referenzen ---
    public Input sl_ML_BLI = new Input();
    public Input sl_AA = new Input();

    public sbyte i_VS12;
    public sbyte i_VS34;
    public Input i_VS5 = new Input();

    // --- Relais-Instanzen (Exakt aus C++ Logik) ---
    public Relais VS1_1 = new Relais(); // 13
    public Relais VS1_2 = new Relais(); // 14
    public Relais VS2 = new Relais();   // 15
    public Relais VS3 = new Relais();   // 16
    public Relais VS4 = new Relais();   // 17
    public Relais VS5 = new Relais();   // 19
    public Relais AV = new Relais();    // 20
    public Relais V_RM1 = new Relais(); // 21
    public Relais V_RM2 = new Relais(); // 22
    public Relais VA_a = new Relais();  // 23

    // --- Lampen ---
    public Lampe l_SR = new Lampe();

    // --- Interne Pointer/Referenzen ---
    private Input m_bli_ptr;
    private Input m_aa_ptr;
    private VorsignalSZRS m_vs;

    public TMN814_VSR()
    {
        sl_ML_BLI.Value = false;
        sl_AA.Value = false;

        i_VS12 = 0;
        i_VS34 = 0;
        i_VS5.Value = false;

        m_bli_ptr = null;
        m_aa_ptr = null;
        m_vs = null;

        // Entspricht #ifdef ENABLE_DUMMY_VSIG
        if (Global.ENABLE_DUMMY_VSIG)
        {
            m_vs = new VorsignalSZRS();
        }
    }

    // Setzt die Pin-Referenzen für Blinklicht und automatische Ausfahrt
    public void SetSL(Input bli, Input aa) 
    { 
        m_bli_ptr = bli; 
        m_aa_ptr = aa; 
    }

    // Zusätzliche Methode, um das Vorsignal-Objekt zu referenzieren (in C++ m_vs)
    public void SetVS(VorsignalSZRS vs)
    {
        m_vs = vs;
    }

    public override void Update()
    {
        bool ac;
        bool pl;
        bool mi;
        bool f13;

        if (m_aa_ptr != null)
            sl_AA.Value = m_aa_ptr.Value;

        VS1_1.Value = (i_VS12 != 0) && (VS1_1.Value || !VS2.Value);
        VS2.Value = (i_VS12 > 0) && VS1_1.Value;
        VS3.Value = (i_VS34 != 0) && (VS3.Value || !VS4.Value);
        VS4.Value = (i_VS34 > 0) && VS3.Value;
        VS5.Value = i_VS5.Value;

        VS1_2.Value = VS1_1.Value;
        VA_a.Value = (sl_AA.Value || VA_a.Value) && !V_RM2.Value && !VS5.Value;
        V_RM2.Value = V_RM1.Value;

        ac = !VS5.Value && !VS1_1.Value && !VS1_2.Value && !VS3.Value;
        pl = VS1_1.Value && VS1_2.Value && VS3.Value && VS4.Value;
        mi = VS1_1.Value && VS1_2.Value && VS3.Value && !VS4.Value;
        f13 = VS2.Value;

        AV.Value = !VS3.Value;
        
        if (m_vs != null) 
        {
            m_vs.Update(ac, pl, mi, f13);
            V_RM1.Value = m_vs.GetRM();
        }
    }

    public override void Output()
    {
        if (m_vs != null)
            m_vs.Commit();

        if (m_bli_ptr != null)
            sl_ML_BLI.Value = m_bli_ptr.Value;
            
        l_SR.Value = sl_ML_BLI.Value && !V_RM2.Value && !VS5.Value;
    }
}