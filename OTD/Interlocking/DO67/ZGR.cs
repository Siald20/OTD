using Avalonia.Controls;
using ZV = OTD.Interlocking.DO67.Zentralverschluss;

namespace OTD.Interlocking.DO67;

public class ZGR : Domino67Element
{
    
  public bool ZGROccupied { get; set; }
    public MonostabilesRelais MR { get; } = new("ZGR-FAL");
    public MonostabilesRelais MZ { get; } = new("ZGR-MZ");
    public MonostabilesRelais NA { get; } = new("ZGR-NA");
    public MonostabilesRelais BAH { get; } = new("Zgr-BAH");
    public MonostabilesRelais ZT  {get;} = new("ZGR-ZT");
    public MonostabilesRelais TZ { get; } = new("ZGR-TZ");
    public MonostabilesRelais Z { get; } = new("ZGR-Z");
    public MonostabilesRelais TS { get; } = new("ZGR-TS");
    public MonostabilesRelais S { get; } = new("ZGR-S");
    public MonostabilesRelais VP { get; } = new("ZGR-VP");
    public MonostabilesRelais FÜ { get; } = new("ZGR-FÜ");
    public MonostabilesRelais DFÜ { get; } = new("ZGR-DFÜ");
    public MonostabilesRelais AS { get; } = new("ZGR-AS");
    public MonostabilesRelais A { get; } = new("ZGR-A");
    public MonostabilesRelais AH { get; } = new("ZGR-AH");
    public MonostabilesRelais SF { get; } = new("ZGR-SF");
    public MonostabilesRelais SV { get; } = new("ZGR-SV");
    public MonostabilesRelais IS { get; } = new("ZGR-IS");
    
    public BistabilesRelais FV { get; } = new("ZGR-FV");
    public BistabilesRelais DV { get; } = new("ZGR-DV");
    public BistabilesRelais VFÜ { get; } = new("ZGR-VFÜ");
    public BistabilesRelais ZV { get; } = new("ZGR-ZV");
    
    public void Occupied()
    {
        if (ZGROccupied)
        {
            IS.SetzeSpannung(false);
            
        }
        else
        {
            IS.SetzeSpannung(true);
            
        }
    }
    
    
    

    public override void VerarbeiteSpurstroeme()
    {
        if (SteckerE.Spur01 && !MR.IstAngezogen)
        {
            
        }
    }
}

