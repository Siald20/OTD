namespace OTD.TrackPlan;

public enum TrackSymbolKind
{
    Track,
    TrackBlock,
    LineBlock,
    Signal,
    ZwergSignal,
    Switch,
    DoubleSlipSwitch,
    Crossing,
    BufferStop,
    Sensor,
    Platform,
    LevelCrossing,
    TunnelPortal,
    Bridge,
    Uncoupler,
    Depot,
    Turntable,
    TextLabel
}

public enum SignalDirection
{
    Both,
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop
}

public enum SwitchPosition
{
    Straight,
    Diverging,
    Left,
    Right
}

public enum RouteSearchMode
{
    Shortest,
    PreferStraightSwitches,
    MinimizeSwitchChanges
}

public enum RouteType
{
    Train,
    Shunting
}
