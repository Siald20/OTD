namespace OTD.Interlocking.DO67;

using System;

/// <summary>
/// Repräsentiert das originale 24-polige Domino 67 Spurkabel der Integra Signum / SBB.
/// </summary>
public struct SpurkabelDomino67
{
    // 32-Bit-Speicher für die Spuren 1 bis 24
    private uint _spuren;

    #region Spur 1 bis 5: Fahrstraßensuche & Flankenschutz
    
    // Spur 1/2: Fahrstrassenspeicher (Suchstromkreise)
    public bool Spur01_Fahrstrassenspeicher 
    { get => GetSpur(1); set => SetSpur(1, value); }
    
    public bool Spur02_Fahrstrassenspeicher 
    { get => GetSpur(2); set => SetSpur(2, value); }

    // Spur 3: Weichensteller und Verschlussrelais
    public bool Spur03_WeichenstellerVerschluss 
    { get => GetSpur(3); set => SetSpur(3, value); }

    // Spur 4/5: Flankenschutzsuche (Richtungsabhängig X/Y)
    public bool Spur04_Flankenschutzsuche_X 
    { get => GetSpur(4); set => SetSpur(4, value); }
    
    public bool Spur05_Flankenschutzsuche_Y 
    { get => GetSpur(5); set => SetSpur(5, value); }

    #endregion

    #region Spur 6 bis 9: Überwachung & Fahrbegriffe
    
    // Spur 6/7: Flankenschutzüberwachung (Richtungsabhängig Y/X)
    public bool Spur06_Flankenschutzueberwachung_Y 
    { get => GetSpur(6); set => SetSpur(6, value); }
    
    public bool Spur07_Flankenschutzueberwachung_X 
    { get => GetSpur(7); set => SetSpur(7, value); }

    // Spur 8: Fahrwegüberwachung und Nothalt
    public bool Spur08_FahrwegueberwachungNothalt 
    { get => GetSpur(8); set => SetSpur(8, value); }

    // Spur 9: Senkrechter Fahrbegriff (SF)
    public bool Spur09_SenkrechterFahrbegriff_SF 
    { get => GetSpur(9); set => SetSpur(9, value); }

    #endregion

    #region Spur 10 bis 15: Auflösung & Festlegung
    
    // Spur 10: Automatische Auflösung, Funktionsabwicklung in Fahrrichtung
    public bool Spur10_AutoAufloesung_InFahrtrichtung 
    { get => GetSpur(10); set => SetSpur(10, value); }

    // Spur 11: Automatische Auflösung, Funktionsabwicklung entgegen der Fahrrichtung
    public bool Spur11_AutoAufloesung_EntgegenFahrtrichtung 
    { get => GetSpur(11); set => SetSpur(11, value); }

    // Spur 12: Betriebsauflösung
    public bool Spur12_Betriebsaufloesung 
    { get => GetSpur(12); set => SetSpur(12, value); }

    // Spur 13 & 14: Diverse Verwendungen
    public bool Spur13_DiverseVerwendungen 
    { get => GetSpur(13); set => SetSpur(13, value); }
    
    public bool Spur14_DiverseVerwendungen 
    { get => GetSpur(14); set => SetSpur(14, value); }

    // Spur 15: Festlegung
    public bool Spur15_Festlegung 
    { get => GetSpur(15); set => SetSpur(15, value); }

    #endregion

    #region Spur 16 bis 20: Die Schweizer Fahrbegriffe (Signalisierung)
    
    // Spur 16: Fahrbegriffsrelais für freie Fahrt (FF) -> Vmax
    public bool Spur16_Fahrbegriff_FreieFahrt_FF 
    { get => GetSpur(16); set => SetSpur(16, value); }

    // Spur 17: Fahrbegriffsrelais für Fahrt 2 (F2) -> 40 km/h
    public bool Spur17_Fahrbegriff_Fahrt2_F2 
    { get => GetSpur(17); set => SetSpur(17, value); }

    // Spur 18: Fahrbegriffsrelais für Fahrt 3 (F3) -> 60 km/h
    public bool Spur18_Fahrbegriff_Fahrt3_F3 
    { get => GetSpur(18); set => SetSpur(18, value); }

    // Spur 19: Fahrbegriffsrelais für Fahrt 5 (F5) -> 90 km/h
    public bool Spur19_Fahrbegriff_Fahrt5_F5 
    { get => GetSpur(19); set => SetSpur(19, value); }

    // Spur 20: Fahrbegriffsrelais für Fahrt 1 (F1) -> Ankündigung Freie Fahrt mit reduzierter Geschwindigkeit
    public bool Spur20_Fahrbegriff_Fahrt1_F1 
    { get => GetSpur(20); set => SetSpur(20, value); }

    #endregion

    #region Spur 21 bis 24: Sonderfunktionen
    
    // Spur 21: Notauflösung
    public bool Spur21_Notaufloesung 
    { get => GetSpur(21); set => SetSpur(21, value); }

    // Spur 22: Diverse Verwendungen
    public bool Spur22_DiverseVerwendungen 
    { get => GetSpur(22); set => SetSpur(22, value); }

    // Spur 23: Abschaltung der Vorzugsfahrstrassen
    public bool Spur23_AbschaltungVorzugsfahrstrassen 
    { get => GetSpur(23); set => SetSpur(23, value); }

    // Spur 24: Diverse Verwendungen
    public bool Spur24_DiverseVerwendungen 
    { get => GetSpur(24); set => SetSpur(24, value); }

    #endregion

    #region Interne Bit-Arithmetik (1-basiert für Spur 1-24)

    private bool GetSpur(int spurNummer)
    {
        // Verschiebung um (spurNummer - 1), damit Spur 1 auf Bit 0 liegt
        return (_spuren & (1u << (spurNummer - 1))) != 0;
    }

    private void SetSpur(int spurNummer, bool value)
    {
        int bitIndex = spurNummer - 1;
        if (value)
            _spuren |= (1u << bitIndex);
        else
            _spuren &= ~(1u << bitIndex);
    }

    public void AllesStromlos() => _spuren = 0;

    #endregion
}