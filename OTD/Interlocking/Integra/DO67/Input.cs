// Repräsentiert ein Eingangssignal in eine Baugruppe (z.B. von einer Taste oder einem Gleis)
public class Input
{
    private bool m_value;

    public Input(bool initVal = false)
    {
        m_value = initVal;
    }

    public bool Value
    {
        get => m_value;
        set => m_value = value;
    }
}

// Repräsentiert ein Ausgangssignal aus einer Baugruppe (z.B. zu einer Lampe oder einem anderen Block)
public class Output
{
    private bool m_value;

    public Output(bool initVal = false)
    {
        m_value = initVal;
    }

    public bool Value
    {
        get => m_value;
        set => m_value = value;
    }
}

// Repräsentiert eine physische oder virtuelle Taste auf dem Stelltisch
public class Taste
{
    private bool m_gedrueckt;

    public Taste(bool initVal = false)
    {
        m_gedrueckt = initVal;
    }

    public bool Value
    {
        get => m_gedrueckt;
        set => m_gedrueckt = value;
    }
}
