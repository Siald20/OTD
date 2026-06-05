namespace OTD.Interlocking.DO67;

public class Zentralverschluss
{
    public static Zentralverschluss Instance { get; } = new();

    private Zentralverschluss()
    {
    }

    public MonostabilesRelais STRGT { get; } = new("ZV_STRGT");
    public MonostabilesRelais WIUMT { get; } = new("ZV_WIUMT");

    public void SetzeEingaenge(bool wt, bool isSignal)
    {
        
    }
    
}
