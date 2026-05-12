namespace OTD.TrackPlan.Interlocking.Profiles;

public static class Domino67PropertyNames
{
    // Gleis
    public const string TrackOccupied = "Do67.TrackOccupied";
    public const string TrackClosed = "Do67.TrackClosed";
    public const string TrackLocked = "Do67.TrackLocked";
    public const string TrackError = "Do67.TrackError";

    // Block
    public const string BlockBlocked = "Do67.BlockBlocked";
    public const string BlockClosed = "Do67.BlockClosed";
    public const string BlockError = "Do67.BlockError";
    public const string LineBlockDirection = "Do67.LineBlockDirection";
    public const string LineBlockDirectionIncoming = "incoming";
    public const string LineBlockDirectionOutgoing = "outgoing";

    // Weichen
    public const string SwitchLocked = "Do67.SwitchLocked";
    public const string FlankProtectionSymbols = "Do67.FlankProtectionSymbols";
    public const string FlankProtectionEnabled = "Do67.FlankProtectionEnabled";
    public const string RequiredPosition = "Do67.RequiredPosition";
    public const string SwitchError = "Do67.SwitchError";
    public const string SwitchOccupied = "Do67.SwitchOccupied";

    // Signal
    public const string HoldRed = "Do67.HoldRed";
    public const string SignalIsGreen = "Do67.SignalIsGreen";
    public const string SignalKeepGreenOnRelease = "Do67.SignalKeepGreenOnRelease";
    public const string SignalIsRed = "Do67.SignalIsRed";
    public const string SignalError = "Do67.SignalError";

    // Bahnuebergang
    public const string LevelCrossingClosed = "Do67.Closed";
    public const string LevelCrossingAutoClose = "Do67.AutoClose";
    public const string LevelCrossingAutoCloseDelaySeconds = "Do67.AutoCloseDelaySeconds";
    public const string LevelCrossingKeepClosedOnRelease = "Do67.LevelCrossingKeepClosedOnRelease";
    public const string LevelCrossingOpen = "Do67.LevelCrossingOpen";
    public const string LevelCrossingError = "Do67.LevelCrossingError";
    public const string LevelCrossingIsClosing = "Do67.LevelCrossingIsClosing";
    public const string LevelCrossingIsOpening = "Do67.LevelCrossingIsOpening";
    public const string LevelCrossingOccupied = "Do67.LevelCrossingOccupied";

    // Andere
    public const string OverlapSymbols = "Do67.OverlapSymbols";
    public const string ReleaseTrigger = "Do67.ReleaseTrigger";
    public const string SwitchReleaseToRequiredPosition = "Do67.SwitchReleaseToRequiredPosition";
}
