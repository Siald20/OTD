/// <summary>
/// Vereinfachter Relaisblock fuer die Modellbahn.
/// Alle Bedienungen sind Zweitastenbedienungen mit der Blocktaste.
/// Zwei Blocksätze kommunizieren ueber die Schleife.
/// </summary>
public interface ITMNBlock
{
}

public class TMN840_BS : RelaisSatz, ITMNBlock
{
    private TMN840_BS? m_other;

    public Schleife m_schleife = new();
    public Schleife m_sperrschleife = new();

    // Bedieninputs
    public Input t_BT = new();
    public Input t_FA = new();
    public Input t_VB = new();
    public Input t_RB = new();
    public Input t_BL = new();
    public Input t_FBH = new();
    public Input t_FBF = new();
    public Input t_SE = new();
    public Input t_SA = new();

    // Bedienrelais
    public Relais B_FA = new();
    public Relais B_VB = new();
    public Relais B_RB = new();
    public Relais B_BL = new();
    public Relais B_FBH = new();
    public Relais B_FBF = new();
    public Relais B_SE = new();
    public Relais B_SA = new();

    // Blockrelais
    public Relais F = new();   // Fahrtrichtung abgehend
    public Relais VB = new();  // Vorgeblockt
    public Relais B = new();   // Geblockt
    public Relais AF = new();  // Anfangsfeld
    public Relais EF = new();  // Endfeld
    public Relais FBH = new(); // Freie Bahn lokal festgehalten
    public Relais VE = new();  // Vorblock empfangen
    public Relais BE = new();  // Block empfangen
    public Relais RB = new();  // Rueckblock empfangen
    public Relais GF = new();  // Gegenblock frei / nichts eingestellt
    public Relais RBS = new(); // Rueckblocksperre fuer das Endfeld

    // Sperrsatzrelais
    public Relais SP = new();  // Sperre eingeschaltet
    public Relais SPE = new(); // Sperre vom Gegenblock empfangen
    public Relais SAK = new(); // Lokale Aufhebungsbestaetigung
    public Relais SAE = new(); // Aufhebungsbestaetigung vom Gegenblock empfangen
    public Relais SPA = new(); // Aufhebung beidseitig erkannt

    // Belegtmeldung fuer den integrierten Sperrsatz
    public Input c_FFZ = new();

    // Anzeigen
    public Lampe l_F = new();
    public Lampe l_VB = new();
    public Lampe l_B = new();
    public Lampe l_RB = new();
    public Lampe l_FBH = new();
    public Lampe l_AF = new();
    public Lampe l_EF = new();
    public Lampe l_SP = new();

    public static void Connect(TMN840_BS first, TMN840_BS second)
    {
        first.m_other = second;
        second.m_other = first;
        Schleife.Connect(first.m_schleife, second.m_schleife);
        Schleife.Connect(first.m_sperrschleife, second.m_sperrschleife);
    }

    public override void Update()
    {
        UpdateBedienrelais();
        UpdateFahrtrichtung();
        UpdateSperrsatz();
        UpdateBlockrelais();
        UpdateSchleife();
        UpdateSperrschleife();
    }

    public override void Output()
    {
        l_F.Value = F.Value;
        l_VB.Value = VB.Value;
        l_B.Value = B.Value;
        l_RB.Value = RB.Value;
        l_FBH.Value = FBH.Value;
        l_AF.Value = AF.Value;
        l_EF.Value = EF.Value;
        l_SP.Value = SP.Value;
    }

    private void UpdateBedienrelais()
    {
        // Fahrtrichtungs- und Rueckblockauftraege bleiben gespeichert, bis sie ausgefuehrt sind.
        B_FA.Value = (B_FA.Value || (t_BT.Value && t_FA.Value))
                     && !F.Value
                     && !SP.Value
                     && !SPE.Value;
        B_VB.Value = t_BT.Value && t_VB.Value;
        B_RB.Value = (B_RB.Value || (t_BT.Value && t_RB.Value && EF.Value && BE.Value))
                     && EF.Value;
        B_BL.Value = t_BT.Value && t_BL.Value;
        B_FBH.Value = t_BT.Value && t_FBH.Value;
        B_FBF.Value = t_BT.Value && t_FBF.Value;
        B_SE.Value = t_BT.Value && t_SE.Value && F.Value;
        // Die Seite mit Fahrtrichtung initiiert die Aufhebung.
        // Die Gegenstation darf erst bestaetigen, nachdem sie diese Anforderung empfangen hat.
        B_SA.Value = t_BT.Value && t_SA.Value && SP.Value && (F.Value || SAE.Value);
    }

