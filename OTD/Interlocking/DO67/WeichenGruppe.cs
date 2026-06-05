using Avalonia.Controls;
using ZV = OTD.Interlocking.DO67.Zentralverschluss;

namespace OTD.Interlocking.DO67;

public class WeichenGruppe : Domino67Element
{
    public enum WeichenStellung { Links, Rechts }
    public bool WSRLage { get; private set; }
    public bool WSROcupied { get; set; }
    

    public WeichenStellung AktuelleLage { get; private set; } = WeichenStellung.Rechts;
    
    
    public MonostabilesRelais FAL { get; } = new("WG-FAL");
    public MonostabilesRelais FAR { get; } = new("WG-FAR");
    public MonostabilesRelais SAL { get; } = new("WG-SAL");
    public MonostabilesRelais SAR { get; } = new("WG-SAR");
    public MonostabilesRelais SÜL { get; } = new("WG-SL");
    public MonostabilesRelais SÜR { get; } = new("WG-SR");
    public MonostabilesRelais DSÜ { get; } = new("WG-DS");
    public MonostabilesRelais A { get; } = new("WG-A");
    public MonostabilesRelais AS { get; } = new("WG-AS");
    public MonostabilesRelais ARL { get; } = new("WG-ARL");
    public MonostabilesRelais IS { get; } = new("WG-IS");
    public MonostabilesRelais WT { get; } = new("WG-WT");
    
    
    
    public BistabilesRelais WV { get; } = new("WG-WV");
    public BistabilesRelais SV { get; } = new("WG-SV");
    public BistabilesRelais EV { get; } = new("WG-EV");
    public BistabilesRelais L1 { get; } = new("WG-L1");
    public BistabilesRelais L2 { get; } = new("WG-L2");
    public BistabilesRelais L3 { get; } = new("WG-L3");
    public BistabilesRelais S1 { get; } = new("WG-S1");
    public BistabilesRelais DSV { get; } = new("WG-DSV");
    

    private void Weichenlage()
    {
        AktuelleLage = L1.IstAngezogen
            ? WeichenStellung.Links
            : WeichenStellung.Rechts;
    }
    public void Occupied()
    {
        if (WSROcupied)
        {
            IS.SetzeSpannung(false);
            
        }
        else
        {
            IS.SetzeSpannung(true);
            
        }

    }

    public void Handumstellung()
    // STRGT für normale Umstellung, WIUMT für Isolierumgehung
    {
        var befehlAktiv =
            WT.IstAngezogen &&
            (IS.IstAngezogen || ZV.Instance.WIUMT.IstAngezogen) &&
            (ZV.Instance.STRGT.IstAngezogen || ZV.Instance.WIUMT.IstAngezogen) &&
            !WV.IstAngezogen &&
            !EV.IstAngezogen;

        
        
    }
       
    
    

    public override void VerarbeiteSpurstroeme()
    {
        Weichenlage();
        Handumstellung();
        
        if (SteckerS.Spur01)
        {
            if (SteckerL.IstVerbunden)
            {
                SteckerL.Spur01 = true;
                SteckerL.Weitergeben();
            }

            if (SteckerR.IstVerbunden)
            {
                SteckerR.Spur01 = true;
                SteckerR.Weitergeben();
            }
        }

        if (SteckerS.Spur02)
        {
            if (SteckerL.IstVerbunden)
            {
                SteckerL.Spur02 = true;
                SteckerL.Weitergeben();
            }

            if (SteckerR.IstVerbunden)
            {
                SteckerR.Spur02 = true;
                SteckerR.Weitergeben();
            }
        }

        if (SteckerS.Spur03)
        {
            
            
            if (AktuelleLage == WeichenStellung.Links && SteckerL.IstVerbunden)
            {
                SteckerL.Spur03 = true;
                SteckerL.Weitergeben();
            }
            else if (AktuelleLage == WeichenStellung.Rechts && SteckerR.IstVerbunden)
            {
                SteckerR.Spur03 = true;
                SteckerR.Weitergeben();
            }
        }
        else
        {
           
        }

        
    }
}
