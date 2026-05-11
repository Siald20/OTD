namespace OTD.TrackPlan;

public sealed class TrackConnection
{
    public string FromSymbolId { get; init; } = string.Empty;

    public string FromPort { get; init; } = string.Empty;

    public string ToSymbolId { get; init; } = string.Empty;

    public string ToPort { get; init; } = string.Empty;

    public int Cost { get; init; } = 1;

    public bool IsEnabled { get; set; } = true;

    public TrackConnection Reverse()
    {
        return new TrackConnection
        {
            FromSymbolId = ToSymbolId,
            FromPort = ToPort,
            ToSymbolId = FromSymbolId,
            ToPort = FromPort,
            Cost = Cost,
            IsEnabled = IsEnabled
        };
    }
}