    private void UpdateFahrtrichtung()
    {
        // FBH ist ein lokales, selbsthaltendes Relais und wird nicht ueber die Schleife uebertragen.
        FBH.Value = (FBH.Value || B_FBH.Value) && !B_FBF.Value;

        var directionAccepted = B_FA.Value
                                && m_other != null
                                && !m_other.FBH.Value
                                && !m_other.B_FA.Value
                                && GF.Value
                                && !VB.Value
                                && !B.Value
                                && !AF.Value
                                && !EF.Value
                                && !VE.Value
                                && !BE.Value
                                && !SP.Value
                                && !SPE.Value;

        var directionRelease = m_other?.B_FA.Value == true
                               && !FBH.Value
                               && !VB.Value
                               && !B.Value
                               && !AF.Value
                               && !EF.Value
                               && !SP.Value
                               && !SPE.Value;

        F.Value = (F.Value || directionAccepted) && !directionRelease;
    }

    private void UpdateBlockrelais()
    {
        var remoteVoltage = m_schleife.GetSpannung();

        VE.Value = remoteVoltage == Schleife.Spannung.Plus_Niederohmig;
        BE.Value = remoteVoltage == Schleife.Spannung.Minus_Niederohmig;
        RB.Value = remoteVoltage == Schleife.Spannung.Plus_Hochohmig;
        GF.Value = remoteVoltage == Schleife.Spannung.Aus
                   && m_schleife.GetLeitwertMinus() == Schleife.Leitwert.Unterbruch
                   && m_schleife.GetLeitwertPlus() == Schleife.Leitwert.Unterbruch;

        VB.Value = (VB.Value || (B_VB.Value && F.Value && GF.Value && !SP.Value))
                   && !RB.Value;

        B.Value = (B.Value || (B_BL.Value && F.Value && VB.Value && AF.Value && GF.Value && !SP.Value))
                  && !RB.Value;

        AF.Value = (AF.Value || (VB.Value && F.Value)) && !RB.Value;
        RBS.Value = (RBS.Value || B_RB.Value) && BE.Value;
        EF.Value = (EF.Value || VE.Value || BE.Value) && !RBS.Value;

        c_FFZ.Value = B.Value;
    }

    private void UpdateSperrsatz()
    {
        var remoteVoltage = m_sperrschleife.GetSpannung();
        SPE.Value = remoteVoltage != Schleife.Spannung.Aus;
        SAE.Value = (SAE.Value || remoteVoltage == Schleife.Spannung.Minus_Niederohmig) && !SPA.Value;

        SAK.Value = (SAK.Value || B_SA.Value) && !SPA.Value;
        SPA.Value = (SPA.Value || (SAK.Value && SAE.Value)) && (SP.Value || SPE.Value);
        SP.Value = (SP.Value || B_SE.Value || SPE.Value) && !SPA.Value;

        if (!SP.Value && !SPE.Value)
        {
            SAK.Value = false;
            SAE.Value = false;
        }
    }

    private void UpdateSchleife()
    {
        var voltage = Schleife.Spannung.Aus;
        if (B_RB.Value && BE.Value)
            voltage = Schleife.Spannung.Plus_Hochohmig;
        else if (B.Value)
            voltage = Schleife.Spannung.Minus_Niederohmig;
        else if (VB.Value)
            voltage = Schleife.Spannung.Plus_Niederohmig;

        m_schleife.SetSpannung(voltage);
        m_schleife.SetLeitwert(
            F.Value || VB.Value || B.Value || AF.Value
                ? Schleife.Leitwert.Niederohmig
                : Schleife.Leitwert.Unterbruch,
            Schleife.Leitwert.Unterbruch);
    }

    private void UpdateSperrschleife()
    {
        var voltage = Schleife.Spannung.Aus;
        if (SP.Value && !SPA.Value)
            voltage = SAK.Value
                ? Schleife.Spannung.Minus_Niederohmig
                : Schleife.Spannung.Plus_Niederohmig;

        m_sperrschleife.SetSpannung(voltage);
        m_sperrschleife.SetLeitwert(
            SP.Value && !SPA.Value ? Schleife.Leitwert.Niederohmig : Schleife.Leitwert.Unterbruch,
            SAK.Value ? Schleife.Leitwert.Niederohmig : Schleife.Leitwert.Unterbruch);
    }
}
