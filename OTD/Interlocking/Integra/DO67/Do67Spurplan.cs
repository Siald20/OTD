public static class Do67Spurplan
{
    public const int Speicher1 = 1;
    public const int Speicher2 = 2;
    public const int Verschluss = 3;
    public const int FlankenschutzSucheX = 4;
    public const int FlankenschutzBestaetigungX = 5;
    public const int FlankenschutzSucheY = 6;
    public const int FlankenschutzBestaetigungY = 7;
    public const int FlankenschutzVon = FlankenschutzSucheX;
    public const int FlankenschutzBis = FlankenschutzBestaetigungY;
    public const int Fahrwegueberwachung = 8;
    public const int SenkrechterFahrbegriff = 9;
    public const int AufloesungInFahrrichtung = 10;
    public const int AufloesungEntgegenFahrrichtung = 11;
    public const int Betriebsaufloesung = 12;
    public const int Aufloesung1 = AufloesungInFahrrichtung;
    public const int Aufloesung2 = AufloesungEntgegenFahrrichtung;
    public const int FestlegungZugfahrstrasse = 15;
    public const int FahrbegriffFrei = 16;
    public const int FahrbegriffF2 = 17;
    public const int FahrbegriffF3 = 18;
    public const int FahrbegriffF5 = 19;
    public const int FahrbegriffF1 = 20;
    public const int Notaufloesung = 21;
    public const int SpurAnzahl = 24;

    public static string Beschreibung(int spur) => spur switch
    {
        Speicher1 => "Speicher 1",
        Speicher2 => "Speicher 2",
        Verschluss => "Verschluss Ziel -> Start",
        FlankenschutzSucheX => "Flankenschutz Suche X",
        FlankenschutzBestaetigungX => "Flankenschutz Bestaetigung X",
        FlankenschutzSucheY => "Flankenschutz Suche Y",
        FlankenschutzBestaetigungY => "Flankenschutz Bestaetigung Y",
        Fahrwegueberwachung => "Fahrwegueberwachung / Nothalt",
        SenkrechterFahrbegriff => "Senkrechter Fahrbegriff",
        AufloesungInFahrrichtung => "Aufloesung in Fahrrichtung",
        AufloesungEntgegenFahrrichtung => "Aufloesung entgegen Fahrrichtung",
        Betriebsaufloesung => "Betriebsaufloesung Rangierfahrstrasse",
        FestlegungZugfahrstrasse => "Festlegung Zugfahrstrasse",
        FahrbegriffFrei => "Fahrbegriff freie Fahrt",
        FahrbegriffF2 => "Fahrbegriff F2",
        FahrbegriffF3 => "Fahrbegriff F3",
        FahrbegriffF5 => "Fahrbegriff F5",
        FahrbegriffF1 => "Fahrbegriff F1",
        Notaufloesung => "Notaufloesung Zugfahrstrasse",
        _ => "Frei"
    };

    public static bool IstFlankenschutzSuche(int spur) =>
        spur is FlankenschutzSucheX or FlankenschutzSucheY;

    public static int FlankenschutzBestaetigungZu(int suchSpur) => suchSpur switch
    {
        FlankenschutzSucheX => FlankenschutzBestaetigungX,
        FlankenschutzSucheY => FlankenschutzBestaetigungY,
        _ => throw new System.ArgumentOutOfRangeException(nameof(suchSpur))
    };

    public static int SpeicherPin(int logischeSpur, bool umgekehrt)
    {
        if (logischeSpur is not (Speicher1 or Speicher2))
            return logischeSpur;

        return umgekehrt ? Speicher1 + Speicher2 - logischeSpur : logischeSpur;
    }

    public static bool HatKonflikt(SpurStecker erster, SpurStecker zweiter)
    {
        for (var spur = FlankenschutzVon; spur <= FlankenschutzBis; spur++)
        {
            if (erster.IsM(spur) || zweiter.IsM(spur))
                return true;
        }

        return false;
    }

    public static bool HatFreigabe(SpurStecker erster, SpurStecker zweiter)
    {
        for (var spur = FlankenschutzVon; spur <= FlankenschutzBis; spur++)
        {
            if (erster.IsP(spur) || zweiter.IsP(spur))
                return true;
        }

        return false;
    }

    public static void Durchschalten(SpurStecker links, SpurStecker rechts, params int[] ausnahmen)
    {
        for (var spur = 1; spur <= SpurAnzahl; spur++)
        {
            if (System.Array.IndexOf(ausnahmen, spur) < 0)
                SpurStecker.Conn(links, rechts, spur);
        }
    }
}
